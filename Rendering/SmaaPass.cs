using System;
using System.Buffers.Binary;
using System.IO;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

/// <summary>SMAA 1x HIGH using the original reference shader and packed lookup textures.</summary>
internal sealed unsafe class SmaaPass : IDisposable
{
    private readonly ID3D12Resource?[] _textures = new ID3D12Resource?[5];
    private readonly ID3D12Resource?[] _uploads = new ID3D12Resource?[2];
    private readonly ID3D12PipelineState?[] _passes = new ID3D12PipelineState?[3];
    private ID3D12RootSignature _root = null!;
    private ID3D12DescriptorHeap _srv = null!;
    private ID3D12DescriptorHeap _rtv = null!;
    private int _stride;
    private int _srvStride;
    private readonly int _width;
    private readonly int _height;
    internal ID3D12Resource Output => _textures[2]!;

    // Source color is perceptual, brightness-mapped FP16. Encoding remains the host's responsibility.
    private const string Prefix = """
        #define SMAA_HLSL_4 1
        // HIGH preset with brightness-relative edge sensitivity for HDR.
        #define SMAA_MAX_SEARCH_STEPS 16
        #define SMAA_MAX_SEARCH_STEPS_DIAG 8
        #define SMAA_CORNER_ROUNDING 25
        #define SMAA_THRESHOLD (.1 * edgeScale)
        cbuffer Settings : register(b0) { float4 metrics; float edgeScale; };
        #define SMAA_RT_METRICS metrics
        """;
    private const string Suffix = """
        Texture2D colorTex : register(t0);
        Texture2D edgesTex : register(t1);
        Texture2D blendTex : register(t2);
        Texture2D areaTex : register(t3);
        Texture2D searchTex : register(t4);
        void VS(uint id : SV_VertexID, out float4 position : SV_Position, out float2 uv : TEXCOORD0) {
            float2 xy = float2((id << 1) & 2, id & 2);
            position = float4(xy * 2 - 1, 0, 1); uv = float2(xy.x, 1 - xy.y);
        }
        float2 Edges(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float4 offsets[3]; SMAAEdgeDetectionVS(uv,offsets);
            return SMAAColorEdgeDetectionPS(uv,offsets,colorTex);
        }
        float4 Weights(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float2 pixel; float4 offsets[3]; SMAABlendingWeightCalculationVS(uv,pixel,offsets);
            return SMAABlendingWeightCalculationPS(uv,pixel,offsets,edgesTex,areaTex,searchTex,0);
        }
        float4 Blend(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float4 offset; SMAANeighborhoodBlendingVS(uv,offset);
            return SMAANeighborhoodBlendingPS(uv,offset,colorTex,blendTex);
        }
        """;

    /// <summary>Records lookup uploads. The caller must drain this list before releasing uploads or disposing.</summary>
    internal SmaaPass(ID3D12Device device, ID3D12GraphicsCommandList commands, ID3D12Resource source, int width, int height)
    {
        _width = width;
        _height = height;
        try
        {
            _srv = device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 15, DescriptorHeapFlags.ShaderVisible));
            _rtv = device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 3));
            _stride = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            _srvStride = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            _root = device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
                [new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 5, 0, 0, 0, DescriptorRangeFlags.None)]), ShaderVisibility.Pixel),
                 new RootParameter1(new RootConstants(0, 0, 5), ShaderVisibility.Pixel)],
                [Sampler(0, Filter.MinMagMipLinear), Sampler(1, Filter.MinMagMipPoint)]));
            using var stream = Resource("SMAA.hlsl");
            using var reader = new StreamReader(stream);
            string shader = Prefix + "\n" + reader.ReadToEnd() + "\n" + Suffix;
            var vs = ShaderCompiler.Compile(shader, "VS", "vs_5_0");
            Format[] formats = [Format.R8G8_UNorm, Format.R8G8B8A8_UNorm, Format.R16G16B16A16_Float];
            string[] entries = ["Edges", "Weights", "Blend"];
            for (int i = 0; i < 3; i++)
            {
                _textures[i] = device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                    ResourceDescription.Texture2D(formats[i], (uint)width, (uint)height, 1, 1, 1, 0, ResourceFlags.AllowRenderTarget), ResourceStates.PixelShaderResource);
                device.CreateRenderTargetView(_textures[i]!, null, _rtv.GetCPUDescriptorHandleForHeapStart() + i * _stride);
                _passes[i] = device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
                {
                    RootSignature = _root, VertexShader = vs, PixelShader = ShaderCompiler.Compile(shader, entries[i], "ps_5_0"),
                    PrimitiveTopologyType = PrimitiveTopologyType.Triangle, RenderTargetFormats = [formats[i]],
                    SampleDescription = new(1, 0), SampleMask = uint.MaxValue, BlendState = BlendDescription.Opaque,
                    RasterizerState = new() { CullMode = CullMode.None, FillMode = FillMode.Solid },
                    DepthStencilState = new() { DepthEnable = false, StencilEnable = false }
                });
            }
            UploadLookup(device, commands, 0, "AreaTexDX10.dds", 160, 560, 3, Format.R8G8_UNorm);
            UploadLookup(device, commands, 1, "SearchTex.dds", 64, 16, 1, Format.R8_UNorm);
            for (int pass = 0; pass < 3; pass++)
            for (int i = 0; i < 5; i++)
            {
                // Never bind the current render target as an SRV, including unused shader registers.
                var texture = i >= 3 ? _textures[i]! : i == 1 && pass >= 1 ? _textures[0]! : i == 2 && pass == 2 ? _textures[1]! : source;
                device.CreateShaderResourceView(texture, null, _srv.GetCPUDescriptorHandleForHeapStart() + (pass * 5 + i) * _srvStride);
            }
        }
        catch { Dispose(); throw; }
    }

    private static StaticSamplerDescription Sampler(uint register, Filter filter) => new(register, filter,
        TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp, 0, 0, ComparisonFunction.Never,
        StaticBorderColor.TransparentBlack, 0, float.MaxValue, ShaderVisibility.Pixel, 0);

    private static Stream Resource(string name) => typeof(SmaaPass).Assembly.GetManifestResourceStream("Spectrum.SMAA." + name)
        ?? throw new InvalidOperationException("Missing SMAA resource: " + name);

    private void UploadLookup(ID3D12Device device, ID3D12GraphicsCommandList commands, int index, string name, int width, int height, int sourceBytes, Format format)
    {
        using var stream = Resource(name);
        byte[] dds = new byte[checked((int)stream.Length)];
        stream.ReadExactly(dds);
        if (dds.Length != 128 + width * height * sourceBytes || BinaryPrimitives.ReadUInt32LittleEndian(dds) != 0x20534444
            || BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12)) != height || BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16)) != width
            || BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(88)) != sourceBytes * 8)
            throw new InvalidDataException("Unexpected SMAA lookup format: " + name);
        int bytes = index == 0 ? 2 : 1;
        uint pitch = (uint)((width * bytes + 255) & ~255);
        var upload = _uploads[index] = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(pitch * (ulong)height), ResourceStates.GenericRead);
        byte* mapped = upload.Map<byte>(0);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int src = 128 + (y * width + x) * sourceBytes;
            mapped[y * pitch + x * bytes] = dds[src + (index == 0 ? 2 : 0)];
            if (index == 0) mapped[y * pitch + x * bytes + 1] = dds[src + 1];
        }
        upload.Unmap(0);
        var texture = _textures[index + 3] = device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(format, (uint)width, (uint)height, 1, 1), ResourceStates.CopyDest);
        commands.CopyTextureRegion(new TextureCopyLocation(texture, 0), 0, 0, 0, new TextureCopyLocation(upload,
            new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(format, (uint)width, (uint)height, 1, pitch) }), null);
        commands.ResourceBarrierTransition(texture, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
    }

    internal void ReleaseUploads()
    {
        for (int i = 0; i < _uploads.Length; i++) { GpuGraphics.DisposeResource(_uploads[i]); _uploads[i] = null; }
    }

    /// <summary>Records three full-resolution passes without managed allocations.</summary>
    internal void Record(ID3D12GraphicsCommandList commands, float edgeScale)
    {
        commands.SetDescriptorHeaps(_srv);
        commands.SetGraphicsRootSignature(_root);
        float* metrics = stackalloc float[5] { 1f / _width, 1f / _height, _width, _height, edgeScale };
        commands.SetGraphicsRoot32BitConstants(1, 5, metrics, 0);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.RSSetViewport(0, 0, _width, _height, 0, 1);
        commands.RSSetScissorRect(_width, _height);
        for (int i = 0; i < 3; i++)
        {
            var target = _rtv.GetCPUDescriptorHandleForHeapStart() + i * _stride;
            commands.ResourceBarrierTransition(_textures[i]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            commands.ClearRenderTargetView(target, default);
            commands.OMSetRenderTargets(target, null);
            commands.SetGraphicsRootDescriptorTable(0, _srv.GetGPUDescriptorHandleForHeapStart() + i * 5 * _srvStride);
            commands.SetPipelineState(_passes[i]!);
            commands.DrawInstanced(3, 1, 0, 0);
            commands.ResourceBarrierTransition(_textures[i]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
    }

    public void Dispose()
    {
        ReleaseUploads();
        foreach (var pass in _passes) GpuGraphics.DisposeResource(pass);
        foreach (var texture in _textures) GpuGraphics.DisposeResource(texture);
        GpuGraphics.DisposeResource(_root);
        GpuGraphics.DisposeResource(_srv);
        GpuGraphics.DisposeResource(_rtv);
    }
}
