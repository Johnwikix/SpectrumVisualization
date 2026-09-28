using System.Diagnostics;
using ComputeSharp;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

internal static class ReconstructionBenchmark
{
    internal static int Run()
    {
        using var input = new SpectrumAnalyzer(captureAudio: false);
        AppSettings.SonicAutoRotate = true;
        using var renderer = new GpuGraphics(input, _ => { }, 2560, 1440, static () => new SonicGpuEffect(), new(50, false, 80));
        using var device = GraphicsDevice.GetDefault();
        Console.WriteLine($"OFFSCREEN benchmark device={device.Name}, output=2560x1440, synthetic audio only, no display FPS");
        foreach (var mode in new[] { ReconstructionMode.Off, ReconstructionMode.Fxaa, ReconstructionMode.Smaa, ReconstructionMode.XeSS, ReconstructionMode.Fsr })
        {
            if (mode >= ReconstructionMode.XeSS && (renderer.Reconstruction.Capabilities & (1u << (int)mode)) == 0) continue;
            renderer.Configure(new(50, mode, 80, "performance"));
            for (int i = 0; i < 120; i++) { ProbeSignal.Update(input, i, true); renderer.Render(1d / 120, 200, 1000); }
            var samples = new double[480];
            _ = Stopwatch.GetTimestamp();
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < samples.Length; i++)
            {
                ProbeSignal.Update(input, i + 120, true);
                long start = Stopwatch.GetTimestamp();
                renderer.Render(1d / 120, 200, 1000);
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
            Array.Sort(samples);
            Console.WriteLine($"OFFSCREEN mode={mode} active={renderer.Reconstruction.Active} meanMs={samples.Average():F3} p95Ms={samples[455]:F3} p99Ms={samples[475]:F3} bytesPerFrame={bytes / 480.0:F1}");
        }
        return 0;
    }
}
