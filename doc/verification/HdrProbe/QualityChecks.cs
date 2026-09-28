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
        foreach (string preset in new[] { "j", "k", "l", "m" })
        {
            var settings = new SonicQualitySettings(75, 120, mode, preset);
            require(settings.Normalize() == settings, "Reconstruction normalization");
            var dto = new SaveSetting { SonicAntiAliasing = mode, SonicDlssPreset = preset, SonicRenderScalePercent = 37, SonicResolutionVersion = 1 };
            var roundtrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(dto, SettingsJsonContext.Default.SaveSetting), SettingsJsonContext.Default.SaveSetting)!;
            require(roundtrip.SonicAntiAliasing == mode && roundtrip.SonicDlssPreset == preset, "Reconstruction AOT roundtrip");
            require(SonicQualitySettings.FromSaved(roundtrip).RenderScalePercent == 37, "Unified scale survives load");
            require(settings.IsTemporal == (mode is "xess" or "fsr" or "dlss"), "Temporal classification");
        }
        foreach (string mode in new[] { "xess", "fsr", "dlss" })
        foreach (string quality in new[] { "native", "quality", "balanced", "performance" })
        {
            var old = JsonSerializer.Deserialize($"{{\"SonicAntiAliasing\":\"{mode}\",\"SonicUpscaleQuality\":\"{quality}\"}}", SettingsJsonContext.Default.SaveSetting)!;
            int expected = quality == "native" ? 100 : mode == "xess"
                ? quality == "performance" ? 43 : quality == "balanced" ? 50 : 59
                : quality == "performance" ? 50 : quality == "balanced" ? 59 : 67;
            require(SonicQualitySettings.FromSaved(old).RenderScalePercent == expected, "Legacy SR scale migration");
        }
        foreach (int scale in new[] { 1, 2, 33, 37, 99, 100 })
            require(new SonicQualitySettings(scale, 160, "dlss").Normalize().RenderScalePercent == scale, "Continuous scale " + scale);
        foreach (int rate in FrameRateSettings.Presets)
        {
            var dto = new SaveSetting { RefreshRate = rate };
            var saved = JsonSerializer.Deserialize(JsonSerializer.Serialize(dto, SettingsJsonContext.Default.SaveSetting), SettingsJsonContext.Default.SaveSetting)!;
            require(FrameRateSettings.Normalize(saved.RefreshRate) == rate, "Frame rate roundtrip " + rate);
        }
        require(FrameRateSettings.Normalize(90) == 90 && FrameRateSettings.Normalize(float.NaN) == 60 && FrameRateSettings.Normalize(-1) == 60, "Legacy and invalid refresh rates");
        require(GpuRenderSize.Create(321, 181, 50) == new GpuRenderSize(321, 181, 160, 90), "Odd physical resolution");
        require(GpuRenderSize.Create(1, 1, 50).Width == 1, "Smallest scene");
        require(GpuRenderSize.Create(3840, 2160, 1) == new GpuRenderSize(3840, 2160, 38, 21), "One percent render dimensions");
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
        pacer.Reset();
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 1000; i++) pacer.Wait(0);
        require(Stopwatch.GetElapsedTime(start).TotalMilliseconds < 100, "Unlimited skips timer waits");
        pacer.Reset();
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < 96; i++) pacer.Wait(480);
        double highRateElapsed = Stopwatch.GetElapsedTime(start).TotalSeconds;
        require(highRateElapsed >= .15 && highRateElapsed < .7, "480 Hz is not clamped to 120 Hz");
        wake.Set();
        start = Stopwatch.GetTimestamp();
        pacer.Wait(60);
        require(Stopwatch.GetElapsedTime(start).TotalMilliseconds < 100, "Unlimited/high-rate transition remains interruptible");
        Console.WriteLine($"PASS high-resolution pacing: {fps:F2} waits/s, {allocated} allocated bytes (not display FPS)");
    }
}
