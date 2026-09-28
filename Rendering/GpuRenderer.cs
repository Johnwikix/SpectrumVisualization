using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.Rendering;

internal readonly record struct GpuRenderSettings(int Width, int Height, bool Active, bool Hdr, float WhiteNits, float PeakNits,
    float FramesPerSecond, EffectDescriptor Effect, GpuSceneOptions Scene);

/// <summary>Owns the GPU host and active effect on one explicitly started worker.</summary>
internal sealed class GpuRenderer
{
    private readonly SpectrumAnalyzer _analyzer;
    private readonly nint _hwnd;
    private readonly Action<nint> _bind;
    private readonly Action<HdrOutputMode> _publish;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private GpuRenderSettings _settings;
    private bool _stopping;
    private HdrOutputMode? _lastStatus;
    private TaskCompletionSource? _paused;

    public GpuRenderer(SpectrumAnalyzer analyzer, nint hwnd, Action<nint> bind, Action<HdrOutputMode> publish, GpuRenderSettings settings)
    {
        _analyzer = analyzer;
        _hwnd = hwnd;
        _bind = bind;
        _publish = publish;
        _settings = settings;
    }

    public void Start()
    {
        try { new Thread(Run) { IsBackground = true, Name = "Spectrum D3D12" }.Start(); }
        catch
        {
            lock (_gate)
            {
                _stopping = true;
                _wake.Dispose();
            }
            _stopped.TrySetResult();
            throw;
        }
    }

    public void Configure(GpuRenderSettings settings)
    {
        lock (_gate)
        {
            if (_stopping) return;
            _settings = settings;
            _wake.Set();
        }
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (!_stopping)
            {
                _stopping = true;
                _wake.Set();
            }
        }
        return _stopped.Task;
    }

    public Task PauseAsync()
    {
        lock (_gate)
        {
            if (_stopping) return _stopped.Task;
            _settings = _settings with { Active = false };
            _paused ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _wake.Set();
            return _paused.Task;
        }
    }

    private void Publish(HdrOutputMode mode)
    {
        if (_lastStatus == mode) return;
        _lastStatus = mode;
        _publish(mode);
    }

    private void Run()
    {
        GpuGraphics? graphics = null;
        GpuFramePacer? pacer = null;
        string? activeEffect = null;
        long previous = Stopwatch.GetTimestamp();
        long displayChecked = 0;
        DisplayOutput display = default;
        bool failureLogged = false;
        bool? previousRequest = null;
        bool hdr = false;
        try
        {
            pacer = new GpuFramePacer(_wake);
            while (true)
            {
                GpuRenderSettings settings;
                lock (_gate)
                {
                    if (_stopping) break;
                    settings = _settings;
                    if (!settings.Active)
                    {
                        _paused?.TrySetResult();
                        _paused = null;
                    }
                }
                if (!settings.Active || settings.Width <= 0 || settings.Height <= 0)
                {
                    _wake.WaitOne();
                    previous = Stopwatch.GetTimestamp();
                    pacer.Reset();
                    displayChecked = 0;
                    continue;
                }
                try
                {
                    if (graphics == null)
                    {
                        Publish(HdrOutputMode.Starting);
                        graphics = new GpuGraphics(_analyzer, _bind, settings.Width, settings.Height, settings.Effect.CreateGpu!, settings.Scene);
                        activeEffect = settings.Effect.Id;
                        pacer.Reset();
                        previous = Stopwatch.GetTimestamp();
                        displayChecked = 0;
                        previousRequest = null;
                    }
                    graphics.Resize(settings.Width, settings.Height);
                    graphics.Configure(settings.Scene);
                    if (activeEffect != settings.Effect.Id)
                    {
                        graphics.ChangeEffect(settings.Effect.CreateGpu!);
                        activeEffect = settings.Effect.Id;
                    }
                    bool probeDisplay = Environment.TickCount64 - displayChecked >= 500;
                    if (probeDisplay)
                    {
                        display = graphics.QueryDisplay(_hwnd);
                        displayChecked = Environment.TickCount64;
                    }
                    bool requested = settings.Hdr && display.HdrEnabled;
                    if (probeDisplay || previousRequest != requested)
                    {
                        hdr = graphics.SetHdr(requested);
                        previousRequest = requested;
                    }
                    Publish(!settings.Hdr ? HdrOutputMode.Disabled : hdr ? HdrOutputMode.Active : HdrOutputMode.Unavailable);
                    long now = Stopwatch.GetTimestamp();
                    double elapsed = Stopwatch.GetElapsedTime(previous, now).TotalSeconds;
                    previous = now;
                    if (graphics.Render(elapsed, settings.WhiteNits, settings.PeakNits))
                        RenderDiagnostics.RecordFrame(activeEffect, _analyzer.PublicationCount);
                    failureLogged = false;
                }
                catch (Exception ex)
                {
                    Publish(HdrOutputMode.Failed);
                    if (!failureLogged) App.WriteCrashLog("GPU renderer", ex.Message, ex);
                    failureLogged = true;
                    try { graphics?.Dispose(); }
                    catch (Exception cleanup) { App.WriteCrashLog("GPU recovery", cleanup.Message, cleanup); }
                    graphics = null;
                    // Device recreation owns recovery. A settings change or shutdown wakes this backoff.
                    _wake.WaitOne(2000);
                    previous = Stopwatch.GetTimestamp();
                    pacer.Reset();
                    continue;
                }
                double fps = float.IsFinite(settings.FramesPerSecond) ? Math.Clamp(settings.FramesPerSecond, 1, 120) : 60;
                pacer.Wait(fps);
            }
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("GPU worker", ex.Message, ex);
            Publish(HdrOutputMode.Failed);
        }
        finally
        {
            pacer?.Dispose();
            try { graphics?.Dispose(); }
            catch (Exception ex) { App.WriteCrashLog("GPU shutdown", ex.Message, ex); }
            // Configure/Stop are serialized with disposal of the wake handle.
            lock (_gate)
            {
                _stopping = true;
                _paused?.TrySetResult();
                _wake.Dispose();
            }
            _stopped.TrySetResult();
        }
    }
}
