using System.Text.Json;
using ComputeSharp;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Manager;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;
using System.Diagnostics;

if (args.Contains("--reconstruction")) return ReconstructionChecks.Run(args.Contains("--motion"));
if (args.Contains("--benchmark-reconstruction")) return ReconstructionBenchmark.Run();

if (args.Contains("--visual"))
{
    const int width = 960, height = 540;
    using var input = new SpectrumAnalyzer(captureAudio: false);
    using var device = GraphicsDevice.GetDefault();
    using var reference = new SonicTopographyEffect(device, input);
    using var target = device.AllocateReadWriteTexture2D<float4>(width, height);
    using var renderer = new GpuGraphics(input, _ => { }, width, height, static () => new SonicGpuEffect(), new(100, false, 160));
    for (int i = 0; i < 120; i++)
    {
        ProbeSignal.Update(input, i, false);
        reference.Update(1d / 120);
        renderer.Render(1d / 120, 200, 1000);
    }
    reference.Render(target);
    var oldPixels = target.ToArray();
    var flat = new float4[width * height];
    for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) flat[y * width + x] = oldPixels[y, x];
    var newPixels = renderer.ReadLinearScene();
    ProbeImages.Save("HdrProbe/bin/reference.bmp", flat, width, height);
    ProbeImages.Save("HdrProbe/bin/raster.bmp", newPixels, width, height);
    double error = 0;
    int large = 0;
    for (int i = 0; i < flat.Length; i++)
    {
        float delta = Math.Max(Math.Abs(flat[i].X-newPixels[i].X),Math.Max(Math.Abs(flat[i].Y-newPixels[i].Y),Math.Abs(flat[i].Z-newPixels[i].Z)));
        if (!float.IsFinite(delta)) throw new InvalidOperationException("Non-finite scene");
        error += delta;
        if (delta > .05f) large++;
    }
    Console.WriteLine($"VISUAL meanMaxChannelError={error / flat.Length:F5} pixelsDeltaOver0.05={large / (double)flat.Length:P2}");
    return 0;
}

if (args.Contains("--benchmark"))
{
    using var input = new SpectrumAnalyzer(captureAudio: false);
    AppSettings.SonicAutoRotate = true;
    using var deviceInfo = GraphicsDevice.GetDefault();
    Console.WriteLine($"BENCHMARK device={deviceInfo.Name}");
    foreach (bool stress in new[] { false, true })
    foreach (var test in new[] { ("performance", 50, 80, false), ("balanced", 75, 120, true), ("high", 100, 160, true), ("legacy-quality", 100, 160, false) })
    {
        AppSettings.SonicGridSize = test.Item3;
        using var renderer = new GpuGraphics(input, _ => { }, 2560, 1440, static () => new SonicGpuEffect(), new(test.Item2, test.Item4, test.Item3));
        for (int i = 0; i < 360; i++)
        {
            if (stress) ProbeSignal.Update(input, i, true);
            renderer.Render(1d / 120, 200, 1000);
        }
        var times = new double[1200];
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < times.Length; i++)
        {
            long start = Stopwatch.GetTimestamp();
            if (stress) ProbeSignal.Update(input, i + 360, true);
            renderer.Render(1d / 120, 200, 1000);
            times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
        Array.Sort(times);
        Console.WriteLine($"BENCHMARK output=2560x1440 preset={test.Item1} stress={stress} meanMs={times.Average():F3} p95Ms={times[1139]:F3} p99Ms={times[1187]:F3} maxMs={times[^1]:F3} bytesPerFrame={allocated / (double)times.Length:F1}");
    }
    return 0;
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

try
{
    var legacy = JsonSerializer.Deserialize("{\"VisualEffect\":\"sonic-topography\"}", SettingsJsonContext.Default.SaveSetting)!;
    Require(!legacy.HdrEnabled && legacy.HdrWhiteNits == 200 && legacy.HdrPeakNits == 1000, "Legacy settings defaults");
    QualityChecks.Run(Require, legacy);
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

    using var analyzer = new SpectrumAnalyzer(captureAudio: false);
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
        using var graphics = new GpuGraphics(analyzer, pointer => attachments += pointer == 0 ? -1 : 1, 256, 144, static () => new SonicGpuEffect(), new(100, false, 160));
        if (cycle == 0)
        {
            graphics.VerifyOutputEncoding();
            graphics.Configure(new(50, true, 80));
            graphics.VerifyOutputEncoding();
            graphics.VerifyEffectContract();
        }
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
    QualityChecks.VerifyPacer(Require);
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
