using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original 240-second, 156-segment periodic FBM track; only interpolation runs per frame.</summary>
internal sealed class FoliaMonetDrift
{
    private readonly Vector3[] _track = new Vector3[157];
    internal FoliaMonetDrift()
    {
        var samples = new double[3, 157];
        ReadOnlySpan<double> seeds = [12.9898, 78.233, 39.425];
        for (int channel = 0; channel < 3; channel++)
        {
            double peak = 0;
            for (int i = 0; i <= 156; i++)
            {
                double t = 76d * i / 156;
                double value = (Noise(t, 76, seeds[channel]) * .68 + Noise(t * 2, 152, seeds[channel] + 17.31) * .32) * 2 - 1;
                samples[channel, i] = value;
                peak = Math.Max(peak, Math.Abs(value));
            }
            for (int i = 0; i <= 156; i++)
                samples[channel, i] /= Math.Max(peak, double.Epsilon);
        }
        for (int i = 0; i <= 156; i++)
            _track[i] = new((float)(Math.Round(samples[0, i] * 1.7, 4, MidpointRounding.AwayFromZero) / 100),
                (float)(Math.Round(samples[1, i] * 1.7, 4, MidpointRounding.AwayFromZero) / 100),
                (float)Math.Round(1.044 + .0175 * ((samples[2, i] + 1) / 2), 5, MidpointRounding.AwayFromZero));
    }
    private static double Hash(int lattice, double seed)
    {
        double value = Math.Sin(lattice * 127.1 + seed * 311.7) * 43758.5453;
        return value - Math.Floor(value);
    }
    private static double Noise(double time, int points, double seed)
    {
        int cell = (int)Math.Floor(time);
        double t = time - cell, from = Hash(cell % points, seed), to = Hash((cell + 1) % points, seed);
        return from + (to - from) * t * t * (3 - 2 * t);
    }
    internal Vector3 At(double seconds)
    {
        float position = (float)((seconds % 240) / 240 * 156);
        int i = Math.Min((int)position, 155);
        return Vector3.Lerp(_track[i], _track[i + 1], position - i);
    }
}
