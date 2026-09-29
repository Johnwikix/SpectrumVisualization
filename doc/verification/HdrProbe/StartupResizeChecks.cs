using System.Diagnostics;
using ComputeSharp;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Rendering;

internal static class StartupResizeChecks
{
    internal static int Run()
    {
        var warmup = new GpuShaderWarmup();
        try
        {
            if (D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
                using (debug) debug!.EnableDebugLayer();
            using var input = new SpectrumAnalyzer(captureAudio: false);
            ProbeSignal.Update(input, 60, true);
            long started = Stopwatch.GetTimestamp();
            Task work = warmup.Start();
            Require(ReferenceEquals(work, warmup.Start()), "Warmup must be single-flight");
            Require(Stopwatch.GetElapsedTime(started).TotalMilliseconds < 1000, "Start blocked the caller");
            using (var live = new GpuGraphics(input, static (_, _, _) => { }, 320, 180,
                static () => new SonicGpuEffect(), new(50, ReconstructionMode.Dlss, 80), deferReconstruction: true))
            {
                Require(live.Reconstruction.Active == ReconstructionMode.Fxaa, "Startup must render spatial fallback");
                int frames = 0;
                do
                {
                    live.Render(1d / 60, 200, 1000);
                    frames++;
                } while (!work.IsCompleted && Stopwatch.GetElapsedTime(started).TotalSeconds < 90);
                Require(work.Wait(TimeSpan.FromSeconds(30)), "Warmup did not finish");
                Require(warmup.IsReady, "Warmup did not publish completion");
                live.CompleteWarmup();
                warmup.ReleaseDeviceCache();
                var preferred = (live.Reconstruction.Capabilities & (1u << (int)ReconstructionMode.Dlss)) != 0
                    ? ReconstructionMode.Dlss : ReconstructionMode.Fsr;
                live.Configure(new(50, preferred, 80));
                uint expected = live.Reconstruction.Capabilities | 7u;
                Require((warmup.PreparedModes & expected) == expected, "Warmup skipped a supported mode");
                if ((expected & (1u << (int)ReconstructionMode.Dlss)) != 0)
                    Require(warmup.PreparedDlssPresets == 15, "Warmup skipped a DLSS preset");
                live.Render(1d / 60, 200, 1000);
                CheckPixels(live.ReadLinearScene(live.Reconstruction.Active >= ReconstructionMode.XeSS));
                live.VerifyDebugMessages();
                Console.WriteLine($"PASS background warmup modes=0x{warmup.PreparedModes:X}, DLSS=0x{warmup.PreparedDlssPresets:X}, elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1}, concurrent offscreen frames={frames}");

                foreach (var mode in new[] { ReconstructionMode.Fxaa, ReconstructionMode.Smaa, ReconstructionMode.XeSS, ReconstructionMode.Fsr, ReconstructionMode.Dlss })
                {
                    if (mode >= ReconstructionMode.XeSS && (expected & (1u << (int)mode)) == 0) continue;
                    long switching = Stopwatch.GetTimestamp();
                    live.Configure(new(67, mode, 80));
                    live.Render(1d / 60, 200, 1000);
                    Require(live.Reconstruction.Active == mode, "Warmed mode fell back: " + mode);
                    Console.WriteLine($"WARMED mode={mode}, configureAndFirstOffscreenFrameMs={Stopwatch.GetElapsedTime(switching).TotalMilliseconds:F1}");
                    foreach (var size in new[] { (321, 181), (480, 270), (320, 180) })
                    {
                        uint presents = live.ProbePresentCount;
                        live.Resize(size.Item1, size.Item2, new(50, mode, 80));
                        Require(live.ProbePresentCount == presents, "Resize submitted a black/placeholder frame");
                        Require(live.RenderSize == GpuRenderSize.Create(size.Item1, size.Item2, 50), "Physical input/output size mismatch");
                        live.Render(1d / 60, 200, 1000);
                        CheckPixels(live.ReadLinearScene(mode >= ReconstructionMode.XeSS));
                        live.VerifyDebugMessages();
                    }
                }
                for (int i = 0; i < 16; i++) live.Render(1d / 60, 200, 1000);
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 32; i++) live.Render(1d / 60, 200, 1000);
                Require(GC.GetAllocatedBytesForCurrentThread() == allocation, "Steady render allocated after resize/warmup");
                Console.WriteLine("PASS AA/SR resize has no extra Present, finite nonblank output, D3D12 validation, 0 B/frame");
            }
            VerifyWorker(input, warmup);
            Task stop = warmup.StopAsync();
            Require(ReferenceEquals(stop, warmup.StopAsync()), "Warmup stop is not idempotent");
            Require(stop.Wait(TimeSpan.FromSeconds(30)), "Warmup shutdown did not drain");
            VerifyCancelledStartup();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { warmup.StopAsync().GetAwaiter().GetResult(); }
    }

    private static void VerifyWorker(SpectrumAnalyzer input, GpuShaderWarmup warmup)
    {
        using var resized = new AutoResetEvent(false);
        int callbacks = 0, width = 0, height = 0, failed = 0;
        var effect = new EffectDescriptor("probe-sonic", "", EffectHost.Gpu, false, static () => new SonicGpuEffect(), null);
        var settings = new GpuRenderSettings(320, 180, true, false, 200, 1000, 60, effect, new(100, ReconstructionMode.Fxaa, 80));
        var worker = new GpuRenderer(input, 0, (chain, w, h) =>
            {
                if (chain == 0) return;
                Volatile.Write(ref width, w);
                Volatile.Write(ref height, h);
                Interlocked.Increment(ref callbacks);
                resized.Set();
            }, status => { if (status == HdrOutputMode.Failed) Interlocked.Increment(ref failed); }, settings,
            static _ => { }, new RenderDebugStatistics(), warmup);
        worker.Start();
        try
        {
            Require(resized.WaitOne(15000), "Worker did not produce first frame");
            for (int i = 1; i <= 20; i++)
            {
                settings = settings with { Width = 320 + i * 5, Height = 180 + i * 3 };
                worker.Configure(settings);
                Thread.Sleep(10);
            }
            Require(Volatile.Read(ref callbacks) == 1, "Resize storm rebuilt intermediate dimensions");
            Require(resized.WaitOne(15000), "Final resize was lost");
            Require(width == 420 && height == 240, "Worker did not apply latest dimensions");
            Require(worker.PauseAsync().Wait(TimeSpan.FromSeconds(10)), "Pause did not acknowledge frame barrier");
            settings = settings with { Width = 801, Height = 451, Active = false };
            worker.Configure(settings);
            settings = settings with { Width = 641, Height = 361, Active = true };
            worker.Configure(settings);
            Require(resized.WaitOne(15000) && width == 641 && height == 361, "Resume lost final resize");
            Require(failed == 0, "Worker entered GPU recovery");
            Console.WriteLine("PASS real worker: resize storm coalescing, final dimensions, pause/resume, asynchronous stop");
        }
        finally { Require(worker.StopAsync().Wait(TimeSpan.FromSeconds(30)), "Worker stop did not finish"); }
    }

    private static void VerifyCancelledStartup()
    {
        var cancelled = new GpuShaderWarmup();
        Task work = cancelled.Start();
        Task stop = cancelled.StopAsync();
        Require(stop.Wait(TimeSpan.FromSeconds(30)) && work.IsCompleted, "Startup cancellation released before work finished");
        Require(ReferenceEquals(stop, cancelled.Start()), "Stopped warmup restarted");
        Console.WriteLine("PASS startup cancellation, repeated stop and no restart after shutdown");
    }

    private static void CheckPixels(float4[] pixels)
    {
        double energy = 0;
        foreach (var pixel in pixels)
        {
            Require(float.IsFinite(pixel.X) && float.IsFinite(pixel.Y) && float.IsFinite(pixel.Z), "Nonfinite scene");
            energy += Math.Max(pixel.X, Math.Max(pixel.Y, pixel.Z));
        }
        Require(energy / pixels.Length > .001, "Blank scene");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

namespace WinExSpectrumTest.Rendering
{
    internal sealed unsafe partial class GpuGraphics
    {
        internal uint ProbePresentCount => _swapChain.LastPresentCount;
    }
}
