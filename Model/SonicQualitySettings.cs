using System;

namespace WinExSpectrumTest.Model;

/// <summary>Stores one coherent set of scene quality parameters.</summary>
public readonly record struct SonicQualitySettings(int RenderScalePercent, int GridSize, string AntiAliasing, string DlssPreset = "k")
{
    /// <summary>Gets the defaults for new configurations.</summary>
    public static SonicQualitySettings Default => new(100, 160, "fxaa");

    /// <summary>Normalizes persisted values without changing existing effect identifiers.</summary>
    public SonicQualitySettings Normalize() => new(
        RenderScalePercent is >= 1 and <= 100 ? RenderScalePercent : 100,
        Math.Clamp(GridSize, 80, 320), AntiAliasing is "off" or "fxaa" or "smaa" or "xess" or "fsr" or "dlss" ? AntiAliasing : "fxaa",
        DlssPreset is "j" or "k" or "l" or "m" ? DlssPreset : "k");

    /// <summary>Gets whether the selected algorithm uses temporal reconstruction.</summary>
    public bool IsTemporal => AntiAliasing is "xess" or "fsr" or "dlss";

    /// <summary>Migrates the old SDK-owned resolution once; newer saves use the common scale.</summary>
    public static SonicQualitySettings FromSaved(SaveSetting saved)
    {
        int scale = saved.SonicRenderScalePercent;
        if (saved.SonicResolutionVersion == 0 && saved.SonicAntiAliasing is "xess" or "fsr" or "dlss")
        {
            scale = (saved.SonicAntiAliasing, saved.SonicUpscaleQuality) switch
            {
                (_, "native") => 100,
                ("xess", "performance") => 43,
                ("xess", "balanced") => 50,
                ("xess", _) => 59,
                (_, "performance") => 50,
                (_, "balanced") => 59,
                _ => 67
            };
        }
        return new SonicQualitySettings(scale, saved.SonicGridSize, saved.SonicAntiAliasing, saved.SonicDlssPreset).Normalize();
    }
}
