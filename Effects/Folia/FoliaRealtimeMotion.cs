using System;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Contains live band-onset envelopes, without claiming metrical beats or separated drum hits.</summary>
internal readonly record struct FoliaMotionFrame(bool Active, float Onset, float Low, float Treble, float Sign, bool Triggered)
{
    internal FoliaMotionFrame Scale(float strength) => strength <= 0 ? default
        : this with { Onset = Onset * strength, Low = Low * strength };
}

/// <summary>Tracks live transients on the render worker using fixed state; silence, pause and seeks reset it.</summary>
internal sealed class FoliaRealtimeMotion
{
    private float _bass, _mid, _treble, _onset, _low, _cooldown, _sign = 1;
    private bool _synced;

    internal void Reset()
    {
        _bass = _mid = _treble = _onset = _low = _cooldown = 0;
        _sign = 1;
        _synced = false;
    }

    internal FoliaMotionFrame Update(ReadOnlySpan<float> features, double elapsed, bool enabled, bool playing, bool reset)
    {
        if (!enabled || !playing || reset || features.Length < FeatureIndex.Count)
        {
            Reset();
            return default;
        }
        float dt = (float)Math.Clamp(elapsed, 0, .1);
        float bass = Level(features[FeatureIndex.Bass] * .7f + features[FeatureIndex.SubBass] * .3f);
        float mid = Level(features[FeatureIndex.Mid]);
        float treble = Level(features[FeatureIndex.Brilliance]);
        if (!_synced)
        {
            _bass = bass;
            _mid = mid;
            _treble = treble;
            _synced = true;
            return new(true, 0, 0, 0, 1, false);
        }
        _cooldown = Math.Max(0, _cooldown - dt);
        float lowRise = Math.Max(0, bass - _bass * 1.35f - .025f);
        float midRise = Math.Max(0, mid - _mid * 1.4f - .02f);
        bool audible = Level(features[FeatureIndex.Energy]) > .008f;
        bool fired = audible && _cooldown <= 0 && Math.Max(lowRise, midRise) > .035f;
        _onset *= MathF.Exp(-dt * 12);
        _low *= MathF.Exp(-dt * 9);
        if (fired)
        {
            _onset = Math.Clamp(Math.Max(lowRise, midRise) * 4, .2f, 1);
            _low = Math.Clamp(lowRise * 5, 0, 1);
            _sign = -_sign;
            _cooldown = .13f;
        }
        float follow = 1 - MathF.Exp(-dt * 4);
        _bass += (bass - _bass) * follow;
        _mid += (mid - _mid) * follow;
        float target = audible ? Math.Clamp((treble - .04f) / .35f, 0, 1) : 0;
        _treble += (target - _treble) * (1 - MathF.Exp(-dt * (target > _treble ? 18 : 5)));
        return new(true, _onset, _low, _treble, _sign, fired);
    }

    private static float Level(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
