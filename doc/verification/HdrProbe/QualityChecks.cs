using System.Diagnostics;
using System.Text.Json;
using WinExSpectrumTest.Manager;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

internal static class QualityChecks
{
    public static void Run(Action<bool, string> require, SaveSetting legacy)
    {
        require(legacy.SonicRenderScalePercent == 100 && legacy.SonicGridSize == 160 && legacy.SonicAntiAliasing == "fxaa", "New and missing-field defaults");
        require(GpuQualitySettings.FromSaved(legacy) == GpuQualitySettings.Default, "Runtime and persisted defaults agree");
        var existing = JsonSerializer.Deserialize("{\"SonicRenderScalePercent\":75,\"SonicAntiAliasing\":\"off\"}", SettingsJsonContext.Default.SaveSetting)!;
        require(GpuQualitySettings.FromSaved(existing) == new GpuQualitySettings(75, "off"), "Explicit saved choices survive default changes");
        int changes = 0;
        void Changed(string name)
        {
            require(name == nameof(AppSettings.GpuQuality), "One quality notification, not partially updated fields");
            changes++;
            require(AppSettings.GpuQuality == new GpuQualitySettings(50, "off"), "Atomic quality notification");
        }
        AppSettings.Changed += Changed;
        AppSettings.GpuQuality = new(50, "off");
        AppSettings.Changed -= Changed;
        require(changes == 1, "Snapshot generates exactly one change");
        AppSettings.GpuAntiAliasing = "fxaa";
        require(AppSettings.GpuQuality == new GpuQualitySettings(50, "fxaa"), "Independent AA control preserves scale and detail");
        require(new GpuQualitySettings(-1, "unknown").Normalize() == new GpuQualitySettings(100, "fxaa"), "Invalid quality normalization");
        require(legacy.SonicUpscaleQuality == "quality", "Legacy reconstruction quality default");
        foreach (string mode in new[] { "off", "fxaa", "smaa", "xess", "fsr", "dlss" })
        foreach (string preset in new[] { "j", "k", "l", "m" })
        {
            var settings = new GpuQualitySettings(75, mode, preset);
            require(settings.Normalize() == settings, "Reconstruction normalization");
            var dto = new SaveSetting { SonicAntiAliasing = mode, SonicDlssPreset = preset, SonicRenderScalePercent = 37, SonicResolutionVersion = 1 };
            var roundtrip = JsonSerializer.Deserialize(JsonSerializer.Serialize(dto, SettingsJsonContext.Default.SaveSetting), SettingsJsonContext.Default.SaveSetting)!;
            require(roundtrip.SonicAntiAliasing == mode && roundtrip.SonicDlssPreset == preset, "Reconstruction AOT roundtrip");
            require(GpuQualitySettings.FromSaved(roundtrip).RenderScalePercent == 37, "Unified scale survives load");
            require(settings.IsTemporal == (mode is "xess" or "fsr" or "dlss"), "Temporal classification");
        }
        foreach (string mode in new[] { "xess", "fsr", "dlss" })
        foreach (string quality in new[] { "native", "quality", "balanced", "performance" })
        {
            var old = JsonSerializer.Deserialize($"{{\"SonicAntiAliasing\":\"{mode}\",\"SonicUpscaleQuality\":\"{quality}\"}}", SettingsJsonContext.Default.SaveSetting)!;
            int expected = quality == "native" ? 100 : mode == "xess"
                ? quality == "performance" ? 43 : quality == "balanced" ? 50 : 59
                : quality == "performance" ? 50 : quality == "balanced" ? 59 : 67;
            require(GpuQualitySettings.FromSaved(old).RenderScalePercent == expected, "Legacy SR scale migration");
        }
        foreach (int scale in new[] { 1, 2, 33, 37, 99, 100 })
            require(new GpuQualitySettings(scale, "dlss").Normalize().RenderScalePercent == scale, "Continuous scale " + scale);
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
        // Registrations cannot opt out of shared quality, even without a detail callback.
        AppSettings.GpuQuality = new(37, "dlss", "m");
        var nextEffect = new WinExSpectrumTest.Effects.EffectDescriptor("future-gpu", "", WinExSpectrumTest.Effects.EffectHost.Gpu, false, null);
        require(nextEffect.GetSceneOptions() == new GpuSceneOptions(37, ReconstructionMode.Dlss, 0, "m"), "New GPU effect inherits all shared options");
        var detailedEffect = nextEffect with { GetDetail = static () => 120 };
        require(detailedEffect.GetSceneOptions() == nextEffect.GetSceneOptions() with { Detail = 120 }, "Effect detail cannot override shared quality");
        AppSettings.SonicGridSize = 320;
        require(nextEffect.GetSceneOptions().RenderScalePercent == 37 && AppSettings.GpuQuality.DlssPreset == "m", "Terrain detail stays independent");
        AppSettings.SonicGridSize = 160;
        AppSettings.GpuQuality = GpuQualitySettings.Default;
        Console.WriteLine("PASS quality defaults, atomic notification, independent controls, migration and JSON roundtrip");
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
