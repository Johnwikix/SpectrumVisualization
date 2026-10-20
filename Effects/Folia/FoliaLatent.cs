using WinExSpectrumTest.Model;
using System;
using System.Threading;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original Latent palette, onset response and independent Paper shader clocks.
/// Cover extraction is cold; frame updates consume the analyzer's existing feature buffer.</summary>
internal sealed class FoliaLatent
{
    private byte[]? _cover;
    private Vector3[] _colors = [];
    private bool _active;
    private float _power, _bass, _mid, _beat, _previousEnergy, _onset;
    private double _meshTime, _ditherTime;

    internal void Prepare(FoliaSong song)
    {
        if (ReferenceEquals(_cover, song.CoverRgba))
            return;
        _colors = Extract(song);
        _cover = song.CoverRgba;
    }
    internal void SetPalette(FoliaSong song, Vector3[] colors)
    {
        _cover = song.CoverRgba;
        _colors = colors;
    }

    internal static Vector3[] Extract(FoliaSong song, CancellationToken token = default)
    {
        if (song.CoverRgba.Length == 0 || song.CoverWidth <= 0 || song.CoverHeight <= 0)
            return [];
        byte[] pixels = FoliaCoverSampling.PalettePixels(song.CoverRgba, song.CoverWidth, song.CoverHeight);
        using var input = new MemoryStream();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartArray();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i + 3];
                for (int c = 0; c < 3; c++)
                    writer.WriteNumberValue(alpha == 0 ? 0 : Math.Min(255, (pixels[i + c] * 255 + alpha / 2) / alpha));
                writer.WriteNumberValue(alpha);
            }
            writer.WriteEndArray();
        }
        string json = FoliaSourcePlanner.Compile(Encoding.UTF8.GetString(input.GetBuffer(), 0, (int)input.Length), "compilePalette", token);
        using var document = JsonDocument.Parse(json);
        var colors = new Vector3[document.RootElement.GetArrayLength()];
        for (int i = 0; i < colors.Length; i++)
        {
            string hex = document.RootElement[i].GetString()!;
            int rgb = int.Parse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            colors[i] = new Vector3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255;
        }
        return colors;
    }
    internal void Update(bool active, bool paused, double elapsed, ReadOnlySpan<float> features, Span<Vector4> colors, out Vector4 frame, out Vector4 style, out Vector4 distortion)
    {
        if (!active)
        {
            _active = false;
            frame = style = distortion = default;
            return;
        }
        if (!_active)
        {
            _active = true;
            _power = _bass = _mid = _beat = _previousEnergy = _onset = 0;
            _meshTime = _ditherTime = 0;
        }
        float dt = (float)Math.Clamp(elapsed, 0, .1), ticks = dt * 60;
        float power = paused ? 0 : Math.Clamp(features[FeatureIndex.Energy], 0, 1);
        float bass = paused ? 0 : Math.Clamp(features[FeatureIndex.Bass], 0, 1);
        float lowMid = paused ? 0 : Math.Clamp(features[FeatureIndex.LowMid], 0, 1);
        float mid = paused ? 0 : Math.Clamp(features[FeatureIndex.Mid], 0, 1);
        // The analyzer publishes fixed bands; map Folia's vocal / treble groups without another capture.
        float vocal = paused ? 0 : Math.Clamp((features[FeatureIndex.Mid] + features[FeatureIndex.HighMid] + features[FeatureIndex.Presence]) / 3, 0, 1);
        float treble = paused ? 0 : Math.Clamp((features[FeatureIndex.Brilliance] + features[FeatureIndex.Air]) * .5f, 0, 1);
        float energy = MathF.Pow(bass * .22f + lowMid * .18f + mid * .22f + vocal * .28f + treble * .1f, .55f);
        _power = Smooth(_power, power, .12f, ticks);
        _bass = Smooth(_bass, bass, .16f, ticks);
        _mid = Smooth(_mid, Math.Max(mid, vocal), .13f, ticks);
        _onset = Math.Max(_onset * MathF.Pow(.84f, ticks), Math.Clamp((energy - _previousEnergy) * 7, 0, 1));
        _previousEnergy = energy;
        float beat = AppSettings.FoliaLatentBeat ? Math.Clamp(energy * .42f + _onset * .85f, 0, 1) : energy;
        _beat = Smooth(_beat, beat, beat > _beat ? .42f : .14f, ticks);
        _meshTime += dt * (paused ? .036f : Math.Clamp(AppSettings.FoliaLatentMeshSpeed + (AppSettings.FoliaLatentMeshAudioSpeed - AppSettings.FoliaLatentMeshSpeed) * _beat, 0, 2));
        _ditherTime += dt * (paused ? .012f : Math.Clamp(AppSettings.FoliaLatentDitherSpeed + (AppSettings.FoliaLatentDitherAudioSpeed - AppSettings.FoliaLatentDitherSpeed) * _beat, 0, 2));
        frame = new((float)_meshTime, (float)_ditherTime, Math.Max(.5f, AppSettings.FoliaLatentSize * (1 - _bass * .34f)), Math.Min(1, AppSettings.FoliaLatentOpacity + _bass * .25f));
        style = new(1.025f + _power * .018f, 1.015f + _bass * .025f, 1.04f + _mid * .34f, .94f + _power * .16f);
        distortion = new(AppSettings.FoliaLatentDistortion + _power * .62f, AppSettings.FoliaLatentSwirl + _mid * .38f, 0, 0);
        // colors initially holds the theme fallback supplied by the effect.
        Vector4 primary = _colors.Length > 0 ? new(_colors[0], 1) : colors[0];
        Vector4 secondary = _colors.Length > 1 ? new(_colors[1], 1) : colors[1];
        colors[0] = primary;
        colors[1] = secondary;
        colors[2] = _colors.Length > 2 ? new(_colors[2], 1) : primary;
        colors[3] = _colors.Length > 3 ? new(_colors[3], 1) : secondary;
    }
    private static float Smooth(float current, float target, float amount, float ticks) => current + (target - current) * (1 - MathF.Pow(1 - amount, ticks));
}
