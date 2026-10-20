using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Owns Cappella's bounded per-message spring state on the render worker.</summary>
internal sealed class FoliaChatMotion(int count)
{
    private struct Row { internal bool Ready; internal Vector4 Rect, Velocity; internal float Alpha; }
    private readonly Row[] _rows = new Row[count];
    private double _lastTime = double.NaN;
    internal float Delta;

    internal void Begin(double time)
    {
        if (!double.IsNaN(_lastTime) && Math.Abs(time - _lastTime) > .5)
            _rows.AsSpan().Clear();
        _lastTime = time;
    }

    internal void Apply(ref FoliaPainter painter, int first, int index, Vector4 target, bool visible)
    {
        ref var row = ref _rows[index];
        if (!row.Ready)
            row = new() { Ready = true, Rect = target + new Vector4(0, 24, 0, 0), Alpha = 0 };
        float dt = Math.Clamp(Delta, 0, .1f);
        // Normal Cappella's spring: stiffness 340, damping 28, mass .72.
        int steps = Math.Max(1, (int)MathF.Ceiling(dt * 240));
        float step = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            row.Velocity += ((target - row.Rect) * 340 - row.Velocity * 28) / .72f * step;
            row.Rect += row.Velocity * step;
        }
        row.Alpha += ((visible ? 1 : 0) - row.Alpha) * (1 - MathF.Exp(-dt * (visible ? 12 : 18)));
        float sx = row.Rect.Z / Math.Max(1, target.Z), sy = row.Rect.W / Math.Max(1, target.W);
        var transform = Matrix3x2.CreateTranslation(-target.X, -target.Y) * Matrix3x2.CreateScale(sx, sy)
            * Matrix3x2.CreateTranslation(row.Rect.X, row.Rect.Y);
        painter.AffineFrom(first, transform);
        painter.FadeFrom(first, row.Alpha);
    }
}
