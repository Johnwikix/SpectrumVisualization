using System;
using Vortice.Mathematics;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original six-level graphics / five-level text bloom. All descriptors and targets are size-owned;
/// the preceding-frame fence belongs to GpuGraphics, and no resources are created while recording warm frames.</summary>
internal sealed unsafe class FoliaBloom : IDisposable
{
    private readonly ID3D12Device _device;
    private readonly ID3D12RootSignature _root;
    private readonly ID3D12PipelineState _down, _up, _combine;
    private readonly ID3D12DescriptorHeap _srv, _rtv;
    private readonly int _srvStride, _rtvStride;
    private readonly ID3D12Resource?[] _textures = new ID3D12Resource?[12];
    private readonly int[] _widths = new int[12], _heights = new int[12];
    private int _width, _height;
    private bool _disposed;

    internal FoliaBloom(ID3D12Device device)
    {
        _device = device;
        _srvStride = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        _rtvStride = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        try
        {
            _srv = device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 13, DescriptorHeapFlags.ShaderVisible));
            _rtv = device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 12));
            _root = device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
                [new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,0)]),ShaderVisibility.Pixel),
                 new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,1)]),ShaderVisibility.Pixel),
                 new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,2)]),ShaderVisibility.Pixel),
                 new RootParameter1(new RootConstants(0,0,8),ShaderVisibility.Pixel)],
                [new StaticSamplerDescription(0, Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
                    0, 0, ComparisonFunction.Never, StaticBorderColor.TransparentBlack, 0, float.MaxValue, ShaderVisibility.Pixel, 0)]));
            string source = FoliaShaderLibrary.Load("Bloom");
            var vertex = ShaderCompiler.Compile(source, "VS", "vs_5_0");
            _down = Pipeline(source, vertex, "Down");
            _up = Pipeline(source, vertex, "Up");
            _combine = Pipeline(source, vertex, "Combine");
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    private ID3D12PipelineState Pipeline(string source, ReadOnlyMemory<byte> vertex, string entry) => _device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
    {
        RootSignature = _root,
        VertexShader = vertex,
        PixelShader = ShaderCompiler.Compile(source, entry, "ps_5_0"),
        PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
        RenderTargetFormats = [Format.R16G16B16A16_Float],
        SampleDescription = new(1, 0),
        SampleMask = uint.MaxValue,
        BlendState = BlendDescription.Opaque,
        RasterizerState = new() { CullMode = CullMode.None, FillMode = FillMode.Solid },
        DepthStencilState = new() { DepthEnable = false, StencilEnable = false }
    });
    internal void Resize(int width, int height, ID3D12Resource backdrop)
    {
        _width = width;
        _height = height;
        for (int i = 0; i < _textures.Length; i++)
        {
            GpuGraphics.DisposeResource(_textures[i]);
            int level = i == 0 ? 0 : i <= 6 ? i : i - 6;
            _widths[i] = Math.Max(1, width >> level);
            _heights[i] = Math.Max(1, height >> level);
            _textures[i] = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.R16G16B16A16_Float, (uint)_widths[i], (uint)_heights[i], 1, 1, 1, 0, ResourceFlags.AllowRenderTarget), ResourceStates.PixelShaderResource);
            _device.CreateRenderTargetView(_textures[i]!, null, Rtv(i));
            _device.CreateShaderResourceView(_textures[i]!, null, _srv.GetCPUDescriptorHandleForHeapStart() + i * _srvStride);
        }
        _device.CreateShaderResourceView(backdrop, null, _srv.GetCPUDescriptorHandleForHeapStart() + 12 * _srvStride);
    }
    private CpuDescriptorHandle Rtv(int i) => _rtv.GetCPUDescriptorHandleForHeapStart() + i * _rtvStride;
    internal CpuDescriptorHandle Begin(ID3D12GraphicsCommandList commands)
    {
        commands.ResourceBarrierTransition(_textures[0]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.ClearRenderTargetView(Rtv(0), new Color4(0, 0, 0, 0));
        return Rtv(0);
    }
    private void Bind(ID3D12GraphicsCommandList commands, int first, int second)
    {
        commands.SetGraphicsRootDescriptorTable(0, _srv.GetGPUDescriptorHandleForHeapStart() + first * _srvStride);
        commands.SetGraphicsRootDescriptorTable(1, _srv.GetGPUDescriptorHandleForHeapStart() + second * _srvStride);
        commands.SetGraphicsRootDescriptorTable(2, _srv.GetGPUDescriptorHandleForHeapStart() + 12 * _srvStride);
    }
    private void Constants(ID3D12GraphicsCommandList commands, int input, float threshold, float knee, float prefilter, float spread, float strength, float background)
    {
        float* values = stackalloc float[8] { 1f / _widths[input], 1f / _heights[input], threshold, knee, prefilter, spread, strength, background };
        commands.SetGraphicsRoot32BitConstants(3, 8, values, 0);
    }
    private void Draw(ID3D12GraphicsCommandList commands, int target)
    {
        commands.ResourceBarrierTransition(_textures[target]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(Rtv(target));
        commands.RSSetViewport(0, 0, _widths[target], _heights[target], 0, 1);
        commands.RSSetScissorRect(_widths[target], _heights[target]);
        commands.DrawInstanced(3, 1, 0, 0);
        commands.ResourceBarrierTransition(_textures[target]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
    }
    internal void Apply(ID3D12GraphicsCommandList commands, ID3D12Resource output, CpuDescriptorHandle target, bool text)
    {
        commands.ResourceBarrierTransition(_textures[0]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        commands.SetDescriptorHeaps(_srv);
        commands.SetGraphicsRootSignature(_root);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        float threshold = text ? .12f : .42f, knee = text ? .2f : .3f, spread = text ? .95f : .9f;
        int count = text ? 5 : 6;
        while (count > 1 && Math.Min(_width >> count, _height >> count) < 2)
            count--;
        int source = 0;
        commands.SetPipelineState(_down);
        for (int i = 0; i < count; i++)
        {
            Bind(commands, source, source);
            Constants(commands, source, threshold, knee, i == 0 ? 1 : 0, spread, 0, 0);
            Draw(commands, i + 1);
            source = i + 1;
        }
        commands.SetPipelineState(_up);
        for (int i = count - 2; i >= 0; i--)
        {
            Bind(commands, source, i + 1);
            Constants(commands, source, threshold, knee, 0, spread, 0, 0);
            Draw(commands, 7 + i);
            source = 7 + i;
        }
        Bind(commands, 0, source);
        Constants(commands, 0, threshold, knee, 0, spread, text ? 1.15f : 1.5f, text ? 1 : .25f);
        commands.SetPipelineState(_combine);
        commands.ResourceBarrierTransition(output, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(target);
        commands.RSSetViewport(0, 0, _width, _height, 0, 1);
        commands.RSSetScissorRect(_width, _height);
        commands.DrawInstanced(3, 1, 0, 0);
        commands.ResourceBarrierTransition(output, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var texture in _textures)
            GpuGraphics.DisposeResource(texture);
        GpuGraphics.DisposeResource(_down);
        GpuGraphics.DisposeResource(_up);
        GpuGraphics.DisposeResource(_combine);
        GpuGraphics.DisposeResource(_root);
        GpuGraphics.DisposeResource(_srv);
        GpuGraphics.DisposeResource(_rtv);
    }
}

