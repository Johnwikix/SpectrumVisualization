using System;
using System.Numerics;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaDiorama
{
    /// <summary>Owns a bounded ring pool; the render worker never allocates ripple instances.</summary>
    private struct Ripple
    {
        internal double Start;
        internal float Strength;
        internal Vector3 Origin;
    }
    private readonly Ripple[] _ripples = new Ripple[9];
    private int _rippleCursor;
    private FoliaMotionFrame _motion;
    private readonly FoliaRealtimeMotion _surfaceAudio = new();

    internal void SetMotion(in FoliaMotionFrame motion, double time, bool reset)
    {
        if (reset || _motion.Active && !motion.Active)
        {
            _ripples.AsSpan().Clear();
            _rippleCursor = 0;
        }
        _motion = motion;
    }

    internal void SetSurfaceAudio(ReadOnlySpan<float> features, double elapsed, double time, bool playing, bool reset)
    {
        var surface = _surfaceAudio.Update(features, elapsed, true, playing, reset);
        if (surface.Triggered)
        {
            float phase = (float)(_seedPhase + time * 2.39996323);
            Vector3 origin = Vector3.Normalize(new Vector3(MathF.Cos(phase), MathF.Sin(phase * 1.37f), MathF.Sin(phase)));
            _ripples[_rippleCursor] = new() { Start = time, Strength = surface.Onset * (1 + _motion.Low), Origin = origin };
            _rippleCursor = (_rippleCursor + 1) % _ripples.Length;
        }
    }

    private float RippleDisplacement(Vector3 point, float phase, double time)
    {
        float value = 0, cosine = MathF.Cos(phase), sine = MathF.Sin(phase);
        foreach (ref readonly var ripple in _ripples.AsSpan())
        {
            float age = (float)(time - ripple.Start);
            if (ripple.Strength <= 0 || age < 0 || age > 6)
                continue;
            Vector3 origin = new(ripple.Origin.X * cosine - ripple.Origin.Z * sine, ripple.Origin.Y, ripple.Origin.X * sine + ripple.Origin.Z * cosine);
            float distance = Vector3.Distance(point, origin), front = distance - age * 2.8f;
            value += MathF.Sin(front * 8) * MathF.Exp(-front * front / (.65f * .65f) - age * 1.15f - distance * .35f) * ripple.Strength;
        }
        return value;
    }
}
