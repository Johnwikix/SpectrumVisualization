using System.Diagnostics;
using ComputeSharp;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

internal static class PipelineBenchmark
{
    internal static int Run(string[] args)
    {
        int grid = args.Contains("--grid80") ? 80 : args.Contains("--grid320") ? 320 : 160;
        bool reverse = args.Contains("--reverse");
        using var device = GraphicsDevice.GetDefault();
        AppSettings.SonicAutoRotate = true;
        Console.WriteLine($"OFFSCREEN device={device.Name} output=3840x2160 grid={grid} syntheticAudio=true debugLayer=false reverse={reverse}");
        var configurations = new (ReconstructionMode Mode, int Scale, string Preset)[]
        {
            (ReconstructionMode.Off, 50, "k"),
            (ReconstructionMode.Fxaa, 50, "k"),
            (ReconstructionMode.Smaa, 50, "k"),
            (ReconstructionMode.Smaa, 100, "k"),
            (ReconstructionMode.Fsr, 50, "k"),
            (ReconstructionMode.XeSS, 50, "k"),
            (ReconstructionMode.Dlss, 50, "k")
        };
        if (args.Contains("--dlss-presets")) configurations =
            [(ReconstructionMode.Dlss, 50, "m"), (ReconstructionMode.Dlss, 50, "k"), (ReconstructionMode.Dlss, 50, "j"), (ReconstructionMode.Dlss, 50, "l")];
        if (reverse) Array.Reverse(configurations);
        foreach (var config in configurations)
        {
            // Fresh simulation/history gives each mode identical camera/audio trajectories.
            using var input = new SpectrumAnalyzer(captureAudio: false);
            using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 3840, 2160,
                static () => new SonicGpuEffect(), new(config.Scale, config.Mode, grid, config.Preset));
            if (renderer.Reconstruction.Active != config.Mode)
                throw new InvalidOperationException($"Requested {config.Mode}, active {renderer.Reconstruction.Active}");
            using var queries = renderer.CreateTimingQueries();
            using var readback = renderer.CreateTimingReadback();
            ulong frequency = renderer.TimingFrequency();
            const int warmup = 120, count = 360;
            var samples = new PipelineSample[count];
            for (int i = 0; i < warmup; i++)
            {
                ProbeSignal.Update(input, i, true);
                renderer.ProfileFrame(queries, readback, frequency);
            }
            long allocation = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                ProbeSignal.Update(input, warmup + i, true);
                samples[i] = renderer.ProfileFrame(queries, readback, frequency);
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
            Console.WriteLine($"PROFILE mode={config.Mode} preset={config.Preset} input={renderer.ProfileSize.Width}x{renderer.ProfileSize.Height} " +
                $"prepareCpu={samples.Average(s => s.PrepareCpu):F4} recordCpu={samples.Average(s => s.RecordCpu):F4} submitFenceCpu={samples.Average(s => s.SubmitFenceCpu):F4} " +
                $"sceneGpu={samples.Average(s => s.SceneGpu):F4} srGpu={samples.Average(s => s.ReconstructionGpu):F4} overlayGpu={samples.Average(s => s.OverlayGpu):F4} outputGpu={samples.Average(s => s.OutputGpu):F4} " +
                $"directGpu={samples.Average(s => s.SceneGpu + s.ReconstructionGpu + s.OverlayGpu + s.OutputGpu):F4} " +
                $"profileCpu={samples.Average(s => s.PrepareCpu + s.RecordCpu + s.SubmitFenceCpu):F4} bytesPerFrame={bytes / (double)count:F1}");
            // Cross-check the unmodified production path. This later trajectory is not an
            // exact per-frame comparison; it detects gross instrumentation distortions.
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                ProbeSignal.Update(input, warmup + count + i, true);
                renderer.Render(1d / 120, 200, 1000);
            }
            Console.WriteLine($"CONTROL mode={config.Mode} preset={config.Preset} input={renderer.ProfileSize.Width}x{renderer.ProfileSize.Height} productionCpuMs={Stopwatch.GetElapsedTime(start).TotalMilliseconds / count:F4}");
        }
        return 0;
    }
}
