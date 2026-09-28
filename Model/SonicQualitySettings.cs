using System;

namespace WinExSpectrumTest.Model;

/// <summary>Stores one coherent set of scene quality parameters.</summary>
public readonly record struct SonicQualitySettings(int RenderScalePercent, int GridSize, string AntiAliasing)
{
    /// <summary>Gets settings compatible with configurations written before quality controls existed.</summary>
    public static SonicQualitySettings Default => new(100, 160, "off");

    /// <summary>Gets the preset matching the actual parameters.</summary>
    public string Preset => this switch
    {
        (50, 80, "off") => "performance",
        (75, 120, "fxaa") => "balanced",
        (100, 160, "fxaa") => "high",
        _ => "custom"
    };

    /// <summary>Normalizes persisted values without changing existing effect identifiers.</summary>
    public SonicQualitySettings Normalize() => new(
        RenderScalePercent is >= 50 and <= 100 ? RenderScalePercent : 100,
        Math.Clamp(GridSize, 80, 320), AntiAliasing == "fxaa" ? "fxaa" : "off");

    /// <summary>Resolves a preset, preserving the current values for custom or unknown selections.</summary>
    public static SonicQualitySettings FromPreset(string preset, SonicQualitySettings current) => preset switch
    {
        "performance" => new(50, 80, "off"),
        "balanced" => new(75, 120, "fxaa"),
        "high" => new(100, 160, "fxaa"),
        _ => current
    };
}
