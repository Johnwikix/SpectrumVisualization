using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaDiorama
{
    /// <summary>Owns a bounded ring pool; the render worker never allocates ripple instances.</summary>
    private struct Ripple
    {
        internal double Start;
        internal float Strength;
    }
    private readonly Ripple[] _ripples = new Ripple[9];
    private int _rippleCursor;
    private FoliaMotionFrame _motion;

    internal void SetMotion(in FoliaMotionFrame motion, double time, bool reset)
    {
        if (reset || _motion.Active && !motion.Active)
        {
            _ripples.AsSpan().Clear();
            _rippleCursor = 0;
        }
        _motion = motion;
        if (motion.Triggered)
        {
            _ripples[_rippleCursor] = new() { Start = time, Strength = motion.Onset * 1.6f + motion.Low * 2.2f };
            _rippleCursor = (_rippleCursor + 1) % _ripples.Length;
        }
    }

    private float RippleDisplacement(Vector3 point, double time)
    {
        float value = 0, distance = point.Length();
        foreach (ref readonly var ripple in _ripples.AsSpan())
        {
            float age = (float)(time - ripple.Start);
            if (ripple.Strength <= 0 || age < 0 || age > 2)
                continue;
            float front = distance - age * 2.8f;
            value += MathF.Sin(front * 8) * MathF.Exp(-front * front * 3 - age * 2) * ripple.Strength * .13f;
        }
        return Math.Clamp(value, -.45f, .45f);
    }
}
