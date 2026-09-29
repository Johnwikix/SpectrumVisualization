using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Rendering;
using Vortice.DXGI;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using System.Diagnostics;
using System.Text.Json;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Manager;

internal static class PresentationChecks
{
    internal static int Run()
    {
        try
        {
            if (D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
                using (debug) debug!.EnableDebugLayer();
            VerifyStatistics();
            using var input = new SpectrumAnalyzer(captureAudio: false);
            foreach (bool requestTearing in new[] { true, false })
            {
                using var renderer = new GpuGraphics(input, static (_, _, _) => { }, 960, 540, static () => new SonicGpuEffect(), new(100, true, 80), requestTearing);
                renderer.VerifyPresentationConfiguration(requestTearing);
                renderer.CaptureTimings = true;
                for (int i = 0; i < 32; i++) { ProbeSignal.Update(input, i, true); renderer.Render(1d / 120, 200, 1000); }
                long allocation = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 96; i++) renderer.Render(1d / 120, 200, 1000);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
                if (bytes != 0) throw new InvalidOperationException("Presentation/timing hot path allocated: " + bytes);
                if (!renderer.LastFrameRendered || !double.IsFinite(renderer.LastPresentMilliseconds) || !double.IsFinite(renderer.LastGpuWaitMilliseconds))
                    throw new InvalidOperationException("Rendered-frame accounting or timing is invalid");
                renderer.Resize(801, 451);
                renderer.Render(1d / 120, 200, 1000);
                renderer.VerifyPresentationConfiguration(requestTearing);
                renderer.Resize(960, 540);
                renderer.Render(1d / 120, 200, 1000);
                var pixels = renderer.ReadLinearScene();
                if (!pixels.Any(p => float.IsFinite(p.X) && p.X > .001f)) throw new InvalidOperationException("Blank scene after resize");
                renderer.VerifyDebugMessages();
                Console.WriteLine($"PASS presentation requestTearing={requestTearing}, resize, GPU drain, readback, hot path={bytes} B");
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void VerifyStatistics()
    {
        var stats = new RenderDebugStatistics();
        var frame = new RenderDebugFrame(true, true, 2, .25, 1, new(960, 540, 480, 270), ReconstructionMode.Fxaa, "k", false, true, 0);
        stats.SetEnabled(false);
        stats.Record(frame);
        if (stats.Sample().Frames != 0) throw new InvalidOperationException("Disabled diagnostics still record");
        stats.SetEnabled(true);
        for (int i = 0; i < 100; i++) stats.Record(frame with { Submitted = i < 70 });
        var sample = stats.Sample();
        if (sample.Frames != 100 || sample.Submissions != 70 || sample.Dropped != 30 || sample.CpuMilliseconds != 2 || sample.PresentMilliseconds != .25 || sample.WaitMilliseconds != 1)
            throw new InvalidOperationException("Render/submit/drop accounting is incorrect");
        long allocation = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) stats.Record(frame);
        if (GC.GetAllocatedBytesForCurrentThread() != allocation) throw new InvalidOperationException("Diagnostics hot path allocated");
        stats.SetEnabled(false);
        stats.SetEnabled(true);
        if (stats.Sample().Frames != 0) throw new InvalidOperationException("Enable transition includes stale samples");
        var saved = JsonSerializer.Deserialize("{}", SettingsJsonContext.Default.SaveSetting)!;
        if (saved.ShowDebugOverlay) throw new InvalidOperationException("Overlay must default off");
        saved.ShowDebugOverlay = true;
        if (!JsonSerializer.Deserialize(JsonSerializer.Serialize(saved, SettingsJsonContext.Default.SaveSetting), SettingsJsonContext.Default.SaveSetting)!.ShowDebugOverlay)
            throw new InvalidOperationException("Overlay setting did not persist");
        Console.WriteLine("PASS disabled diagnostics, render/submit/drop counters, allocation, transitions and AOT JSON");
    }
}

namespace WinExSpectrumTest.Rendering
{
    internal sealed unsafe partial class GpuGraphics
    {
        internal void VerifyPresentationConfiguration(bool requestTearing)
        {
            var flags = _swapChain.Description1.Flags;
            Console.WriteLine($"OFFSCREEN tearingSupported={_factory.PresentAllowTearing} flags={flags}");
            if (requestTearing && _factory.PresentAllowTearing && (flags & SwapChainFlags.AllowTearing) == 0)
                throw new InvalidOperationException("Uncapped presentation is missing AllowTearing");
            if (!requestTearing && AllowsTearing) throw new InvalidOperationException("Compatibility chain unexpectedly allows tearing");
            if ((flags & SwapChainFlags.FrameLatencyWaitableObject) == 0 || _swapChain.MaximumFrameLatency != 2)
                throw new InvalidOperationException("Presentation latency is not explicitly configured");
        }
    }
}
