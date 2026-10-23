using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Retains the last native line for AnimatePresence-style exits without retaining an atlas owner.</summary>
internal sealed class FoliaLineHandoff(int capacity = 8192)
{
    private readonly FoliaInstance[] _last = new FoliaInstance[capacity];
    private readonly FoliaInstance[] _exit = new FoliaInstance[capacity];
    private int _lastCount, _exitCount, _line = -1;
    private double _position = double.NaN, _enterTime, _exitTime;
    private FoliaLineTiming _timing, _exitTiming;

    internal void Compose(ref FoliaPainter painter, Span<FoliaInstance> output, int line, double time, double elapsed, FoliaLineTiming timing)
    {
        if (double.IsNaN(_position) || Math.Abs(time - _position) > .5)
        {
            _lastCount = _exitCount = 0;
            _line = -1;
        }
        _position = time;
        if (_line != line)
        {
            _last.AsSpan(0, _lastCount).CopyTo(_exit);
            _exitCount = _lastCount;
            _exitTiming = _timing;
            _exitTime = elapsed;
            _enterTime = elapsed;
            _line = line;
        }
        _timing = timing;
        float enterDuration = timing.Mode == 2 ? 0 : timing.Mode == 1 ? .16f : .3f;
        float enter = enterDuration == 0 ? 1 : FoliaMotion.Bezier(0, 0, .58f, 1, (float)(elapsed - _enterTime) / enterDuration);
        float initial = timing.Mode == 1 ? .96f : .9f;
        painter.TransformFrom(0, new(painter.Width * .5f, painter.Height * .5f), new(painter.Width * .5f, painter.Height * .5f), initial + (1 - initial) * enter, 0);
        painter.FadeFrom(0, (timing.Mode == 1 ? .35f : 0) + (timing.Mode == 1 ? .65f : 1) * enter,
            (timing.Mode == 1 ? 4 : 10) * (1 - enter));
        _lastCount = Math.Min(painter.Count, _last.Length);
        output[.._lastCount].CopyTo(_last);
        float duration = _exitTiming.Mode == 2 ? .12f : _exitTiming.Mode == 1 ? .16f : .3f;
        float progress = Math.Clamp((float)(elapsed - _exitTime) / duration, 0, 1);
        if (_exitCount > 0 && progress < 1)
        {
            int first = painter.Count;
            painter.Append(_exit.AsSpan(0, _exitCount));
            float ease = FoliaMotion.Bezier(.42f, 0, .58f, 1, progress);
            float growth = _exitTiming.Mode == 2 ? .02f : _exitTiming.Mode == 1 ? .04f : .1f;
            painter.TransformFrom(first, new(painter.Width * .5f, painter.Height * .5f), new(painter.Width * .5f, painter.Height * .5f), 1 + growth * ease, 0);
            painter.FadeFrom(first, 1 - ease, (_exitTiming.Mode == 2 ? 6 : _exitTiming.Mode == 1 ? 10 : 20) * ease);
        }
        else
            _exitCount = 0;
    }
}
