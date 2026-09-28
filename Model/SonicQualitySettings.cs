using System;

namespace WinExSpectrumTest.Model;

/// <summary>Stores one coherent set of scene quality parameters.</summary>
public readonly record struct SonicQualitySettings(int RenderScalePercent, int GridSize, string AntiAliasing, string UpscaleQuality = "quality")
{
    /// <summary>Gets settings compatible with configurations written before quality controls existed.</summary>
    public static SonicQualitySettings Default => new(100, 160, "off");

    /// <summary>Gets the preset matching the actual parameters.</summary>
    public string Preset => this switch
    {
        (50, 80, "off", _) => "performance",
        (75, 120, "fxaa", _) => "balanced",
        (100, 160, "fxaa", _) => "high",
        _ => "custom"
    };

    /// <summary>Normalizes persisted values without changing existing effect identifiers.</summary>
    public SonicQualitySettings Normalize() => new(
        RenderScalePercent is >= 50 and <= 100 ? RenderScalePercent : 100,
        Math.Clamp(GridSize, 80, 320), AntiAliasing is "fxaa" or "smaa" or "xess" or "fsr" or "dlss" ? AntiAliasing : "off",
        UpscaleQuality is "native" or "balanced" or "performance" ? UpscaleQuality : "quality");

    /// <summary>Gets whether the selected reconstruction algorithm owns the internal render size.</summary>
    public bool IsTemporal => AntiAliasing is "xess" or "fsr" or "dlss";

    /// <summary>Resolves a preset, preserving the current values for custom or unknown selections.</summary>
    public static SonicQualitySettings FromPreset(string preset, SonicQualitySettings current) => preset switch
    {
        "performance" => new(50, 80, "off"),
        "balanced" => new(75, 120, "fxaa"),
        "high" => new(100, 160, "fxaa"),
        _ => current
    };
}
