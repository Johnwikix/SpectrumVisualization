using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.Rendering;

internal readonly record struct SonicRenderSettings(int Width, int Height, bool Active, bool Hdr, float WhiteNits, float PeakNits, float FramesPerSecond);

/// <summary>One explicitly started worker; only this worker owns SonicGraphics and its effects.</summary>
internal sealed class SonicRenderer
{
    private readonly SpectrumAnalyzer _analyzer;
    private readonly nint _hwnd;
    private readonly Action<nint> _bind;
    private readonly Action<HdrOutputMode> _publish;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SonicRenderSettings _settings;
    private bool _stopping;
    private HdrOutputMode? _lastStatus;
    private TaskCompletionSource? _paused;

    public SonicRenderer(SpectrumAnalyzer analyzer, nint hwnd, Action<nint> bind, Action<HdrOutputMode> publish, SonicRenderSettings settings)
    {
        _analyzer = analyzer;
        _hwnd = hwnd;
        _bind = bind;
        _publish = publish;
        _settings = settings;
    }

    public void Start()
    {
        try { new Thread(Run) { IsBackground = true, Name = "Sonic D3D12" }.Start(); }
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

    public void Configure(SonicRenderSettings settings)
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
        SonicGraphics? graphics = null;
        long previous = Stopwatch.GetTimestamp();
        long displayChecked = 0;
        DisplayOutput display = default;
        bool failureLogged = false;
        bool? previousRequest = null;
        bool hdr = false;
        try
        {
            while (true)
            {
                SonicRenderSettings settings;
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
                    displayChecked = 0;
                    continue;
                }
                long started = Stopwatch.GetTimestamp();
                try
                {
                    if (graphics == null)
                    {
                        Publish(HdrOutputMode.Starting);
                        graphics = new SonicGraphics(_analyzer, _bind, settings.Width, settings.Height);
                        previous = Stopwatch.GetTimestamp();
                        displayChecked = 0;
                        previousRequest = null;
                    }
                    graphics.Resize(settings.Width, settings.Height);
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
                    graphics.Render(elapsed, settings.WhiteNits, settings.PeakNits);
                    RenderDiagnostics.RecordFrame(SonicTopographyEffect.EffectId, _analyzer.PublicationCount);
                    failureLogged = false;
                }
                catch (Exception ex)
                {
                    Publish(HdrOutputMode.Failed);
                    if (!failureLogged) App.WriteCrashLog("Sonic renderer", ex.Message, ex);
                    failureLogged = true;
                    try { graphics?.Dispose(); }
                    catch (Exception cleanup) { App.WriteCrashLog("Sonic recovery", cleanup.Message, cleanup); }
                    graphics = null;
                    // Device recreation owns recovery. A settings change or shutdown wakes this backoff.
                    _wake.WaitOne(2000);
                    previous = Stopwatch.GetTimestamp();
                    continue;
                }
                double fps = float.IsFinite(settings.FramesPerSecond) ? Math.Clamp(settings.FramesPerSecond, 1, 120) : 60;
                double remaining = 1000d / fps
                    - Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (remaining > 0) _wake.WaitOne((int)Math.Ceiling(remaining));
            }
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("Sonic worker", ex.Message, ex);
            Publish(HdrOutputMode.Failed);
        }
        finally
        {
            try { graphics?.Dispose(); }
            catch (Exception ex) { App.WriteCrashLog("Sonic shutdown", ex.Message, ex); }
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
