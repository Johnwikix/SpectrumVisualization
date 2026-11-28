using System;
using System.Numerics;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Follows the spectrum with gradual color changes for the scene's lights.</summary>
internal struct MachiNeonMotion
{
    private const float PublicationTimeout = .25f;
    private Vector3 _baseline;
    private Circuit _bass, _mid, _treble, _landmark;
    private long _lastPublication;
    private float _secondsSincePublication;

    internal Vector4 Update(ReadOnlySpan<float> features, float elapsed, float strength, bool reset, long publicationCount)
    {
        if (reset || strength <= 0 || features.Length < FeatureIndex.Count)
        {
            this = default;
            return Vector4.Zero;
        }

        float dt = Math.Clamp(elapsed, 0, PublicationTimeout);
        bool fresh = publicationCount != 0 && publicationCount != _lastPublication;
        _secondsSincePublication = fresh ? 0 : _secondsSincePublication + dt;
        _lastPublication = publicationCount;
        if (publicationCount == 0 || _secondsSincePublication >= PublicationTimeout)
        {
            this = default;
            _lastPublication = publicationCount;
            _secondsSincePublication = PublicationTimeout;
            return Vector4.Zero;
        }

        var energy = new Vector3(Band(features[FeatureIndex.MotionBass]),
            Band(features[FeatureIndex.MotionMid]), Band(features[FeatureIndex.MotionTreble]));
        var attack = fresh ? Vector3.Max(energy - _baseline, Vector3.Zero)
            / (new Vector3(.0025f) + .16f * _baseline) : Vector3.Zero;
        // Follow the mix over several FFT windows so attacks still register over compression.
        _baseline += (energy - _baseline) * (1 - MathF.Exp(-dt * 9));
        float landmarkAttack = MathF.Max(attack.X, MathF.Max(.80f * attack.Y, .62f * attack.Z));
        float amount = .5f * strength;
        float fade = MathF.Exp(-dt * 4);
        float follow = 1 - MathF.Exp(-dt * 9);
        float landmarkFollow = 1 - MathF.Exp(-dt * 7);
        return amount * new Vector4(
            _bass.Update(attack.X, fade, follow),
            _mid.Update(attack.Y, fade, follow),
            _treble.Update(attack.Z, fade, follow),
            _landmark.Update(landmarkAttack, fade, landmarkFollow));
    }

    private static float Band(float value) => float.IsFinite(value) ? Math.Max(0, value - .0015f) : 0;

    private struct Circuit
    {
        private float _target, _color;

        internal float Update(float attack, float fade, float follow)
        {
            _target = Math.Max(_target * fade, attack / (.55f + attack));
            _color += (_target - _color) * follow;
            return _color;
        }
    }
}
