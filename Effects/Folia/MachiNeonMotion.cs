using System;
using System.Numerics;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Tracks independent FFT-band transients for the three Machi neon circuits.</summary>
internal struct MachiNeonMotion
{
    private const float PublicationTimeout = .25f;
    private Vector3 _baseline, _level, _hold;
    private long _lastPublication;
    private float _secondsSincePublication;

    internal Vector3 Update(ReadOnlySpan<float> features, float elapsed, float strength, bool reset, long publicationCount)
    {
        // WASAPI can stop delivering data when the output goes idle. The analyzer
        // retains the last FFT, so expire it independently of the lyrics clock.
        _secondsSincePublication = publicationCount != _lastPublication
            ? 0 : Math.Min(PublicationTimeout, _secondsSincePublication + Math.Max(0, elapsed));
        _lastPublication = publicationCount;
        if (reset || strength <= 0 || features.Length < FeatureIndex.Count)
        {
            _baseline = _level = _hold = Vector3.Zero;
            return Vector3.Zero;
        }

        bool hasAudio = publicationCount != 0 && _secondsSincePublication < PublicationTimeout;
        var bands = !hasAudio ? Vector3.Zero
            : new(
                .65f * Band(features[FeatureIndex.Bass], 3.5f) + .35f * Band(features[FeatureIndex.SubBass], 3.5f),
                .35f * Band(features[FeatureIndex.LowMid], 5) + .45f * Band(features[FeatureIndex.Mid], 5)
                    + .20f * Band(features[FeatureIndex.HighMid], 5),
                .70f * Band(features[FeatureIndex.Presence], 8) + .30f * Band(features[FeatureIndex.Brilliance], 8));
        var transient = Vector3.Max(bands - _baseline, Vector3.Zero);
        float dt = Math.Clamp(elapsed, 0, .1f);
        _baseline += (bands - _baseline) * (1 - MathF.Exp(-dt * 5));
        // The slow baseline keeps sustained notes dim. A strong attack reaches full
        // output immediately and holds briefly so it can survive a missed render frame.
        var drive = Vector3.Clamp((bands * .14f + transient * 4.8f) * strength, Vector3.Zero, Vector3.One);
        float decay = MathF.Exp(-dt * 18);
        _level = new(Pulse(drive.X, _level.X, ref _hold.X), Pulse(drive.Y, _level.Y, ref _hold.Y),
            Pulse(drive.Z, _level.Z, ref _hold.Z));
        return _level;

        float Pulse(float target, float previous, ref float hold)
        {
            if (target >= 1)
                hold = .045f;
            else
                hold = Math.Max(0, hold - dt);
            if (hold > 0 && hasAudio)
                return 1;
            return Math.Max(target, previous * decay);
        }
    }

    private static float Band(float value, float gain) =>
        float.IsFinite(value) ? Math.Clamp((value - .008f) * gain, 0, 1) : 0;
}
