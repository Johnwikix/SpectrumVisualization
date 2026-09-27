using System;

namespace WinExSpectrumTest.Effects;

/// <summary>Device-independent stereo projection used by Aurora and its regression probe.</summary>
internal sealed class RadialSpectrumState
{
    private float[] _positions = [];
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
        _positions = new float[count + 1];
        int half = count / 2;
        float nyquist = Math.Max(sampleRate, 8000) * 0.5f;
        float maxFrequency = MathF.Min(16000, nyquist * 0.9f);
        for (int i = 0; i <= count; i++)
        {
            float t = Math.Clamp((float)(i <= half ? i : count - i) / half, 0, 1);
            _positions[i] = 40 * MathF.Pow(maxFrequency / 40, t) / nyquist * bandCount;
            if (i < count)
                FrequencyPositions[i] = ((i < half ? i : count - 1 - i) + 0.5f) / half;
        }
    }

    public void Update(ReadOnlySpan<float> left, ReadOnlySpan<float> right,
        float gain, float smoothing, float exponent, float dt)
    {
        int count = Levels.Length;
        for (int i = 0; i < count; i++)
        {
            float lo = MathF.Min(_positions[i], _positions[i + 1]);
            float hi = MathF.Max(_positions[i], _positions[i + 1]);
            float l = Peak(left, lo, hi);
            float r = Peak(right, lo, hi);
            int half = count / 2;
            float distance = Math.Min(i % half + 0.5f, half - i % half - 0.5f);
            // Meet at the channel average at both joins, preserving stereo elsewhere.
            float blend = Math.Clamp(distance / Math.Max(4f, half * 0.12f), 0, 1);
            blend = blend * blend * (3 - 2 * blend);
            float own = i < half ? l : r;
            float value = ((l + r) * 0.5f + (own - (l + r) * 0.5f) * blend) * gain;
            value *= 0.7f + 0.6f * FrequencyPositions[i];
            value = MathF.Pow(Math.Clamp(value, 0, 1), exponent);
            _temporal[i] = _temporal[i] * smoothing + value * (1 - smoothing);
        }
        // Circular convolution reads a separate temporal buffer, including the wrap.
        for (int i = 0; i < count; i++)
        {
            Levels[i] = _temporal[(i + count - 1) % count] * 0.25f
                + _temporal[i] * 0.5f + _temporal[(i + 1) % count] * 0.25f;
            Peaks[i] = MathF.Max(Levels[i], Peaks[i] - dt * 0.55f);
        }
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
