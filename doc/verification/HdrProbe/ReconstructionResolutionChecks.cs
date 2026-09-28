using ComputeSharp;
using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Rendering;

internal static class ReconstructionResolutionChecks
{
    internal static int Run(ReconstructionMode mode = ReconstructionMode.Dlss)
    {
        try
        {
            if (D3D12.D3D12GetDebugInterface<ID3D12Debug>(out var debug).Success)
            {
                using (debug) debug!.EnableDebugLayer();
            }
            using var compute = GraphicsDevice.GetDefault();
            Console.WriteLine($"OFFSCREEN device={compute.Name} syntheticAudio=true");
            using var input = new SpectrumAnalyzer(captureAudio: false);
            foreach (var (width, height) in new[] { (960, 540), (3840, 2160) })
            {
                using var renderer = new GpuGraphics(input, static _ => { }, width, height, static () => new SonicGpuEffect(), new(100, false, 80));
                foreach (int scale in new[] { 50, 49, 40, 34, 33, 32, 10, 1, 67, 75, 90, 99, 100 })
                {
                    renderer.Configure(new(scale, mode, 80, "k"));
                    Verify(renderer, input, $"scale={scale}");
                    if (renderer.ProfileSize != GpuRenderSize.Create(width, height, scale))
                        throw new InvalidOperationException("Requested scale was silently changed");
                }
                renderer.ConfigureExactInputForProbe(width / 3, height / 3, mode);
                Verify(renderer, input, "exactThird");
                foreach (string preset in mode == ReconstructionMode.Dlss ? new[] { "j", "k", "l", "m" } : new[] { "k" })
                {
                    renderer.ConfigureExactInputForProbe(16, 9, mode, preset);
                    Verify(renderer, input, "exact16x9 preset=" + preset);
                }
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Verify(GpuGraphics renderer, SpectrumAnalyzer input, string label)
    {
        for (int i = 0; i < 16; i++)
        {
            ProbeSignal.Update(input, i, true);
            renderer.Render(1d / 120, 200, 1000);
        }
        var state = renderer.Reconstruction;
        Console.WriteLine($"SCALE output={renderer.Width}x{renderer.Height} {label} input={renderer.ProfileSize.Width}x{renderer.ProfileSize.Height} active={state.Active} unsupported={state.UnsupportedScale}");
        if (state.Active != state.Requested || state.UnsupportedScale)
            throw new InvalidOperationException($"{state.Requested} unexpectedly fell back: {label}");
        var pixels = renderer.ReadLinearScene(true);
        double energy = 0;
        foreach (var pixel in pixels)
        {
            if (!float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y) || !float.IsFinite(pixel.Z))
                throw new InvalidOperationException("Non-finite reconstruction output: " + label);
            energy += Math.Max(pixel.X, Math.Max(pixel.Y, pixel.Z));
        }
        if (energy / pixels.Length < .001) throw new InvalidOperationException("Blank reconstruction output: " + label);
        renderer.VerifyDebugMessages();
        Console.WriteLine($"PASS energy={energy / pixels.Length:F4}");
    }
}
