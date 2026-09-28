namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    // Exercise the production resource/SDK path with dimensions the integer slider cannot express.
    internal void ConfigureExactInputForProbe(int width, int height, ReconstructionMode mode, string preset = "k")
    {
        WaitForGpu();
        _options = new(1, mode, 80, preset);
        CreateSceneResources(_options, new GpuRenderSize(Width, Height, width, height));
        ResetHistory();
    }
}
