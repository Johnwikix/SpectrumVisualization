using System.Diagnostics;
using ComputeSharp;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

internal static class PipelineChecks
{
    internal static int Run()
    {
        try
        {
            if (D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
            {
                using (debug) debug!.EnableDebugLayer();
            }
            using var input = new SpectrumAnalyzer(captureAudio: false);
            using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 960, 540,
                static () => new SonicGpuEffect(), new(50, ReconstructionMode.Fsr, 160));
            if (renderer.Reconstruction.Active != ReconstructionMode.Fsr)
                throw new InvalidOperationException("FSR is required for the pipeline regression");
            for (int i = 0; i < 8; i++) renderer.Render(1d / 120, 200, 1000);
            renderer.VerifyPostQueueIsolation();
            renderer.VerifyDebugMessages();
            renderer.VerifyDispatchFailureWithFramesInFlight();
            renderer.VerifyDebugMessages();
            // Dispose the failed context before starting fresh histories for each provider.
            renderer.Dispose();
            foreach (var mode in new[] { ReconstructionMode.Fsr, ReconstructionMode.XeSS, ReconstructionMode.Dlss })
            {
                float4[] reference = ReadTrajectory(mode, drainEachFrame: true);
                if (reference.Length == 0) continue;
                float4[] pipelined = ReadTrajectory(mode, drainEachFrame: false);
                double mean = 0;
                float maximum = 0;
                for (int i = 0; i < reference.Length; i++)
                {
                    var a = reference[i];
                    var b = pipelined[i];
                    float error = Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
                    if (!float.IsFinite(error)) throw new InvalidOperationException("Nonfinite pipelined output");
                    mean += error;
                    maximum = Math.Max(maximum, error);
                }
                mean /= reference.Length;
                if (mean > .0001 || maximum > .01f)
                    throw new InvalidOperationException($"Pipeline changed {mode} history/output: mean={mean}, max={maximum}");
                Console.WriteLine($"PASS {mode} 96-frame history at four checkpoints matches drained reference: mean={mean:F7}, max={maximum:F7}");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static float4[] ReadTrajectory(ReconstructionMode mode, bool drainEachFrame)
    {
        using var input = new SpectrumAnalyzer(captureAudio: false);
        using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 801, 451,
            static () => new SonicGpuEffect(), new(67, mode, 160));
        if (renderer.Reconstruction.Active != mode)
        {
            Console.WriteLine($"SKIP unavailable mode={mode}");
            return [];
        }
        AppSettings.SonicAutoRotate = true;
        renderer.SeedSimulationForProbe();
        var checkpoints = new List<float4>();
        for (int i = 0; i < 96; i++)
        {
            ProbeSignal.Update(input, i, true);
            if (i == 35) renderer.Configure(new(67, mode, 320));
            if (i == 47) renderer.ResetHistory();
            if (i == 61) renderer.Resize(641, 361, new(75, mode, 80));
            renderer.Render(1d / 120, 200, 1000);
            if (drainEachFrame) renderer.Drain();
            if (i is 34 or 46 or 60 or 95)
                checkpoints.AddRange(renderer.ReadLinearScene(reconstructed: true));
        }
        renderer.VerifyDebugMessages();
        return checkpoints.ToArray();
    }

    internal static int Benchmark()
    {
        using var device = GraphicsDevice.GetDefault();
        Console.WriteLine($"OFFSCREEN pipeline device={device.Name} output=3840x2160 input=50% grid=160 syntheticAudio=true debugLayer=false");
        AppSettings.SonicAutoRotate = true;
        foreach (var mode in new[] { ReconstructionMode.Fxaa, ReconstructionMode.Fsr, ReconstructionMode.XeSS, ReconstructionMode.Dlss })
        {
            using var input = new SpectrumAnalyzer(captureAudio: false);
            using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 3840, 2160,
                static () => new SonicGpuEffect(), new(50, mode, 160));
            if (renderer.Reconstruction.Active != mode)
            {
                Console.WriteLine($"SKIP unavailable mode={mode}");
                continue;
            }
            renderer.CaptureTimings = true;
            for (int i = 0; i < 120; i++)
            {
                ProbeSignal.Update(input, i, true);
                renderer.Render(1d / 120, 200, 1000);
            }
            renderer.DrainForProbe();
            const int count = 360;
            double wait = 0, present = 0;
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++)
            {
                ProbeSignal.Update(input, 120 + i, true);
                renderer.Render(1d / 120, 200, 1000);
                wait += renderer.LastGpuWaitMilliseconds;
                present += renderer.LastPresentMilliseconds;
            }
            renderer.DrainForProbe();
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds / count;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Console.WriteLine($"PIPELINE mode={mode} meanIncludingDrainMs={elapsed:F4} cpuFenceWaitMs={wait / count:F4} presentMs={present / count:F4} allocatedBytesPerFrame={bytes / (double)count:F1}");
        }
        return 0;
    }
}

namespace WinExSpectrumTest.Rendering
{
    internal sealed unsafe partial class GpuGraphics
    {
        internal void DrainForProbe() => WaitForGpu();

        internal void VerifyPostQueueIsolation()
        {
            WaitForGpu();
            using var gate = _device.CreateFence(0);
            using var recorded = new ManualResetEventSlim();
            using var thirdReturned = new ManualResetEventSlim();
            int timedOut = 0;
            int reusedBusySlot = 0;
            // A bounded watchdog always releases the GPU, including on the old synchronous path.
            var release = new Thread(() =>
            {
                if (!recorded.Wait(TimeSpan.FromSeconds(5))) Volatile.Write(ref timedOut, 1);
                else if (thirdReturned.Wait(TimeSpan.FromMilliseconds(150))) Volatile.Write(ref reusedBusySlot, 1);
                gate.Signal(1);
            }) { IsBackground = true };
            _queue.Wait(gate, 1).CheckError();
            release.Start();
            try
            {
                Render(1d / 120, 200, 1000);
                Render(1d / 120, 200, 1000);
                if (Volatile.Read(ref timedOut) != 0)
                    throw new InvalidOperationException("Render waited for the deliberately blocked SR/post queue before submitting two frames");
                if (_sceneFence == null || !SpinWait.SpinUntil(() => _sceneFence.CompletedValue >= _sceneFenceValue, 2000))
                    throw new InvalidOperationException("Scene GPU work did not finish independently of the blocked post queue");
                recorded.Set();
                Render(1d / 120, 200, 1000);
                thirdReturned.Set();
                if (Volatile.Read(ref reusedBusySlot) != 0)
                    throw new InvalidOperationException("Third frame overwrote a slot still consumed by SR");
            }
            finally
            {
                recorded.Set();
                release.Join();
                WaitForGpu();
            }
            Console.WriteLine("PASS two scene frames finished on GPU while SR/post was blocked; third frame waited for a free slot");
        }

        internal void VerifyDispatchFailureWithFramesInFlight()
        {
            Render(1d / 120, 200, 1000);
            Render(1d / 120, 200, 1000);
            nint context = _temporal!.DetachContextForProbe();
            try
            {
                if (Render(1d / 120, 200, 1000)) throw new InvalidOperationException("Injected SDK failure was not observed");
                if (_activeMode != ReconstructionMode.Fxaa || PipelineEnabled)
                    throw new InvalidOperationException("Failed pipeline did not fall back to spatial AA");
                Render(1d / 120, 200, 1000);
                ReadLinearScene();
            }
            finally
            {
                WaitForGpu();
                TemporalReconstruction.DestroyDetachedForProbe(context);
            }
            Console.WriteLine("PASS SDK dispatch failure drains prior frames before fallback and resource replacement");
        }
    }

    internal sealed unsafe partial class TemporalReconstruction
    {
        // Invoke the native bridge's null-context error path without destroying in-flight SDK resources.
        internal nint DetachContextForProbe()
        {
            nint context = _context;
            _context = 0;
            return context;
        }
        internal static void DestroyDetachedForProbe(nint context) => ReconstructionDestroy(context);
    }
}
