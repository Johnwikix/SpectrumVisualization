using ComputeSharp;
using ComputeSharp.Interop;
using System;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Sonic;

/// <summary>Renders instanced terrain and projected particles into the host's linear scene.</summary>
internal sealed unsafe class SonicGpuEffect : IGpuVisualizerEffect
{
    private const int ParticleCapacity = (40 * 2 + 200) * 6;
    private ID3D12Device _device = null!;
    private SonicTopographyEffect _simulation = null!;
    private ID3D12RootSignature _root = null!;
    private ID3D12PipelineState _terrain = null!;
    private ID3D12PipelineState _particles = null!;
    private ID3D12DescriptorHeap _srv = null!;
    private ID3D12DescriptorHeap _dsv = null!;
    private ID3D12Resource? _depth;
    private ID3D12Resource? _field;
    private ReadWriteTexture2D<float4>? _fieldOwner;
    private ID3D12Resource _vertices = null!;
    private ParticleVertex* _mapped;
    private GpuRenderSize _size;
    private int _vertexCount;
    private bool _disposed;

    public void Initialize(in GpuEffectServices services)
    {
        _device = services.Device;
        _simulation = new SonicTopographyEffect(services.Compute, services.Analyzer);
        _srv = _device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 1, DescriptorHeapFlags.ShaderVisible));
        _dsv = _device.CreateDescriptorHeap(new(DescriptorHeapType.DepthStencilView, 1));
        _vertices = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer((ulong)(ParticleCapacity * sizeof(ParticleVertex))), ResourceStates.GenericRead);
        _mapped = _vertices.Map<ParticleVertex>(0);
        _root = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout,
            [new RootParameter1(new RootConstants(0, 0, 52), ShaderVisibility.All),
             new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 0, 0, 0, DescriptorRangeFlags.None)]), ShaderVisibility.Vertex)]));
        _terrain = CreatePipeline("TerrainVS", "TerrainPS", false);
        _particles = CreatePipeline("ParticleVS", "ParticlePS", true);
    }

    public void Resize(in GpuRenderSize size)
    {
        if (_size.Width != size.Width || _size.Height != size.Height)
        {
            var replacement = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.D32_Float, (uint)size.Width, (uint)size.Height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil),
                ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1, 0));
            _device.CreateDepthStencilView(replacement, null, _dsv.GetCPUDescriptorHandleForHeapStart());
            _depth?.Dispose();
            _depth = replacement;
        }
        _size = size;
    }

    public void PrepareFrame(double elapsedSeconds, int detail)
    {
        _simulation.Update(elapsedSeconds);
        _simulation.PrepareHeightField(detail);
        if (!ReferenceEquals(_fieldOwner, _simulation.HeightField))
        {
            Guid iid = new("696442BE-A72E-4059-BC79-5B5C98040FAD");
            void* native = null;
            InteropServices.GetID3D12Resource(_simulation.HeightField, &iid, &native);
            var replacement = new ID3D12Resource((nint)native);
            _device.CreateShaderResourceView(replacement, null, _srv.GetCPUDescriptorHandleForHeapStart());
            _field?.Dispose();
            _field = replacement;
            _fieldOwner = _simulation.HeightField;
        }
        // .NET 10: persistently mapped storage and stack constants avoid per-frame managed buffers.
        _vertexCount = _simulation.WriteParticles(new Span<ParticleVertex>(_mapped, ParticleCapacity), _size.Width, _size.Height);
    }

    public void RecordScene(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target)
    {
        commands.ResourceBarrierTransition(_field!, ResourceStates.UnorderedAccess, ResourceStates.NonPixelShaderResource);
        var depth = _dsv.GetCPUDescriptorHandleForHeapStart();
        var background = _simulation.BackgroundColor;
        commands.ClearRenderTargetView(target, new Vortice.Mathematics.Color4(background.X, background.Y, background.Z, 1));
        commands.ClearDepthStencilView(depth, ClearFlags.Depth, 1, 0);
        commands.OMSetRenderTargets(target, depth);
        commands.RSSetViewport(0, 0, _size.Width, _size.Height, 0, 1);
        commands.RSSetScissorRect(_size.Width, _size.Height);
        commands.SetDescriptorHeaps(_srv);
        commands.SetGraphicsRootSignature(_root);
        float4* constants = stackalloc float4[13];
        _simulation.WriteSceneData(new Span<float4>(constants, 13), (float)_size.OutputWidth / _size.OutputHeight);
        commands.SetGraphicsRoot32BitConstants(0, 52, constants, 0);
        commands.SetGraphicsRootDescriptorTable(1, _srv.GetGPUDescriptorHandleForHeapStart());
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.SetPipelineState(_terrain);
        uint grid = (uint)_fieldOwner!.Width;
        commands.DrawInstanced(30, grid * grid, 0, 0);
        if (_vertexCount > 0)
        {
            commands.OMSetRenderTargets(target, null);
            commands.SetPipelineState(_particles);
            var view = new VertexBufferView(_vertices.GPUVirtualAddress, (uint)(ParticleCapacity * sizeof(ParticleVertex)), (uint)sizeof(ParticleVertex));
            commands.IASetVertexBuffers(0, 1, &view);
            commands.DrawInstanced((uint)_vertexCount, 1, 0, 0);
        }
        commands.ResourceBarrierTransition(_field!, ResourceStates.NonPixelShaderResource, ResourceStates.UnorderedAccess);
    }

    private ID3D12PipelineState CreatePipeline(string vs, string ps, bool particles) => _device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
    {
        RootSignature = _root,
        VertexShader = ShaderCompiler.Compile(SonicTerrainSource.Code, vs, "vs_5_0"),
        PixelShader = ShaderCompiler.Compile(SonicTerrainSource.Code, ps, "ps_5_0"),
        InputLayout = particles ? new InputLayoutDescription([
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 8, 0)]) : default,
        PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
        RenderTargetFormats = [Format.R16G16B16A16_Float],
        DepthStencilFormat = particles ? Format.Unknown : Format.D32_Float,
        SampleDescription = new SampleDescription(1, 0),
        SampleMask = uint.MaxValue,
        BlendState = particles ? BlendDescription.AlphaBlend : BlendDescription.Opaque,
        RasterizerState = new RasterizerDescription { CullMode = particles ? CullMode.None : CullMode.Back, FrontCounterClockwise = true, FillMode = FillMode.Solid, DepthClipEnable = true },
        DepthStencilState = new DepthStencilDescription { DepthEnable = !particles, DepthWriteMask = DepthWriteMask.All, DepthFunc = ComparisonFunction.Less, StencilEnable = false }
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GpuGraphics.DisposeResource(_field);
        GpuGraphics.DisposeResource(_depth);
        GpuGraphics.DisposeResource(_vertices);
        GpuGraphics.DisposeResource(_terrain);
        GpuGraphics.DisposeResource(_particles);
        GpuGraphics.DisposeResource(_root);
        GpuGraphics.DisposeResource(_srv);
        GpuGraphics.DisposeResource(_dsv);
        GpuGraphics.DisposeResource(_simulation);
    }
}
