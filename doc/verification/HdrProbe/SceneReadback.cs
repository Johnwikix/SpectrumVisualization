using ComputeSharp;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    internal void SeedSimulationForProbe() => ((WinExSpectrumTest.Effects.Sonic.SonicGpuEffect)_effect).SeedForProbe();

    internal float4[] ReadLinearScene(bool reconstructed = false)
    {
        int width = reconstructed ? Width : _size.Width;
        int height = reconstructed ? Height : _size.Height;
        var source = reconstructed ? _reconstructed! : _scene!;
        uint pitch = (uint)((width * 8 + 255) & ~255);
        using var readback = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
            ResourceDescription.Buffer((ulong)pitch * (uint)height), ResourceStates.CopyDest);
        BeginCommands();
        Transition(source, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
        _commands.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint
        {
            Offset = 0,
            Footprint = new SubresourceFootPrint(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, pitch)
        }), 0, 0, 0, new TextureCopyLocation(source, 0), null);
        Transition(source, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
        Submit();
        WaitForGpu();
        var pixels = new float4[width * height];
        byte* address = readback.Map<byte>(0);
        for (int y = 0; y < height; y++)
        {
            Half* row = (Half*)(address + y * pitch);
            for (int x = 0; x < width; x++)
                pixels[y * width + x] = new((float)row[x * 4], (float)row[x * 4 + 1], (float)row[x * 4 + 2], (float)row[x * 4 + 3]);
        }
        readback.Unmap(0);
        return pixels;
    }
}
