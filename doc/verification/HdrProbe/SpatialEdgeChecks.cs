using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    internal void VerifySpatialEdges()
    {
        const string shader = """
            void VS(uint id : SV_VertexID, out float4 p : SV_Position) {
                p=float4(float2((id<<1)&2,id&2)*2-1,0,1);
            }
            float4 PS(float4 p : SV_Position) : SV_Target {
                return float4((p.y > p.x*.37+50 ? 1 : 0).xxx,1);
            }
            """;
        using var root = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None));
        using var pso = _device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            RootSignature = root, VertexShader = ShaderCompiler.Compile(shader, "VS", "vs_5_0"),
            PixelShader = ShaderCompiler.Compile(shader, "PS", "ps_5_0"),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle, RenderTargetFormats = [Format.R16G16B16A16_Float],
            SampleDescription = new(1, 0), SampleMask = uint.MaxValue, BlendState = BlendDescription.Opaque,
            RasterizerState = new() { CullMode = CullMode.None, FillMode = FillMode.Solid },
            DepthStencilState = new() { DepthEnable = false, StencilEnable = false }
        });
        SetHdr(false);
        foreach (var mode in new[] { ReconstructionMode.Off, ReconstructionMode.Fxaa, ReconstructionMode.Smaa })
        {
            Configure(new(100, mode, 80));
            int index = (int)_swapChain.CurrentBackBufferIndex;
            BeginCommands();
            Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            _commands.SetGraphicsRootSignature(root);
            _commands.SetPipelineState(pso);
            _commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _commands.RSSetViewport(0, 0, Width, Height, 0, 1);
            _commands.RSSetScissorRect(Width, Height);
            _commands.OMSetRenderTargets(Rtv(2), null);
            _commands.DrawInstanced(3, 1, 0, 0);
            Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            ComposeAndPresent(200, 1000);
            uint pitch = (uint)((Width * 4 + 255) & ~255);
            using var readback = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
                ResourceDescription.Buffer((ulong)pitch * (uint)Height), ResourceStates.CopyDest);
            BeginCommands();
            Transition(_backBuffers[index]!, ResourceStates.Common, ResourceStates.CopySource);
            _commands.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint
            {
                Footprint = new SubresourceFootPrint(Format.R10G10B10A2_UNorm, (uint)Width, (uint)Height, 1, pitch)
            }), 0, 0, 0, new TextureCopyLocation(_backBuffers[index]!, 0), null);
            Transition(_backBuffers[index]!, ResourceStates.CopySource, ResourceStates.Common);
            Submit(); WaitForGpu();
            byte* data = readback.Map<byte>(0);
            int partial = 0;
            for (int y = 2; y < Height - 2; y++)
            for (int x = 2; x < Width - 2; x++)
            {
                uint channel = ((uint*)(data + y * pitch))[x] & 1023;
                if (channel > 10 && channel < 1013) partial++;
            }
            readback.Unmap(0);
            if (mode == ReconstructionMode.Off ? partial != 0 : partial < Width / 4)
                throw new InvalidOperationException($"Spatial edge test failed: {mode}, partial pixels={partial}");
            Console.WriteLine($"PASS diagonal edge mode={mode}, antialiased pixels={partial}");
        }
    }
}
