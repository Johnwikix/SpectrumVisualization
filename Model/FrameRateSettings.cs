using System;

namespace WinExSpectrumTest.Model;

/// <summary>Zero removes application pacing. Positive legacy values remain valid.</summary>
internal static class FrameRateSettings
{
    internal static ReadOnlySpan<int> Presets => [30, 60, 72, 80, 120, 144, 160, 240, 0];
    internal static float Normalize(float value) =>
        value == 0 ? 0 : float.IsFinite(value) && value >= 1 ? Math.Min(value, 240) : 60;
}
