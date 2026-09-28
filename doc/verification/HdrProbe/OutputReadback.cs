using ComputeSharp;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

// Compiled only into this probe. Exercise the production output PSO and read its RGB10 buffer.
internal sealed unsafe partial class GpuGraphics
{
    public void VerifyOutputEncoding()
    {
        if (!SetHdr(true)) throw new InvalidOperationException("Probe requires an HDR-capable composition chain");
        CheckUniform(0, 200, 1000, Pq(0));
        CheckUniform(1, 200, 1000, Pq(200));
        CheckUniform(1, 400, 1000, Pq(400));
        CheckUniform(100, 200, 1000, Pq(1000));
        CheckUniform(100, 200, 200, Pq(200));
        SetHdr(false);
        CheckUniform(0.18f, 200, 1000, 1.055 * Math.Pow(.18, 1 / 2.4) - .055);
        CheckUniform(100, 200, 1000, 1);
        Console.WriteLine("PASS RGB10 GPU readback: black, 200/400 nit white, 1000/200 nit peak and SDR gamma/clipping");
    }

    private static double Pq(double nits)
    {
        double y = Math.Pow(nits / 10000, 2610d / 16384);
        return Math.Pow((3424d / 4096 + 2413d / 128 * y) / (1 + 2392d / 128 * y), 2523d / 32);
    }

    private void CheckUniform(float linear, float white, float peak, double expected)
    {
        int index = (int)_swapChain.CurrentBackBufferIndex;
        BeginCommands();
        Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        _commands.ClearRenderTargetView(Rtv(2), new Vortice.Mathematics.Color4(linear, linear, linear, 1));
        Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        ComposeAndPresent(white, peak);
        uint pitch = (uint)((Width * 4 + 255) & ~255);
        using var readback = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
            ResourceDescription.Buffer((ulong)pitch * (uint)Height), ResourceStates.CopyDest);
        BeginCommands();
        Transition(_backBuffers[index]!, ResourceStates.Common, ResourceStates.CopySource);
        _commands.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(Format.R10G10B10A2_UNorm, (uint)Width, (uint)Height, 1, pitch)
        }), 0, 0, 0, new TextureCopyLocation(_backBuffers[index]!, 0), null);
        Transition(_backBuffers[index]!, ResourceStates.CopySource, ResourceStates.Common);
        Submit();
        WaitForGpu();
        uint pixel = *readback.Map<uint>(0);
        readback.Unmap(0);
        for (int channel = 0; channel < 3; channel++)
        {
            double code = (pixel >> (channel * 10)) & 1023;
            if (Math.Abs(code - expected * 1023) > 2)
                throw new InvalidOperationException($"Output mismatch: HDR={_hdr}, linear={linear}, white={white}, peak={peak}, channel={channel}, code={code}, expected={expected * 1023:0.0}");
        }
    }
}
