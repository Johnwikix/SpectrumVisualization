using System.Text.Json;
using ComputeSharp;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Manager;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

try
{
    var legacy = JsonSerializer.Deserialize("{\"VisualEffect\":\"sonic-topography\"}", SettingsJsonContext.Default.SaveSetting)!;
    Require(!legacy.HdrEnabled && legacy.HdrWhiteNits == 200 && legacy.HdrPeakNits == 1000, "Legacy settings defaults");
    AppSettings.Changed += name => Require(AppSettings.HdrWhiteNits <= AppSettings.HdrPeakNits, "Inconsistent brightness during event " + name);
    AppSettings.HdrPeakNits = 80;
    Require(AppSettings.HdrWhiteNits == 80, "Peak lowered below white");
    AppSettings.HdrWhiteNits = 500;
    Require(AppSettings.HdrPeakNits == 500, "White raised above peak");
    AppSettings.HdrWhiteNits = float.NaN;
    AppSettings.HdrPeakNits = float.PositiveInfinity;
    Require(AppSettings.HdrWhiteNits == 200 && AppSettings.HdrPeakNits == 1000, "Nonfinite settings");
    string json = JsonSerializer.Serialize(new SaveSetting { HdrEnabled = true, HdrWhiteNits = 250, HdrPeakNits = 1200 }, SettingsJsonContext.Default.SaveSetting);
    var roundtrip = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SaveSetting)!;
    Require(roundtrip.HdrEnabled && roundtrip.HdrWhiteNits == 250 && roundtrip.HdrPeakNits == 1200, "AOT JSON roundtrip");
    Console.WriteLine("PASS settings defaults, bounds, event consistency and source-generated JSON");

    var pendingSaves = new Task<bool>[24];
    for (int i = 0; i < pendingSaves.Length; i++)
        pendingSaves[i] = SettingManager.SaveSettingsAsync(new SaveSetting { HdrWhiteNits = 200 + i });
    Require(SettingManager.SaveSettingsNow(new SaveSetting { HdrEnabled = true, HdrWhiteNits = 350, HdrPeakNits = 1200 }), "Synchronous final save");
    Task.WhenAll(pendingSaves).GetAwaiter().GetResult();
    var saved = SettingManager.LoadSettingsAsync().GetAwaiter().GetResult();
    Require(saved.HdrEnabled && saved.HdrWhiteNits == 350 && saved.HdrPeakNits == 1200, "An older asynchronous save overwrote the final snapshot");
    Console.WriteLine("PASS concurrent saves followed by synchronous exit snapshot and JSON reload");

    using var analyzer = new SpectrumAnalyzer();
    using (var device = GraphicsDevice.GetDefault())
    using (var scene = device.AllocateReadWriteTexture2D<float4>(256, 144))
    using (var effect = new SonicTopographyEffect(device, analyzer))
    {
        Console.WriteLine("Device: " + device.Name);
        foreach (int grid in new[] { 80, 160, 320 })
        {
            AppSettings.SonicGridSize = grid;
            for (int i = 0; i < 120; i++) effect.Update(1d / 60);
            effect.Render(scene);
            var pixels = scene.ToArray();
            float maximum = 0;
            foreach (float4 pixel in pixels)
            {
                Require(float.IsFinite(pixel.X) && float.IsFinite(pixel.Y) && float.IsFinite(pixel.Z), "Nonfinite GPU pixel");
                Require(pixel.W == 1, "Scene must be opaque");
                maximum = Math.Max(maximum, Math.Max(pixel.X, Math.Max(pixel.Y, pixel.Z)));
            }
            Require(maximum > 0, "Black scene");
            Console.WriteLine($"PASS actual compute grid={grid}, max linear channel={maximum:0.000}");
        }
    }
    AppSettings.SonicGridSize = 160;
    int attachments = 0;
    for (int cycle = 0; cycle < 3; cycle++)
    {
        using var graphics = new SonicGraphics(analyzer, pointer => attachments += pointer == 0 ? -1 : 1, 256, 144);
        if (cycle == 0) graphics.VerifyOutputEncoding();
        foreach (var size in new[] { (256, 144), (480, 270), (321, 181), (256, 144) })
        {
            graphics.Resize(size.Item1, size.Item2);
            bool accepted = graphics.SetHdr(true);
            graphics.Render(1d / 60, 200, 1000);
            graphics.SetHdr(false);
            graphics.Render(1d / 60, 200, 1000);
            Console.WriteLine($"PASS native pipelines, HDR space accepted={accepted}, size={size}, cycle={cycle}");
        }
        for (int i = 0; i < 20; i++) graphics.Render(1d / 60, 200, 1000);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 60; i++) graphics.Render(1d / 60, 200, 1000);
        Console.WriteLine($"Measured render allocation: {(GC.GetAllocatedBytesForCurrentThread() - before) / 60d:0.0} bytes/frame");
    }
    Require(attachments == 0, "Composition chain ownership did not balance");
    Console.WriteLine("PASS repeated creation, resize, SDR/HDR encoding and disposal");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    return 1;
}

namespace WinExSpectrumTest
{
    internal static class App
    {
        public static void WriteCrashLog(string source, string message, Exception? exception)
            => Console.Error.WriteLine($"{source}: {message}\n{exception}");
    }
}
