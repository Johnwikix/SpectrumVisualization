using System.Diagnostics;
using System.Threading;

namespace WinExSpectrumTest.Rendering;

internal readonly record struct RenderDebugFrame(bool Gpu, bool Submitted, double CpuMilliseconds,
    double PresentMilliseconds, double WaitMilliseconds, GpuRenderSize Size, ReconstructionMode Mode,
    string? DlssPreset, bool Hdr, bool Tearing, float Limit);

internal readonly record struct RenderDebugSnapshot(long Frames, long Submissions, long Dropped, double Seconds,
    double CpuMilliseconds, double PresentMilliseconds, double WaitMilliseconds, RenderDebugFrame Last);

/// <summary>Render threads only write values; UI owns sampling and text. Disabled means no sampling work.</summary>
internal sealed class RenderDebugStatistics
{
    private readonly object _gate = new();
    private bool _enabled;
    private long _started, _frames, _submissions, _dropped;
    private double _cpu, _present, _wait;
    private RenderDebugFrame _last;
    internal bool Enabled => Volatile.Read(ref _enabled);

    internal void SetEnabled(bool value)
    {
        lock (_gate)
        {
            Volatile.Write(ref _enabled, value);
            Reset();
            _last = default;
        }
    }

    internal void Record(in RenderDebugFrame frame)
    {
        if (!Enabled) return;
        lock (_gate)
        {
            if (!_enabled) return;
            _frames++;
            if (frame.Gpu)
            {
                if (frame.Submitted) _submissions++;
                else _dropped++;
            }
            _cpu += frame.CpuMilliseconds;
            _present += frame.PresentMilliseconds;
            _wait += frame.WaitMilliseconds;
            _last = frame;
        }
    }

    internal RenderDebugSnapshot Sample()
    {
        lock (_gate)
        {
            long count = System.Math.Max(1, _frames);
            var snapshot = new RenderDebugSnapshot(_frames, _submissions, _dropped,
                Stopwatch.GetElapsedTime(_started).TotalSeconds, _cpu / count, _present / count, _wait / count, _last);
            Reset();
            return snapshot;
        }
    }

    private void Reset()
    {
        _started = Stopwatch.GetTimestamp();
        _frames = _submissions = _dropped = 0;
        _cpu = _present = _wait = 0;
    }
}
