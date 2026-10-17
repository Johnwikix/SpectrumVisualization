using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinExSpectrumTest.Effects.Folia;
using WinExSpectrumTest.Services.Lyrics;

namespace WinExSpectrumTest.Services;

/// <summary>Active-effect scoped player/cache/network lookup. I/O, parsing and cover decoding stay off render / UI threads.</summary>
internal sealed class FoliaPlaybackService
{
    private readonly MediaInfoService _media;
    private readonly OriginalSoundLyricsSource _playerLyrics;
    private readonly LyricsOnlineSearch _onlineLyrics;
    private readonly Func<IRandomAccessStreamReference?, CancellationToken, Task<(byte[] Bytes, int Width, int Height)>> _decodeCover;
    private readonly object _gate = new();
    private readonly HashSet<Task> _operations = [];
    private CancellationTokenSource? _request;
    private CancellationTokenSource? _coverRequest;
    private IRandomAccessStreamReference? _thumbnail;
    private long _coverVersion;
    private bool _active, _stopped;
    private long _version;
    private string _trackKey = "";
    private string _songKey = "";
    private string _status = "waiting";
    internal FoliaPlaybackState State { get; } = new();
    internal string Status { get { lock (_gate) return _status; } }
    internal bool IsActive { get { lock (_gate) return _active && !_stopped; } }
    internal event Action? Changed;
    internal static bool PreviewEnabled => Environment.GetEnvironmentVariable("SPECTRUM_FOLIA_PREVIEW") == "1";

    internal FoliaPlaybackService(MediaInfoService media, OriginalSoundLyricsSource? playerLyrics = null, LyricsOnlineSearch? onlineLyrics = null,
        Func<IRandomAccessStreamReference?, CancellationToken, Task<(byte[] Bytes, int Width, int Height)>>? decodeCover = null)
    {
        _media = media;
        _playerLyrics = playerLyrics ?? new();
        _onlineLyrics = onlineLyrics ?? new();
        _decodeCover = decodeCover ?? DecodeCoverAsync;
    }

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
                _coverRequest?.Cancel();
                _coverVersion++;
                _thumbnail = null;
                _version++;
                _trackKey = ""; // Re-read player preferences and lyrics when this effect is activated again.
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
            var snapshot = _media.CurrentTrack;
            string title = snapshot.Title?.Trim() ?? "";
            string artist = snapshot.Artist?.Trim() ?? "";
            var track = new LyricsTrack(title, artist, snapshot.Album?.Trim() ?? "", _media.Duration.TotalSeconds);
            string sourceId = snapshot.SourceAppId ?? "";
            string key = sourceId + "\n" + track.CacheKey;
            if (!retry && key == _trackKey)
            {
                RequestCover(snapshot.Thumbnail, _version);
                return;
            }
            _trackKey = key;
            _request?.Cancel();
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            _request = cancellation;
            long version = ++_version;
            // Re-entering the effect / retrying the same song retains its decoded cover and published lyrics.
            State.Publish((_songKey == key ? State.Song : FoliaSong.Empty) with { Title = title, Artist = artist });
            _songKey = key;
            _thumbnail = null;
            _coverRequest?.Cancel();
            _coverVersion++;
            RequestCover(snapshot.Thumbnail, version);
            _status = title.Length == 0 ? "waiting" : "loading";
            if (title.Length != 0)
            {
                // Task.Run also keeps any synchronous parser, image decode and search setup off UI.
                TrackOperation(Task.Run(() => ResolveAsync(track, sourceId, retry, version, cancellation)));
            }
            else
            {
                cancellation.Dispose();
                _request = null;
            }
        }
        Changed?.Invoke();
    }

    private void TrackOperation(Task task)
    {
        _operations.Add(task);
        _ = task.ContinueWith(static (completed, state) =>
        {
            var owner = (FoliaPlaybackService)state!;
            lock (owner._gate) owner._operations.Remove(completed);
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    // Called under _gate. Same-song thumbnail updates have their own cancellation/epoch and never restart lyrics.
    private void RequestCover(IRandomAccessStreamReference? thumbnail, long version)
    {
        if (thumbnail is null || ReferenceEquals(_thumbnail, thumbnail)) return;
        _thumbnail = thumbnail;
        _coverRequest?.Cancel();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _coverRequest = cancellation;
        long coverVersion = ++_coverVersion;
        TrackOperation(Task.Run(async () =>
        {
            try
            {
                var cover = await _decodeCover(thumbnail, cancellation.Token).ConfigureAwait(false);
                cancellation.Token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (!_active || _stopped || version != _version || coverVersion != _coverVersion || cover.Width <= 0 || cover.Height <= 0) return;
                    var song = State.Song;
                    if (song.CoverWidth == cover.Width && song.CoverHeight == cover.Height && song.CoverRgba.AsSpan().SequenceEqual(cover.Bytes)) return;
                    State.Publish(song with { CoverRgba = cover.Bytes, CoverWidth = cover.Width, CoverHeight = cover.Height });
                }
                Changed?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { App.WriteCrashLog("Folia cover", ex.Message, ex); }
            finally
            {
                lock (_gate) if (ReferenceEquals(_coverRequest, cancellation)) _coverRequest = null;
                cancellation.Dispose();
            }
        }));
    }

    private async Task ResolveAsync(LyricsTrack track, string sourceId, bool retry, long version, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            await Task.Delay(120, token).ConfigureAwait(false);
            // Session metadata can arrive before its timeline; take the duration after coalescing those updates.
            track = track with { Duration = _media.Duration.TotalSeconds };
            var player = await _playerLyrics.ReadAsync(track, sourceId, token).ConfigureAwait(false);
            var content = player.Content ?? ((!retry || !player.AllowOnline) && player.AllowCache
                ? await LyricsDiskCache.ReadAsync(track, token).ConfigureAwait(false) : null);
            var status = LyricsSearchStatus.NoResult;
            if (content is null && player.AllowOnline)
            {
                var online = await _onlineLyrics.SearchAsync(track, retry, token).ConfigureAwait(false);
                content = online.Content;
                status = online.Status;
                if (content is not null) await LyricsDiskCache.WriteAsync(track, content, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            var lines = content is null ? [] : FoliaSong.Parse(content.Original, content.Translation, track.Duration);
            // The player's positive offset delays its lyric clock; shift this immutable snapshot equivalently.
            double offset = player.OffsetMs / 1000d;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (offset != 0)
                {
                    for (int w = 0; w < line.Words.Length; w++)
                        line.Words[w] = line.Words[w] with { Start = line.Words[w].Start + offset, End = line.Words[w].End + offset };
                }
                if (offset != 0 || !player.ShowTranslation)
                    lines[i] = line with { Start = line.Start + offset, End = line.End + offset, Translation = player.ShowTranslation ? line.Translation : "" };
            }
            lock (_gate)
            {
                if (!_active || _stopped || version != _version) return;
                State.Publish(State.Song with { Lines = lines });
                _status = lines.Length > 0 ? "ready" : status == LyricsSearchStatus.NetworkError ? "failed" : "missing";
            }
            Changed?.Invoke();
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
            _coverRequest?.Cancel();
            _coverVersion++;
            _version++;
            pending = [.. _operations];
        }
        Changed?.Invoke();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        finally
        {
            lock (_gate)
            {
                _operations.Clear();
                _request = null;
                _coverRequest = null;
                _thumbnail = null;
                _trackKey = "";
                _songKey = "";
                State.Publish(FoliaSong.Empty);
                State.SetClock(0, 0, false);
            }
        }
    }
}
