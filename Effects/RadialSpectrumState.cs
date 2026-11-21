using System;

namespace WinExSpectrumTest.Effects;

/// <summary>Device-independent stereo projection used by Aurora and its regression probe.</summary>
internal sealed class RadialSpectrumState
{
    private float[] _positions = [];
    private float[] _channelBlend = [];
    private float[] _temporal = [];
    private int _sampleRate;
    private int _bandCount;
    public float[] Levels { get; private set; } = [];
    public float[] Peaks { get; private set; } = [];
    public float[] FrequencyPositions { get; private set; } = [];

    public void Configure(int count, int sampleRate, int bandCount)
    {
        count = Math.Clamp(count, 32, bandCount) & ~1;
        if (Levels.Length == count && _sampleRate == sampleRate && _bandCount == bandCount) return;
        _sampleRate = sampleRate;
        _bandCount = bandCount;
        Levels = new float[count];
        Peaks = new float[count];
        _temporal = new float[count];
        FrequencyPositions = new float[count];
        int half = count / 2;
        _positions = new float[half + 1];
        _channelBlend = new float[half];
        float nyquist = Math.Max(sampleRate, 8000) * 0.5f;
        float maxFrequency = MathF.Min(16000, nyquist * 0.9f);
        for (int i = 0; i <= half; i++)
        {
            float t = (float)i / half;
            _positions[i] = 40 * MathF.Pow(maxFrequency / 40, t) / nyquist * bandCount;
        }
        for (int i = 0; i < half; i++)
        {
            FrequencyPositions[i] = FrequencyPositions[count - 1 - i] = (i + 0.5f) / half;
            float distance = Math.Min(i + 0.5f, half - i - 0.5f);
            float blend = Math.Clamp(distance / Math.Max(4f, half * 0.12f), 0, 1);
            _channelBlend[i] = blend * blend * (3 - 2 * blend);
        }
    }

    public void Update(ReadOnlySpan<float> left, ReadOnlySpan<float> right,
        float gain, float smoothing, float exponent, float dt)
    {
        int count = Levels.Length;
        for (int i = 0; i < count / 2; i++)
        {
            float lo = _positions[i];
            float hi = _positions[i + 1];
            float l = Peak(left, lo, hi);
            float r = Peak(right, lo, hi);
            // Mirrored bars share a frequency interval and meet at the channel average at both joins.
            float average = (l + r) * 0.5f;
            float blend = _channelBlend[i];
            float weight = 0.7f + 0.6f * FrequencyPositions[i];
            Smooth(i, (average + (l - average) * blend) * gain * weight, smoothing, exponent);
            Smooth(count - 1 - i, (average + (r - average) * blend) * gain * weight, smoothing, exponent);
        }
        // Circular convolution reads a separate temporal buffer, including the wrap.
        for (int i = 0; i < count; i++)
        {
            Levels[i] = _temporal[i == 0 ? count - 1 : i - 1] * 0.25f
                + _temporal[i] * 0.5f + _temporal[i + 1 == count ? 0 : i + 1] * 0.25f;
            Peaks[i] = MathF.Max(Levels[i], Peaks[i] - dt * 0.55f);
        }
    }

    private void Smooth(int index, float value, float smoothing, float exponent)
    {
        value = MathF.Pow(Math.Clamp(value, 0, 1), exponent);
        _temporal[index] = _temporal[index] * smoothing + value * (1 - smoothing);
    }

    private static float Peak(ReadOnlySpan<float> bands, float lo, float hi)
    {
        float peak = MathF.Max(Sample(bands, lo), Sample(bands, hi));
        for (int b = (int)lo + 1; b <= Math.Min((int)hi, bands.Length - 1); b++)
            peak = MathF.Max(peak, bands[b]);
        return peak;
    }

    private static float Sample(ReadOnlySpan<float> bands, float position)
    {
        int index = Math.Clamp((int)position, 0, bands.Length - 1);
        int next = Math.Min(index + 1, bands.Length - 1);
        return bands[index] + (bands[next] - bands[index]) * (position - index);
    }
}
