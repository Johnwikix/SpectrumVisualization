using System;
using System.IO;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

/// <summary>Maps any linear Rec.709 scene to SDR or HDR10, optionally applying FXAA after brightness mapping.</summary>
internal sealed class ColorOutputPipelines : IDisposable
{
    internal ID3D12RootSignature Root { get; private set; } = null!;
    internal ID3D12PipelineState Output { get; private set; } = null!;
    internal ID3D12PipelineState PrepareAntialias { get; private set; } = null!;
    internal ID3D12PipelineState AntialiasOutput { get; private set; } = null!;
    internal ID3D12PipelineState PreparedOutput { get; private set; } = null!;

    internal ColorOutputPipelines(ID3D12Device device)
    {
        try
        {
            Root = device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
                [new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,0,0,0,DescriptorRangeFlags.None)]),ShaderVisibility.Pixel),
                 new RootParameter1(new RootConstants(0,0,6),ShaderVisibility.Pixel)],
                [new StaticSamplerDescription(0,Filter.MinMagMipLinear,TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureAddressMode.Clamp,
                    0,0,ComparisonFunction.Never,StaticBorderColor.TransparentBlack,0,float.MaxValue,ShaderVisibility.Pixel,0)]));
            byte[] vs = ShaderCompiler.Compile(ColorOutputShaderSource.Source,"VS","vs_5_0");
            Output = Create(device,vs,ColorOutputShaderSource.Source,"Encode",Format.R10G10B10A2_UNorm);
            PrepareAntialias = Create(device,vs,ColorOutputShaderSource.Source,"PrepareAA",Format.R16G16B16A16_Float);
            PreparedOutput = Create(device,vs,ColorOutputShaderSource.Source,"EncodePrepared",Format.R10G10B10A2_UNorm);
            string aa = ColorOutputShaderSource.Fxaa();
            AntialiasOutput = Create(device,vs,aa,"EncodeAA",Format.R10G10B10A2_UNorm);
        }
        catch { Dispose(); throw; }
    }

    private ID3D12PipelineState Create(ID3D12Device device, byte[] vs, string source, string entry, Format format) =>
        GpuPipelineCache.Create(device, new GraphicsPipelineStateDescription
        {
            RootSignature = Root,
            VertexShader = vs,
            PixelShader = ShaderCompiler.Compile(source,entry,"ps_5_0"),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [format],
            SampleDescription = new SampleDescription(1,0),
            SampleMask = uint.MaxValue,
            BlendState = BlendDescription.Opaque,
            RasterizerState = new RasterizerDescription { CullMode = CullMode.None, FillMode = FillMode.Solid },
            DepthStencilState = new DepthStencilDescription { DepthEnable = false, StencilEnable = false }
        }, $"Output/root-v1/{entry}/{format}");

    public void Dispose()
    {
        GpuGraphics.DisposeResource(AntialiasOutput);
        GpuGraphics.DisposeResource(PreparedOutput);
        GpuGraphics.DisposeResource(PrepareAntialias);
        GpuGraphics.DisposeResource(Output);
        GpuGraphics.DisposeResource(Root);
    }
}
