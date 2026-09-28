using System.Diagnostics;
using ComputeSharp;
using ComputeSharp.Interop;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;

internal static class ReconstructionChecks
{
    internal static unsafe int DeviceCapabilities()
    {
        try
        {
            using var compute = GraphicsDevice.GetDefault();
            Console.WriteLine($"Selected device: {compute.Name}");
            Guid deviceId = new("189819F1-1DB6-4B57-BE54-1821339B85F7");
            void* native = null;
            InteropServices.GetID3D12Device(compute, &deviceId, &native);
            using var device = new ID3D12Device((nint)native);
            Console.WriteLine($"Reconstruction capabilities: 0x{TemporalReconstruction.Capabilities(device):X}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    internal static int Run(bool motionOnly = false, bool dlssOnly = false)
    {
        try
        {
            if (!dlssOnly && D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
            {
                using (debug) debug!.EnableDebugLayer();
                Console.WriteLine("D3D12 debug layer enabled (offscreen only)");
            }
            using var input = new SpectrumAnalyzer(captureAudio: false);
            QualityChecks.Run(static (condition, message) => { if (!condition) throw new InvalidOperationException(message); }, new SaveSetting());
            AppSettings.SonicAutoRotate = true;
            using var renderer = new GpuGraphics(input, _ => { }, 960, 540, static () => new SonicGpuEffect(), new(100, false, 80));
            Console.WriteLine($"Reconstruction capabilities: 0x{renderer.Reconstruction.Capabilities:X}");
            if (dlssOnly && (renderer.Reconstruction.Capabilities & (1u << (int)ReconstructionMode.Dlss)) == 0)
                throw new InvalidOperationException("DLSS capability is unavailable on the selected GPU");
            if (motionOnly) { renderer.VerifyTemporalMotion(); renderer.VerifyDebugMessages(); return 0; }
            foreach (var mode in dlssOnly ? new[] { ReconstructionMode.Dlss } : new[] { ReconstructionMode.Off, ReconstructionMode.Fxaa, ReconstructionMode.Smaa, ReconstructionMode.XeSS, ReconstructionMode.Fsr, ReconstructionMode.Dlss })
            foreach (int scale in mode >= ReconstructionMode.XeSS ? new[] { 100, 75, 50, 34, 33, 1, 67 } : new[] { 75, 1 })
            {
                bool supported = mode < ReconstructionMode.XeSS || (renderer.Reconstruction.Capabilities & (1u << (int)mode)) != 0;
                renderer.Configure(new(scale, mode, 80));
                bool sizeUnsupported = renderer.Reconstruction.UnsupportedScale;
                if (supported && sizeUnsupported)
                    throw new InvalidOperationException("A fixed input size was rejected before SDK execution");
                if (mode < ReconstructionMode.XeSS) renderer.VerifyOutputEncoding();
                for (int i = 0; i < 32; i++)
                {
                    ProbeSignal.Update(input, i, true);
                    renderer.Render(1d / 120, 200, 1000);
                }
                var status = renderer.Reconstruction;
                Console.WriteLine($"MODE requested={mode} scale={scale} active={status.Active} unsupportedScale={status.UnsupportedScale} input={renderer.ProfileSize.Width}x{renderer.ProfileSize.Height}");
                if (supported && !sizeUnsupported && status.Active != mode) throw new InvalidOperationException("Supported algorithm failed to initialize or dispatch");
                if (renderer.ProfileSize != GpuRenderSize.Create(960, 540, scale)) throw new InvalidOperationException("SDK changed the requested render scale");
                if (!supported && status.Active != ReconstructionMode.Fxaa) throw new InvalidOperationException("Unavailable algorithm did not fall back");
                var pixels = renderer.ReadLinearScene(status.Active >= ReconstructionMode.XeSS);
                double energy = 0;
                foreach (var pixel in pixels)
                {
                    if (!float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y) || !float.IsFinite(pixel.Z)) throw new InvalidOperationException("Non-finite reconstructed output");
                    energy += Math.Max(pixel.X, Math.Max(pixel.Y, pixel.Z));
                }
                if (energy / pixels.Length < .001) throw new InvalidOperationException("Blank reconstructed output");
                if (status.Active >= ReconstructionMode.XeSS && scale == 67)
                    ProbeImages.Save($"HdrProbe/bin/{mode}.bmp", pixels, 960, 540);
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < 32; i++) renderer.Render(1d / 120, 200, 1000);
                double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds / 32;
                long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Console.WriteLine($"PASS energy={energy / pixels.Length:F4} offscreenCpuFenceMs={ms:F3} managedBytesPerFrame={bytes / 32.0:F1}");
                renderer.ResetHistory();
                renderer.Render(1d / 120, 200, 1000);
                renderer.Resize(801, 451);
                renderer.Render(1d / 120, 200, 1000);
                renderer.Resize(960, 540);
                renderer.Render(1d / 120, 200, 1000);
                if (renderer.Reconstruction.Active != status.Active || renderer.Reconstruction.UnsupportedScale != sizeUnsupported)
                    throw new InvalidOperationException("Resize failed to restore reconstruction status");
                renderer.VerifyDebugMessages();
            }
            if ((renderer.Reconstruction.Capabilities & (1u << (int)ReconstructionMode.Dlss)) != 0)
            {
                foreach (string preset in new[] { "j", "k", "l", "m", "k" })
                {
                    // Change presets on the same host, including a non-preset input ratio.
                    renderer.Configure(new(63, ReconstructionMode.Dlss, 80, preset));
                    for (int i = 0; i < 16; i++) renderer.Render(1d / 120, 200, 1000);
                    if (renderer.Reconstruction.Active != ReconstructionMode.Dlss)
                        throw new InvalidOperationException("DLSS preset failed: " + preset);
                    var pixels = renderer.ReadLinearScene(true);
                    double energy = 0;
                    foreach (var pixel in pixels)
                    {
                        if (!float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y) || !float.IsFinite(pixel.Z))
                            throw new InvalidOperationException("Non-finite DLSS preset output: " + preset);
                        energy += Math.Max(pixel.X, Math.Max(pixel.Y, pixel.Z));
                    }
                    if (energy / pixels.Length < .001) throw new InvalidOperationException("Blank DLSS preset output: " + preset);
                    renderer.VerifyDebugMessages();
                    Console.WriteLine($"PASS DLSS preset={preset} scale=63 input={renderer.ProfileSize.Width}x{renderer.ProfileSize.Height}");
                }
            }
            renderer.VerifyTemporalMotion();
            if (!dlssOnly)
            {
                renderer.VerifySpatialEdges();
                renderer.VerifyEffectContract();
            }
            renderer.VerifyDebugMessages();
            Console.WriteLine("PASS reconstruction mode/scale/preset changes, history reset, odd-size resize, fallback and teardown");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
