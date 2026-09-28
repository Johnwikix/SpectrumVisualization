using ComputeSharp;
using ComputeSharp.Interop;
using System;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Sonic;

/// <summary>Renders instanced terrain and projected particles into the host's linear scene.</summary>
internal sealed unsafe partial class SonicGpuEffect : IGpuVisualizerEffect, ITemporalGpuEffect
{
    private const int ParticleCapacity = (40 * 2 + 200) * 6;
    private const float NearPlane = .5f;
    private const float FarPlane = 1000;
    private ID3D12Device _device = null!;
    private SonicTopographyEffect _simulation = null!;
    private ID3D12RootSignature _root = null!;
    private ID3D12RootSignature _particleRoot = null!;
    private ID3D12PipelineState _terrain = null!;
    private ID3D12PipelineState _particles = null!;
    private ID3D12PipelineState? _temporalTerrain;
    private ID3D12Resource _constants = null!;
    private float4* _constantData;
    private readonly float4[] _previousData = new float4[13];
    private ID3D12Resource? _previousField;
    private ID3D12Resource? _motion;
    private ID3D12Resource? _reactive;
    private ID3D12DescriptorHeap _temporalRtv = null!;
    private int _rtvStride;
    private bool _temporal;
    private bool _depthReadable;
    private bool _copyHistory;
    private bool _updateHistoryDescriptor = true;
    private TemporalFrame _frame;
    public TemporalCamera Camera => new(NearPlane, FarPlane, SonicTopographyEffect.FieldOfViewY * (MathF.PI / 180));
    public ID3D12Resource Depth => _depth!;
    public ID3D12Resource Motion => _motion!;
    public ID3D12Resource Reactive => _reactive!;
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
        _srv = _device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 2, DescriptorHeapFlags.ShaderVisible));
        _dsv = _device.CreateDescriptorHeap(new(DescriptorHeapType.DepthStencilView, 1));
        _temporalRtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 2));
        _rtvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _constants = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(512), ResourceStates.GenericRead);
        _constantData = _constants.Map<float4>(0);
        _vertices = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer((ulong)(ParticleCapacity * sizeof(ParticleVertex))), ResourceStates.GenericRead);
        _mapped = _vertices.Map<ParticleVertex>(0);
        _root = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout,
            [new RootParameter1(RootParameterType.ConstantBufferView, new RootDescriptor1(0, 0), ShaderVisibility.All),
             new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 2, 0, 0, 0, DescriptorRangeFlags.None)]), ShaderVisibility.Vertex)]));
        _terrain = CreatePipeline("TerrainVS", "TerrainPS", false);
        _particleRoot = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.AllowInputAssemblerInputLayout));
        _particles = CreatePipeline("ParticleVS", "ParticlePS", true);
    }

    public void Resize(in GpuRenderSize size)
    {
        if (_size.Width != size.Width || _size.Height != size.Height || (_motion != null) != _temporal)
        {
            var replacement = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.D32_Float, (uint)size.Width, (uint)size.Height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil),
                ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1, 0));
            _device.CreateDepthStencilView(replacement, null, _dsv.GetCPUDescriptorHandleForHeapStart());
            _depth?.Dispose();
            _depth = replacement;
            _depthReadable = false;
            GpuGraphics.DisposeResource(_motion);
            GpuGraphics.DisposeResource(_reactive);
            _motion = null;
            _reactive = null;
            if (_temporal)
            {
                _motion = CreateTemporalTexture(Format.R16G16_Float, size, 0);
                _reactive = CreateTemporalTexture(Format.R8_UNorm, size, 1);
            }
        }
        _size = size;
    }

    public void ConfigureTemporal(bool enabled)
    {
        if (enabled && _temporalTerrain == null) _temporalTerrain = CreatePipeline("TerrainVS", "TerrainTemporalPS", false, true);
        if (_temporal != enabled)
        {
            GpuGraphics.DisposeResource(_previousField);
            _previousField = null;
            _copyHistory = true;
            _updateHistoryDescriptor = true;
        }
        _temporal = enabled;
    }

    public void SetTemporalFrame(in TemporalFrame frame) => _frame = frame;

    private ID3D12Resource CreateTemporalTexture(Format format, in GpuRenderSize size, int slot)
    {
        var texture = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(format, (uint)size.Width, (uint)size.Height, 1, 1, 1, 0, ResourceFlags.AllowRenderTarget), ResourceStates.NonPixelShaderResource);
        _device.CreateRenderTargetView(texture, null, _temporalRtv.GetCPUDescriptorHandleForHeapStart() + slot * _rtvStride);
        return texture;
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
            GpuGraphics.DisposeResource(_previousField);
            _previousField = null;
            _updateHistoryDescriptor = true;
        }
        if (_temporal && _previousField == null)
        {
            _previousField = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.R32G32B32A32_Float, (uint)detail, (uint)detail, 1, 1), ResourceStates.NonPixelShaderResource);
            _copyHistory = true;
        }
        if (_updateHistoryDescriptor)
        {
            int stride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            _device.CreateShaderResourceView(_temporal ? _previousField! : _field!, null, _srv.GetCPUDescriptorHandleForHeapStart() + stride);
            _updateHistoryDescriptor = false;
        }
        // .NET 10: persistently mapped storage and stack constants avoid per-frame managed buffers.
        _vertexCount = _simulation.WriteParticles(new Span<ParticleVertex>(_mapped, ParticleCapacity),
            _temporal ? _size.OutputWidth : _size.Width, _temporal ? _size.OutputHeight : _size.Height);
        var current = new Span<float4>(_constantData, 13);
        _simulation.WriteSceneData(current, (float)_size.OutputWidth / _size.OutputHeight);
        if (_frame.Reset || _copyHistory) current.CopyTo(_previousData);
        _previousData.CopyTo(new Span<float4>(_constantData + 13, 13));
        float reactive = 0;
        for (int i = 4; i < 13; i++)
        {
            float4 delta = current[i] - _previousData[i];
            reactive = Math.Max(reactive, Math.Max(Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)), Math.Max(Math.Abs(delta.Z), Math.Abs(delta.W))));
        }
        _constantData[26] = new float4(_frame.JitterX, _frame.JitterY, 0, 0);
        _constantData[27] = new float4(_size.Width, _size.Height, Math.Min(1, reactive * 2), _temporal ? 1 : 0);
        _constantData[28] = new float4(NearPlane, FarPlane, 0, 0);
        current.CopyTo(_previousData);
    }

    public void RecordScene(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target)
    {
        if (_temporal && (_copyHistory || _frame.Reset)) CopyHistory(commands, ResourceStates.UnorderedAccess);
        commands.ResourceBarrierTransition(_field!, ResourceStates.UnorderedAccess, ResourceStates.NonPixelShaderResource);
        if (_depthReadable) commands.ResourceBarrierTransition(_depth!, ResourceStates.NonPixelShaderResource, ResourceStates.DepthWrite);
        var depth = _dsv.GetCPUDescriptorHandleForHeapStart();
        var background = _simulation.BackgroundColor;
        commands.ClearRenderTargetView(target, new Vortice.Mathematics.Color4(background.X, background.Y, background.Z, 1));
        commands.ClearDepthStencilView(depth, ClearFlags.Depth, 1, 0);
        commands.OMSetRenderTargets(target, depth);
        if (_temporal)
        {
            var motion = _temporalRtv.GetCPUDescriptorHandleForHeapStart();
            var reactive = motion + _rtvStride;
            commands.ResourceBarrierTransition(_motion!, ResourceStates.NonPixelShaderResource, ResourceStates.RenderTarget);
            commands.ResourceBarrierTransition(_reactive!, ResourceStates.NonPixelShaderResource, ResourceStates.RenderTarget);
            commands.ClearRenderTargetView(motion, default);
            commands.ClearRenderTargetView(reactive, default);
            ReadOnlySpan<CpuDescriptorHandle> targets = stackalloc CpuDescriptorHandle[3] { target, motion, reactive };
            commands.OMSetRenderTargets(3, targets, false, depth);
        }
        commands.RSSetViewport(0, 0, _size.Width, _size.Height, 0, 1);
        commands.RSSetScissorRect(_size.Width, _size.Height);
        commands.SetDescriptorHeaps(_srv);
        commands.SetGraphicsRootSignature(_root);
        commands.SetGraphicsRootConstantBufferView(0, _constants.GPUVirtualAddress);
        commands.SetGraphicsRootDescriptorTable(1, _srv.GetGPUDescriptorHandleForHeapStart());
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.SetPipelineState(_temporal ? _temporalTerrain! : _terrain);
        uint grid = (uint)_fieldOwner!.Width;
        commands.DrawInstanced(30, grid * grid, 0, 0);
        if (!_temporal) RecordOverlay(commands, target);
        else
        {
            commands.ResourceBarrierTransition(_depth!, ResourceStates.DepthWrite, ResourceStates.NonPixelShaderResource);
            commands.ResourceBarrierTransition(_motion!, ResourceStates.RenderTarget, ResourceStates.NonPixelShaderResource);
            commands.ResourceBarrierTransition(_reactive!, ResourceStates.RenderTarget, ResourceStates.NonPixelShaderResource);
            CopyHistory(commands, ResourceStates.NonPixelShaderResource);
        }
        _depthReadable = _temporal;
        commands.ResourceBarrierTransition(_field!, ResourceStates.NonPixelShaderResource, ResourceStates.UnorderedAccess);
    }

    private void CopyHistory(ID3D12GraphicsCommandList commands, ResourceStates fieldState)
    {
        commands.ResourceBarrierTransition(_field!, fieldState, ResourceStates.CopySource);
        commands.ResourceBarrierTransition(_previousField!, ResourceStates.NonPixelShaderResource, ResourceStates.CopyDest);
        commands.CopyResource(_previousField!, _field!);
        commands.ResourceBarrierTransition(_previousField!, ResourceStates.CopyDest, ResourceStates.NonPixelShaderResource);
        commands.ResourceBarrierTransition(_field!, ResourceStates.CopySource, fieldState);
        _copyHistory = false;
    }

    public void RecordOverlay(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target)
    {
        if (_vertexCount == 0) return;
        commands.SetGraphicsRootSignature(_particleRoot);
        commands.RSSetViewport(0, 0, _temporal ? _size.OutputWidth : _size.Width, _temporal ? _size.OutputHeight : _size.Height, 0, 1);
        commands.RSSetScissorRect(_temporal ? _size.OutputWidth : _size.Width, _temporal ? _size.OutputHeight : _size.Height);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.OMSetRenderTargets(target, null);
        commands.SetPipelineState(_particles);
        var view = new VertexBufferView(_vertices.GPUVirtualAddress, (uint)(ParticleCapacity * sizeof(ParticleVertex)), (uint)sizeof(ParticleVertex));
        commands.IASetVertexBuffers(0, 1, &view);
        commands.DrawInstanced((uint)_vertexCount, 1, 0, 0);
    }

    private ID3D12PipelineState CreatePipeline(string vs, string ps, bool particles, bool temporal = false) => _device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
    {
        RootSignature = particles ? _particleRoot : _root,
        VertexShader = ShaderCompiler.Compile(SonicTerrainSource.Code, vs, "vs_5_0"),
        PixelShader = ShaderCompiler.Compile(SonicTerrainSource.Code, ps, "ps_5_0"),
        InputLayout = particles ? new InputLayoutDescription([
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
            new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 8, 0)]) : default,
        PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
        RenderTargetFormats = temporal ? [Format.R16G16B16A16_Float, Format.R16G16_Float, Format.R8_UNorm] : [Format.R16G16B16A16_Float],
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
        GpuGraphics.DisposeResource(_previousField);
        GpuGraphics.DisposeResource(_motion);
        GpuGraphics.DisposeResource(_reactive);
        GpuGraphics.DisposeResource(_constants);
        GpuGraphics.DisposeResource(_temporalTerrain);
        GpuGraphics.DisposeResource(_temporalRtv);
        GpuGraphics.DisposeResource(_depth);
        GpuGraphics.DisposeResource(_vertices);
        GpuGraphics.DisposeResource(_terrain);
        GpuGraphics.DisposeResource(_particles);
        GpuGraphics.DisposeResource(_root);
        GpuGraphics.DisposeResource(_particleRoot);
        GpuGraphics.DisposeResource(_srv);
        GpuGraphics.DisposeResource(_dsv);
        GpuGraphics.DisposeResource(_simulation);
    }
}
