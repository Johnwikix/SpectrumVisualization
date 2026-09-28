using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Globalization;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.ViewModel;

/// <summary>Low-frequency UI projection; formatting never runs on either render thread.</summary>
public sealed class RenderDebugViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherQueueTimer _timer;
    private readonly ResourceLoader _resources = new();
    private bool _active;
    private string _text = "";
    internal RenderDebugStatistics Statistics { get; } = new();
    public string Text { get => _text; private set => SetProperty(ref _text, value); }
    public Visibility Visibility => _active ? Visibility.Visible : Visibility.Collapsed;

    public RenderDebugViewModel()
    {
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += OnTick;
    }

    internal void SetActive(bool active)
    {
        if (_active == active) return;
        _active = active;
        Statistics.SetEnabled(active);
        if (active)
        {
            Text = _resources.GetString("RenderDebugWaiting");
            _timer.Start();
        }
        else _timer.Stop();
        OnPropertyChanged(nameof(Visibility));
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_active) return;
        var sample = Statistics.Sample();
        if (sample.Frames == 0) { Text = _resources.GetString("RenderDebugWaiting"); return; }
        var frame = sample.Last;
        double seconds = Math.Max(.001, sample.Seconds);
        string limit = frame.Limit == 0 ? _resources.GetString("RefreshRateUnlimited") : $"{frame.Limit:0.##} Hz";
        if (!frame.Gpu)
        {
            Text = string.Format(CultureInfo.CurrentCulture, _resources.GetString("RenderDebugWin2D"),
                sample.Frames / seconds, sample.CpuMilliseconds, limit);
            return;
        }
        string aa = frame.Mode == ReconstructionMode.Dlss ? $"DLSS {frame.DlssPreset?.ToUpperInvariant()}" : frame.Mode.ToString();
        Text = string.Format(CultureInfo.CurrentCulture, _resources.GetString("RenderDebugGpu"),
            sample.Frames / seconds, sample.Submissions / seconds, sample.Dropped,
            sample.CpuMilliseconds, sample.PresentMilliseconds, sample.WaitMilliseconds,
            frame.Size.Width, frame.Size.Height, frame.Size.OutputWidth, frame.Size.OutputHeight,
            frame.Hdr ? "HDR10" : "SDR", aa, limit,
            _resources.GetString(frame.Tearing ? "RenderDebugTearing" : "RenderDebugComposed"));
    }

    public void Dispose()
    {
        SetActive(false);
        _timer.Tick -= OnTick;
    }
}
