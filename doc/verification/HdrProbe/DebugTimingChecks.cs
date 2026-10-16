using System.Diagnostics;
using ComputeSharp;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Rendering;

internal static class DebugTimingChecks
{
    internal static int Run(bool debugLayer)
    {
        try
        {
            if (debugLayer && D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
                using (debug) debug!.EnableDebugLayer();
            using var device = GraphicsDevice.GetDefault();
            Console.WriteLine($"OFFSCREEN device={device.Name}, debugLayer={debugLayer}, syntheticAudio=true, output=960x540, grid=80");
            VerifyStatistics();
            using var input = new SpectrumAnalyzer(captureAudio: false);
            using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 960, 540,
                static () => new SonicGpuEffect(), new(67, ReconstructionMode.Fxaa, 80));
            renderer.VerifyTimingTransitions();
            renderer.VerifySharedQualityWithSecondEffect();
            foreach (var mode in new[] { ReconstructionMode.Off, ReconstructionMode.Fxaa, ReconstructionMode.Smaa,
                ReconstructionMode.Fsr, ReconstructionMode.XeSS, ReconstructionMode.Dlss })
            {
                renderer.Configure(new(67, mode, 80));
                if (renderer.Reconstruction.Active != mode)
                {
                    Console.WriteLine($"SKIP unavailable mode={mode}, active={renderer.Reconstruction.Active}");
                    continue;
                }
                renderer.CaptureTimings = true;
                renderer.ForceNextTimingSample();
                int samples = 0;
                double gpu = 0;
                for (int i = 0; i < 96; i++)
                {
                    ProbeSignal.Update(input, i, true);
                    renderer.Render(1d / 120, 200, 1000);
                    if (renderer.LastAntialiasMilliseconds is double milliseconds)
                    {
                        Require(double.IsFinite(milliseconds) && milliseconds > 0, "Invalid GPU time");
                        samples++;
                        gpu += milliseconds;
                    }
                }
                Require(mode == ReconstructionMode.Off ? samples == 0 : samples > 0, "GPU time missing or Off was sampled");
                renderer.VerifyDebugMessages();
                Console.WriteLine($"PASS mode={mode}, GPU samples={samples}, averageMs={(samples == 0 ? 0 : gpu / samples):F4}");
                if (debugLayer) continue;

                // Alternating order exposes warmup / power / scheduling noise. Includes the
                // production statistics recorder and drains at both ends, never display FPS.
                var stats = new RenderDebugStatistics();
                for (int round = 0; round < 4; round++)
                {
                    bool enabled = round is 1 or 2;
                    renderer.CaptureTimings = enabled;
                    stats.SetEnabled(enabled);
                    for (int i = 0; i < 240; i++) renderer.Render(1d / 120, 200, 1000);
                    renderer.Drain();
                    const int frameCount = 6000;
                    int timingSamples = 0;
                    long bytes = GC.GetAllocatedBytesForCurrentThread();
                    long started = Stopwatch.GetTimestamp();
                    for (int i = 0; i < frameCount; i++)
                    {
                        long frameStart = enabled ? Stopwatch.GetTimestamp() : 0;
                        bool submitted = renderer.Render(1d / 120, 200, 1000);
                        if (renderer.LastAntialiasMilliseconds != null) timingSamples++;
                        if (enabled)
                            stats.Record(new(true, submitted, Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds,
                                renderer.LastPresentMilliseconds, renderer.LastGpuWaitMilliseconds, renderer.RenderSize,
                                mode, "k", false, renderer.AllowsTearing, 0, renderer.LastAntialiasMilliseconds));
                    }
                    renderer.Drain();
                    double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds / frameCount;
                    bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
                    Require(bytes == 0, "Timing hot path allocated: " + bytes);
                    Require(!enabled || mode == ReconstructionMode.Off || timingSamples >= 2, "Benchmark did not exercise repeated GPU sampling");
                    Console.WriteLine($"BENCH mode={mode}, diagnostics={enabled}, round={round}, offscreenFrameMs={elapsed:F5}, allocation={bytes} B, GPU samples={timingSamples}");
                }
            }
            renderer.CaptureTimings = true;
            renderer.Configure(new(67, ReconstructionMode.Fsr, 80));
            renderer.VerifyPostQueueIsolation();
            renderer.VerifyDispatchFailureWithFramesInFlight();
            renderer.Render(1d / 120, 200, 1000);
            renderer.VerifyDebugMessages();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void VerifyStatistics()
    {
        var stats = new RenderDebugStatistics();
        stats.SetEnabled(true);
        var frame = new RenderDebugFrame(true, true, 2, .2, 1, new(960, 540, 640, 360), ReconstructionMode.Fxaa, "k", false, true, 0);
        stats.Record(frame with { AntialiasMilliseconds = 2 });
        for (int i = 0; i < 100; i++) stats.Record(frame);
        stats.Record(frame with { AntialiasMilliseconds = 4 });
        Require(stats.Sample().AntialiasMilliseconds == 3, "Sparse GPU samples were averaged as per-frame zeros");
        stats.Record(frame with { AntialiasMilliseconds = 8 });
        stats.Record(frame with { Mode = ReconstructionMode.Smaa });
        Require(stats.Sample().AntialiasMilliseconds == null, "A previous algorithm's GPU time survived a mode switch");
        stats.Record(frame with { AntialiasMilliseconds = 8 });
        stats.SetEnabled(false);
        stats.SetEnabled(true);
        Require(stats.Sample().AntialiasMilliseconds == null, "Re-enable retained stale timing");

        for (int pass = 0; pass < 2; pass++)
        {
            stats.SetEnabled(pass == 1);
            for (int i = 0; i < 10000; i++) stats.Record(frame);
            long bytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < 1000000; i++) stats.Record(frame);
            double nanoseconds = Stopwatch.GetElapsedTime(start).TotalNanoseconds / 1000000;
            bytes = GC.GetAllocatedBytesForCurrentThread() - bytes;
            Require(bytes == 0, "Statistics allocated");
            Console.WriteLine($"STATS enabled={pass == 1}, nsPerRecord={nanoseconds:F2}, allocation={bytes} B");
        }
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
        internal void ForceNextTimingSample() => _aaNextSampleAt = 0;

        internal void VerifySharedQualityWithSecondEffect()
        {
            var saved = Model.AppSettings.GpuQuality;
            try
            {
                var descriptor = new Effects.EffectDescriptor("probe-shared-quality", "", Effects.EffectHost.Gpu, false,
                    static () => new ProbeEffect(static () => { }, false));
                Model.AppSettings.GpuQuality = new(50, "smaa", "m");
                ChangeEffect(descriptor.CreateGpu!);
                Configure(descriptor.GetSceneOptions());
                Render(1d / 120, 200, 1000);
                if (_size.Width != 480 || _size.Height != 270 || _activeMode != ReconstructionMode.Smaa ||
                    Math.Abs(ReadLinearScene()[0].X - .25f) > .001f)
                    throw new InvalidOperationException("Second GPU effect did not inherit shared scale / AA");
                Model.AppSettings.GpuAntiAliasing = "dlss";
                Configure(descriptor.GetSceneOptions());
                Render(1d / 120, 200, 1000);
                if (Reconstruction.Requested != ReconstructionMode.Dlss || _activeMode != ReconstructionMode.Fxaa)
                    throw new InvalidOperationException("Effect without temporal inputs did not fall back safely");
                VerifyDebugMessages();
                Console.WriteLine("PASS second GPU effect inherits shared scale / SMAA and temporal selection falls back without inputs");
            }
            finally
            {
                Model.AppSettings.GpuQuality = saved;
                ChangeEffect(static () => new SonicGpuEffect());
            }
        }

        internal void VerifyTimingTransitions()
        {
            CaptureTimings = false;
            Render(1d / 120, 200, 1000);
            if (_aaQueries != null || LastAntialiasMilliseconds != null)
                throw new InvalidOperationException("Disabled timing allocated query resources");
            CaptureTimings = true;
            Render(1d / 120, 200, 1000);
            Drain();
            Render(1d / 120, 200, 1000);
            if (LastAntialiasMilliseconds is not > 0)
                throw new InvalidOperationException("Completed timestamp was not consumed");
            Render(1d / 120, 200, 1000);
            if (LastAntialiasMilliseconds != null)
                throw new InvalidOperationException("A query result was counted twice");
            ForceNextTimingSample();
            Render(1d / 120, 200, 1000);
            Resize(801, 451, new(67, ReconstructionMode.Smaa, 80));
            Render(1d / 120, 200, 1000);
            if (LastAntialiasMilliseconds != null)
                throw new InvalidOperationException("Resize / mode change published stale timing");
            Drain();
            CaptureTimings = false;
            CaptureTimings = true;
            Render(1d / 120, 200, 1000);
            if (LastAntialiasMilliseconds != null)
                throw new InvalidOperationException("Disable / enable published stale timing");
            Resize(960, 540);
            VerifyDebugMessages();
            Console.WriteLine("PASS lazy diagnostics, exactly-once query consumption, resize / mode / enable transitions");
        }
    }
}
