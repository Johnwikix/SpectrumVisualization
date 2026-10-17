using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinExSpectrumTest.Effects.Folia;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

namespace WinExSpectrumTest.Services;

/// <summary>Active-effect scoped QQ lookup. Requests, cancellation and cover decoding stay off the render / UI threads.</summary>
internal sealed class FoliaPlaybackService
{
    private readonly MediaInfoService _media;
    private readonly object _gate = new();
    private readonly HashSet<Task> _operations = [];
    private CancellationTokenSource? _request;
    private bool _active, _stopped;
    private long _version;
    private string _trackKey = "";
    private string _status = "waiting";
    internal FoliaPlaybackState State { get; } = new();
    internal string Status { get { lock (_gate) return _status; } }
    internal bool IsActive { get { lock (_gate) return _active && !_stopped; } }
    internal event Action? Changed;
    internal static bool PreviewEnabled => Environment.GetEnvironmentVariable("SPECTRUM_FOLIA_PREVIEW") == "1";

    internal FoliaPlaybackService(MediaInfoService media) => _media = media;

    internal void SetActive(bool active)
    {
        lock (_gate)
        {
            if (_stopped || _active == active) return;
            _active = active;
            if (active)
            {
                if (PreviewEnabled)
                {
                    State.Publish(FoliaSong.Preview());
                    State.SetClock(17.6, 64, false);
                    _status = "ready";
                }
                else
                {
                    _media.MediaTextChanged += OnMediaChanged;
                    _media.PlaybackChanged += OnPlaybackChanged;
                    _media.TimelineChanged += OnTimelineChanged;
                    State.SetClock(_media.Position.TotalSeconds, _media.Duration.TotalSeconds, _media.IsPlaying);
                }
            }
            else
            {
                _media.MediaTextChanged -= OnMediaChanged;
                _media.PlaybackChanged -= OnPlaybackChanged;
                _media.TimelineChanged -= OnTimelineChanged;
                _request?.Cancel();
                _version++;
                if (_status == "loading") _trackKey = "";
            }
        }
        if (active && !PreviewEnabled) RequestCurrent(false);
        Changed?.Invoke();
    }

    internal void Retry() => RequestCurrent(true);
    private void OnMediaChanged(string? title, string? artist, IRandomAccessStreamReference? thumbnail) => RequestCurrent(false);
    private void OnPlaybackChanged(bool playing)
    {
        lock (_gate)
        {
            if (!_active || _stopped) return;
            var clock = State.Clock;
            State.SetClock(clock.Now, clock.Duration, playing);
        }
    }
    private void OnTimelineChanged(TimeSpan position, TimeSpan duration)
    {
        lock (_gate)
        {
            if (!_active || _stopped) return;
            State.SetClock(position.TotalSeconds, duration.TotalSeconds, _media.IsPlaying);
        }
    }

    private void RequestCurrent(bool retry)
    {
        lock (_gate)
        {
            if (!_active || _stopped || PreviewEnabled) return;
            string title = _media.CurrentTitle?.Trim() ?? "";
            string artist = _media.CurrentArtist?.Trim() ?? "";
            string key = title + "\n" + artist;
            if (!retry && key == _trackKey) return;
            _trackKey = key;
            _request?.Cancel();
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            _request = cancellation;
            long version = ++_version;
            State.Publish(FoliaSong.Empty with { Title = title, Artist = artist });
            _status = title.Length == 0 ? "waiting" : "loading";
            if (title.Length != 0)
            {
                // Task.Run also keeps any synchronous parser, image decode and search setup off UI.
                double duration = _media.Duration.TotalSeconds;
                var thumbnail = _media.CurrentThumbnail;
                Task task = Task.Run(() => ResolveAsync(title, artist, duration, thumbnail, version, cancellation));
                _operations.Add(task);
                _ = task.ContinueWith(static (completed, state) =>
                {
                    var owner = (FoliaPlaybackService)state!;
                    lock (owner._gate) owner._operations.Remove(completed);
                }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else
            {
                cancellation.Dispose();
                _request = null;
            }
        }
        Changed?.Invoke();
    }

    private async Task ResolveAsync(string title, string artist, double duration, IRandomAccessStreamReference? thumbnail,
        long version, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            var cover = await DecodeCoverAsync(thumbnail, token).ConfigureAwait(false);
            Publish(version, FoliaSong.Empty with { Title = title, Artist = artist, CoverRgba = cover.Bytes, CoverWidth = cover.Width, CoverHeight = cover.Height }, "loading");
            var track = new TrackMultiArtistMetadata
            {
                Title = title,
                Artists = [.. artist.Split([" / ", ";", ", ", "、", " & "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                DurationMs = duration > 0 ? (int)Math.Min(int.MaxValue, duration * 1000) : null
            };
            var match = await new QQMusicSearcher().SearchForResult(track, CompareHelper.MatchType.High, token).ConfigureAwait(false) as QQMusicSearchResult;
            token.ThrowIfCancellationRequested();
            if (match == null)
            {
                Publish(version, State.Song, "missing");
                return;
            }
            string original = "", translated = "";
            try
            {
                var lyrics = await ProviderHelper.QQMusicApi.GetLyricsAsync(match.Id, token).ConfigureAwait(false);
                original = lyrics?.Lyrics ?? "";
                translated = lyrics?.Trans ?? "";
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // QRC can be unavailable for a track even when its line-synced LRC exists.
            }
            token.ThrowIfCancellationRequested();
            double trackDuration = duration > 0 ? duration : (match.DurationMs ?? 0) / 1000d;
            var lines = FoliaSong.Parse(original, translated, trackDuration);
            if (lines.Length == 0)
            {
                var lyrics = await ProviderHelper.QQMusicApi.GetLyric(match.Mid, token).ConfigureAwait(false);
                lines = FoliaSong.Parse(lyrics?.Lyric ?? "", lyrics?.Trans, trackDuration);
            }
            Publish(version, new(title, artist, lines, cover.Bytes, cover.Width, cover.Height, FoliaSong.Empty.Palette), lines.Length == 0 ? "missing" : "ready");
        }
        catch (OperationCanceledException)
        {
            // A deadline is recoverable; an obsolete track never changes the new song's state.
            Publish(version, null, "failed");
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("Folia lyrics", ex.Message, ex);
            Publish(version, null, "failed");
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_request, cancellation)) _request = null;
            cancellation.Dispose();
        }
    }

    private void Publish(long version, FoliaSong? song, string status)
    {
        lock (_gate)
        {
            if (_stopped || !_active || version != _version) return;
            if (song != null) State.Publish(song);
            _status = status;
        }
        Changed?.Invoke();
    }

    private static async Task<(byte[] Bytes, int Width, int Height)> DecodeCoverAsync(IRandomAccessStreamReference? reference, CancellationToken token)
    {
        if (reference == null) return ([], 0, 0);
        try
        {
            using var stream = await reference.OpenReadAsync().AsTask(token).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
            double scale = Math.Min(1, 512d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            uint width = Math.Max(1, (uint)(decoder.PixelWidth * scale));
            uint height = Math.Max(1, (uint)(decoder.PixelHeight * scale));
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Ignore,
                new BitmapTransform { ScaledWidth = width, ScaledHeight = height }, ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb).AsTask(token).ConfigureAwait(false);
            return (pixels.DetachPixelData(), (int)width, (int)height);
        }
        catch (Exception) when (!token.IsCancellationRequested) { return ([], 0, 0); }
    }

    internal async Task StopAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            _stopped = true;
            _active = false;
            _media.MediaTextChanged -= OnMediaChanged;
            _media.PlaybackChanged -= OnPlaybackChanged;
            _media.TimelineChanged -= OnTimelineChanged;
            _request?.Cancel();
            _version++;
            pending = [.. _operations];
        }
        Changed?.Invoke();
        await Task.WhenAll(pending).ConfigureAwait(false);
    }
}
