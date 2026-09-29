using ComputeSharp;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

internal static class DlssNativeChecks
{
    internal static int Run(bool debugLayer)
    {
        try
        {
            if (debugLayer && D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
                using (debug) debug!.EnableDebugLayer();
            AppSettings.SonicAutoRotate = true;
            Console.WriteLine($"DLSS native regression: 3840x2160, synthetic audio, unpaced, debugLayer={debugLayer}");
            foreach (string preset in new[] { "m", "l", "k", "j" })
            foreach (int scale in new[] { 100, 99 })
            foreach (int count in new[] { 255, 256 })
            {
                float4[] reference = Capture(preset, scale, count, serialized: true);
                float4[] pipeline = Capture(preset, scale, count, serialized: false);
                double error = 0;
                float maximum = 0;
                // Exclude the bottom strip where a user-enabled NVIDIA indicator may print timing.
                int samples = 3840 * (2160 - 160);
                for (int i = 0; i < samples; i++)
                {
                    var a = reference[i];
                    var b = pipeline[i];
                    // ComputeSharp vector arithmetic is shader-only; compare CPU scalar fields.
                    float value = Math.Max(Math.Abs(a.X - b.X), Math.Max(Math.Abs(a.Y - b.Y), Math.Abs(a.Z - b.Z)));
                    if (!float.IsFinite(value)) throw new InvalidOperationException("Nonfinite DLSS output");
                    error += value;
                    maximum = Math.Max(maximum, value);
                }
                error /= samples;
                Console.WriteLine($"DLSS-NATIVE preset={preset} scale={scale} frames={count} mean={error:F7} max={maximum:F7}");
                if (error > .0001 || maximum > .01)
                    throw new InvalidOperationException("Pipelined DLSS differs from serialized reference");
            }
            using var input = new SpectrumAnalyzer(captureAudio: false);
            using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 960, 540,
                static () => new SonicGpuEffect(), new(100, ReconstructionMode.Dlss, 160, "m"));
            renderer.VerifyDlssSceneOverlap();
            renderer.VerifyDispatchFailureWithFramesInFlight();
            renderer.VerifyDebugMessages();
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static float4[] Capture(string preset, int scale, int count, bool serialized)
    {
        using var input = new SpectrumAnalyzer(captureAudio: false);
        using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 3840, 2160,
            static () => new SonicGpuEffect(), new(scale, ReconstructionMode.Dlss, 160, preset));
        if (renderer.Reconstruction.Active != ReconstructionMode.Dlss)
            throw new InvalidOperationException("DLSS unavailable");
        renderer.SeedSimulationForProbe();
        return renderer.CaptureDlssFinalWithoutCopies(input, count, serialized);
    }
}

namespace WinExSpectrumTest.Rendering
{
    internal sealed unsafe partial class GpuGraphics
    {
        internal float4[] CaptureDlssFinalWithoutCopies(SpectrumAnalyzer input, int count, bool serialized)
        {
            // Do not insert per-frame copies: their resource transitions hid the original race.
            for (int i = 0; i < count; i++)
            {
                ProbeSignal.Update(input, i, true);
                if (serialized) RenderDlssSerializedForProbe();
                else Render(1d / 120, 200, 1000);
            }
            var pixels = ReadLinearScene(true);
            VerifyDebugMessages();
            return pixels;
        }

        private void RenderDlssSerializedForProbe()
        {
            // Reproduce the pre-pipeline path: one input slot and one scene+SR command list.
            _frameIndex = 0;
            ((IBufferedGpuEffect)_effect).SelectFrame(0);
            var frame = _temporal!.BeginFrame(1d / 120);
            ((ITemporalGpuEffect)_effect).SetTemporalFrame(frame);
            _effect.PrepareFrame(1d / 120, _options.Detail);
            BeginCommands();
            Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            _effect.RecordScene(_commands, Rtv(2));
            Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            if (!RecordReconstruction(in frame, 1d / 120)) throw new InvalidOperationException("DLSS reference failed");
            ComposeAndPresent(200, 1000);
        }

        internal void VerifyDlssSceneOverlap()
        {
            WaitForGpu();
            if (!_temporal!.RequiresPreviousDispatchCompletion)
                throw new InvalidOperationException("Probe requires DLSS native L/M dispatch protection");
            using var gate = _device.CreateFence(0);
            ulong twoScenes = _sceneFenceValue + 2;
            int scenesFinished = 0;
            var release = new Thread(() =>
            {
                try
                {
                    if (SpinWait.SpinUntil(() => _sceneFence!.CompletedValue >= twoScenes, 5000))
                        Volatile.Write(ref scenesFinished, 1);
                }
                finally { gate.Signal(1); }
            }) { IsBackground = true };
            _queue.Wait(gate, 1).CheckError();
            release.Start();
            try
            {
                Render(1d / 120, 200, 1000);
                // This call submits scene #2 before waiting for the SDK fence from frame #1.
                Render(1d / 120, 200, 1000);
            }
            finally
            {
                release.Join();
                WaitForGpu();
            }
            if (Volatile.Read(ref scenesFinished) == 0)
                throw new InvalidOperationException("SDK protection serialized the scene behind SR");
            Console.WriteLine("PASS DLSS SDK fence preserves GPU overlap: two scenes finish while post is blocked");
        }
    }
}
