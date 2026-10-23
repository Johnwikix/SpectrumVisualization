using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage.Streams;
using WinExSpectrumTest.Services;

namespace WinExSpectrumTest.ViewModel;

/// <summary>SDR media overlay; all bindings and bitmap operations stay on the UI thread.</summary>
public sealed partial class SonicMediaViewModel : ObservableObject
{
    private readonly MediaInfoService _media;
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _timeline = new();
    private TimeSpan _position;
    private TimeSpan _duration;
    private bool _playing;
    private bool _active;
    private bool _stopped;
    private int _coverVersion;
    private int _attemptedCoverVersion = -1;
    private IRandomAccessStreamReference? _thumbnail;
    private Task? _coverTask;
    private readonly CancellationTokenSource _stop = new();
    private Task? _stopTask;
    private int _lastSecond = -1;

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string Artist { get; set; } = "";
    [ObservableProperty] public partial string TimeText { get; set; } = "";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial double CardOpacity { get; set; }
    [ObservableProperty] public partial BitmapImage? Cover { get; set; }

    public SonicMediaViewModel(MediaInfoService media, DispatcherQueue dispatcher)
    {
        _media = media;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100);
        _timer.Tick += OnTick;
    }

    public void SetActive(bool active)
    {
        if (_stopped || _active == active) return;
        _active = active;
        if (active)
        {
            Refresh();
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            CardOpacity = 0;
            ++_coverVersion;
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args) => Refresh();

    private void Refresh()
    {
        if (!_active || _stopped) return;
        Title = _media.CurrentTitle ?? "";
        Artist = _media.CurrentArtist ?? "";
        bool playing = _media.IsPlaying;
        if (_position != _media.Position || _duration != _media.Duration || _playing != playing)
        {
            _position = _media.Position;
            _duration = _media.Duration;
            _playing = playing;
            _timeline.Restart();
            if (!playing) _timeline.Stop();
            _lastSecond = -1;
        }
        CardOpacity = playing && (Title.Length > 0 || Artist.Length > 0) ? 1 : 0;
        double duration = Math.Max(0, _duration.TotalSeconds);
        double current = Math.Clamp(_position.TotalSeconds + (_playing ? _timeline.Elapsed.TotalSeconds : 0), 0, duration);
        Progress = duration > 0 ? current / duration * 100 : 0;
        int second = (int)current;
        if (_lastSecond != second)
        {
            _lastSecond = second;
            TimeText = duration > 0 ? $"{FormatTime(current)} / {FormatTime(duration)}" : "";
        }
        if (!ReferenceEquals(_thumbnail, _media.CurrentThumbnail))
        {
            _thumbnail = _media.CurrentThumbnail;
            ++_coverVersion;
            Cover = null;
            if (_coverTask == null || _coverTask.IsCompleted) _coverTask = LoadCoverAsync();
        }
        else if (Cover == null && _thumbnail != null && _attemptedCoverVersion != _coverVersion && (_coverTask == null || _coverTask.IsCompleted))
        {
            _coverTask = LoadCoverAsync();
        }
    }

    private async Task LoadCoverAsync()
    {
        var token = _stop.Token;
        // One decoder at a time. New tracks supersede old results without parallel decode storms.
        while (_active && !_stopped && _thumbnail != null)
        {
            int version = _coverVersion;
            _attemptedCoverVersion = version;
            var thumbnail = _thumbnail;
            try
            {
                using var stream = await AwaitCoverAsync(thumbnail.OpenReadAsync(), token);
                if (!_active || _stopped) return;
                if (version != _coverVersion) continue;
                var bitmap = new BitmapImage { DecodePixelWidth = 256 };
                await AwaitCoverAsync(bitmap.SetSourceAsync(stream), token);
                if (version == _coverVersion && _active && !_stopped)
                {
                    Cover = bitmap;
                    return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                App.WriteCrashLog("Sonic artwork", ex.Message, ex);
                return;
            }
        }
    }

    // Cancel the WinRT operation, but still await its completion callback before
    // disposing the stream. Cancelling only a Task wait can race the decoder.
    private static async Task<T> AwaitCoverAsync<T>(IAsyncOperation<T> operation, CancellationToken token)
    {
        using var registration = token.Register(CancelCoverOperation, operation);
        return await operation;
    }

    private static async Task AwaitCoverAsync(IAsyncAction operation, CancellationToken token)
    {
        using var registration = token.Register(CancelCoverOperation, operation);
        await operation;
    }

    private static void CancelCoverOperation(object? operation)
    {
        try { ((IAsyncInfo)operation!).Cancel(); }
        catch (Exception ex) { App.WriteCrashLog("Sonic artwork cancellation", ex.Message, ex); }
    }

    private static string FormatTime(double seconds)
    {
        int total = (int)Math.Max(0, seconds);
        return $"{total / 60}:{total % 60:00}";
    }

    public Task StopAsync() => _stopTask ??= StopCoreAsync();

    private async Task StopCoreAsync()
    {
        _stopped = true;
        _active = false;
        ++_coverVersion;
        _timer.Stop();
        _timer.Tick -= OnTick;
        try
        {
            _stop.Cancel();
            if (_coverTask != null) await _coverTask;
        }
        finally
        {
            Cover = null;
            _thumbnail = null;
            _coverTask = null;
            _stop.Dispose();
        }
    }
}
