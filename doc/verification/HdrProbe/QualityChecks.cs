using System.Diagnostics;
using System.Text.Json;
using WinExSpectrumTest.Manager;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

internal static class QualityChecks
{
    public static void Run(Action<bool, string> require, SaveSetting legacy)
    {
        require(legacy.SonicRenderScalePercent == 100 && legacy.SonicGridSize == 160 && legacy.SonicAntiAliasing == "off", "Legacy quality defaults");
        int changes = 0;
        void Changed(string name)
        {
            require(name == nameof(AppSettings.SonicQuality), "One quality notification, not partially updated fields");
            changes++;
            require(AppSettings.SonicQuality == new SonicQualitySettings(50, 80, "off"), "Atomic quality notification");
        }
        AppSettings.Changed += Changed;
        AppSettings.SonicQuality = SonicQualitySettings.FromPreset("performance", AppSettings.SonicQuality);
        AppSettings.Changed -= Changed;
        require(changes == 1, "Preset generates exactly one change");
        require(AppSettings.SonicQuality.Preset == "performance", "Performance preset");
        AppSettings.SonicAntiAliasing = "fxaa";
        require(AppSettings.SonicQuality.Preset == "custom", "Independent control becomes custom");
        foreach (string preset in new[] { "performance", "balanced", "high" })
        {
            var quality = SonicQualitySettings.FromPreset(preset, SonicQualitySettings.Default);
            require(quality.Preset == preset && quality.Normalize() == quality, "Preset normalization " + preset);
            string json = JsonSerializer.Serialize(new SaveSetting
            {
                SonicGridSize = quality.GridSize,
                SonicRenderScalePercent = quality.RenderScalePercent,
                SonicAntiAliasing = quality.AntiAliasing
            }, SettingsJsonContext.Default.SaveSetting);
            var saved = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SaveSetting)!;
            require(new SonicQualitySettings(saved.SonicRenderScalePercent, saved.SonicGridSize, saved.SonicAntiAliasing) == quality, "Quality AOT JSON roundtrip");
        }
        require(new SonicQualitySettings(-1, 500, "unknown").Normalize() == new SonicQualitySettings(100, 320, "off"), "Invalid quality normalization");
        require(legacy.SonicUpscaleQuality == "quality", "Legacy reconstruction quality default");
        foreach (string mode in new[] { "off", "fxaa", "smaa", "xess", "fsr", "dlss" })
        foreach (string quality in new[] { "native", "quality", "balanced", "performance" })
        {
            var settings = new SonicQualitySettings(75, 120, mode, quality);
            require(settings.Normalize() == settings, "Reconstruction normalization");
            var dto = new SaveSetting { SonicAntiAliasing = mode, SonicUpscaleQuality = quality };
            var roundtrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(dto, SettingsJsonContext.Default.SaveSetting), SettingsJsonContext.Default.SaveSetting)!;
            require(roundtrip.SonicAntiAliasing == mode && roundtrip.SonicUpscaleQuality == quality, "Reconstruction AOT roundtrip");
            require(settings.IsTemporal == (mode is "xess" or "fsr" or "dlss"), "Temporal scale ownership");
        }
        require(GpuRenderSize.Create(321, 181, 50) == new GpuRenderSize(321, 181, 160, 90), "Odd physical resolution");
        require(GpuRenderSize.Create(1, 1, 50).Width == 1, "Smallest scene");
        AppSettings.SonicQuality = SonicQualitySettings.Default;
        Console.WriteLine("PASS quality presets, atomic notification, custom values, migration and JSON roundtrip");
    }

    public static void VerifyPacer(Action<bool, string> require)
    {
        using var wake = new AutoResetEvent(false);
        using var pacer = new GpuFramePacer(wake);
        for (int i = 0; i < 20; i++) pacer.Wait(120);
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 240; i++) pacer.Wait(120);
        double fps = 240 / Stopwatch.GetElapsedTime(start).TotalSeconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
        require(allocated == 0, "Pacer steady-state allocation");
        wake.Set();
        start = Stopwatch.GetTimestamp();
        pacer.Wait(1);
        require(Stopwatch.GetElapsedTime(start).TotalMilliseconds < 100, "Settings/shutdown interrupts timer wait");
        Console.WriteLine($"PASS high-resolution pacing: {fps:F2} waits/s, {allocated} allocated bytes (not display FPS)");
    }
}
