using ComputeSharp;
using ComputeSharp.Interop;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;

namespace WinExSpectrumTest.Rendering;

/// <summary>
/// All methods run on one render thread. Compute completes before the direct queue reads its
/// texture; the direct fence completes before that texture or the upload buffer is reused.
/// </summary>
internal sealed unsafe partial class SonicGraphics : IDisposable
{
    private const Format OutputFormat = Format.R10G10B10A2_UNorm;
    private const int ParticleCapacity = (40 * 2 + 200) * 6;
    private readonly Action<nint> _bind;
    private GraphicsDevice _compute = null!;
    private ID3D12Device _device = null!;
    private ID3D12CommandQueue _queue = null!;
    private ID3D12CommandAllocator _allocator = null!;
    private ID3D12GraphicsCommandList _commands = null!;
    private readonly ID3D12CommandList[] _submission = new ID3D12CommandList[1];
    private ID3D12Fence _fence = null!;
    private readonly AutoResetEvent _gpuDone = new(false);
    private ulong _fenceValue;
    private IDXGISwapChain3 _swapChain = null!;
    private IDXGIFactory6 _factory = null!;
    private ID3D12DescriptorHeap _rtvs = null!;
    private ID3D12DescriptorHeap _srvs = null!;
    private int _rtvStride;
    private int _srvStride;
    private readonly ID3D12Resource?[] _backBuffers = new ID3D12Resource?[2];
    private ReadWriteTexture2D<float4>? _terrain;
    private ID3D12Resource? _terrainNative;
    private ID3D12Resource? _scene;
    private ID3D12Resource _vertices = null!;
    private ParticleVertex* _mappedVertices;
    private SonicPipelines _pipelines = null!;
    private SonicTopographyEffect _effect = null!;
    private bool _attached;
    private bool _hdr;
    private bool _disposed;
    public int Width { get; private set; }
    public int Height { get; private set; }

    public SonicGraphics(SpectrumAnalyzer analyzer, Action<nint> bind, int width, int height)
    {
        _bind = bind;
        try
        {
            _compute = GraphicsDevice.GetDefault();
            Guid deviceId = new("189819F1-1DB6-4B57-BE54-1821339B85F7");
            void* native = null;
            InteropServices.GetID3D12Device(_compute, &deviceId, &native);
            _device = new ID3D12Device((nint)native);
            _queue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
            _allocator = _device.CreateCommandAllocator(CommandListType.Direct);
            _commands = _device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _allocator, null);
            _commands.Close();
            _submission[0] = _commands;
            _fence = _device.CreateFence(0, FenceFlags.None);
            _factory = DXGI.CreateDXGIFactory2<IDXGIFactory6>(false);
            _rtvs = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, 3));
            _srvs = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 2, DescriptorHeapFlags.ShaderVisible));
            _rtvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            _srvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            _vertices = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
                ResourceDescription.Buffer((ulong)(ParticleCapacity * sizeof(ParticleVertex))), ResourceStates.GenericRead);
            _mappedVertices = _vertices.Map<ParticleVertex>(0);
            _pipelines = new SonicPipelines(_device);
            _effect = new SonicTopographyEffect(_compute, analyzer);
            using var chain = _factory.CreateSwapChainForComposition(_queue,
                new SwapChainDescription1((uint)width, (uint)height, OutputFormat, false, Usage.RenderTargetOutput,
                    2, Scaling.Stretch, SwapEffect.FlipSequential, AlphaMode.Ignore, SwapChainFlags.None), null);
            _swapChain = chain.QueryInterface<IDXGISwapChain3>();
            CreateSizeResources(width, height);
            // Establish a live SDR chain with black content before setting a color space.
            ClearAndPresent();
            _bind(_swapChain.NativePointer);
            _attached = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height) return;
        WaitForGpu();
        ReleaseSizeResources();
        // Release every application reference before ResizeBuffers; each resize is followed by a present.
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, OutputFormat, SwapChainFlags.None).CheckError();
        var actual = _swapChain.Description1;
        if (actual.Width != width || actual.Height != height)
            throw new InvalidOperationException("The swap chain did not apply its requested size.");
        CreateSizeResources(width, height);
        _swapChain.SetColorSpace1(_hdr ? ColorSpaceType.RgbFullG2084NoneP2020 : ColorSpaceType.RgbFullG22NoneP709);
        ClearAndPresent();
    }

    private void CreateSizeResources(int width, int height)
    {
        Width = width;
        Height = height;
        for (int i = 0; i < 2; i++)
        {
            _backBuffers[i] = _swapChain.GetBuffer<ID3D12Resource>((uint)i);
            _device.CreateRenderTargetView(_backBuffers[i]!, null, Rtv(i));
        }
        _terrain = _compute.AllocateReadWriteTexture2D<float4>(width, height);
        Guid resourceId = new("696442BE-A72E-4059-BC79-5B5C98040FAD");
        void* native = null;
        InteropServices.GetID3D12Resource(_terrain, &resourceId, &native);
        _terrainNative = new ID3D12Resource((nint)native);
        _device.CreateShaderResourceView(_terrainNative, null, _srvs.GetCPUDescriptorHandleForHeapStart());
        _scene = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1, 1, 0, ResourceFlags.AllowRenderTarget),
            ResourceStates.PixelShaderResource);
        _device.CreateRenderTargetView(_scene, null, Rtv(2));
        _device.CreateShaderResourceView(_scene, null, _srvs.GetCPUDescriptorHandleForHeapStart() + _srvStride);
    }

    public bool SetHdr(bool requested)
    {
        if (_hdr == requested) return _hdr;
        try
        {
            _swapChain.SetColorSpace1(requested ? ColorSpaceType.RgbFullG2084NoneP2020 : ColorSpaceType.RgbFullG22NoneP709);
        }
        catch when (requested) { return _hdr; }
        _hdr = requested;
        return _hdr;
    }

    public DisplayOutput QueryDisplay(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out NativeRect bounds)) return default;
        if (!_factory.IsCurrent)
        {
            _factory.Dispose();
            _factory = DXGI.CreateDXGIFactory2<IDXGIFactory6>(false);
        }
        long bestArea = 0;
        DisplayOutput found = default;
        for (uint ai = 0; _factory.EnumAdapters1(ai, out var adapter).Success; ai++)
        {
            using (adapter)
            for (uint oi = 0; adapter.EnumOutputs(oi, out var output).Success; oi++)
            {
                using (output)
                using (var output6 = output.QueryInterfaceOrNull<IDXGIOutput6>())
                {
                    if (output6 == null) continue;
                    var info = output6.Description1;
                    var r = info.DesktopCoordinates;
                    long area = (long)Math.Max(0, Math.Min(bounds.Right, r.Right) - Math.Max(bounds.Left, r.Left))
                        * Math.Max(0, Math.Min(bounds.Bottom, r.Bottom) - Math.Max(bounds.Top, r.Top));
                    if (area <= bestArea) continue;
                    bestArea = area;
                    found = new DisplayOutput(info.ColorSpace == ColorSpaceType.RgbFullG2084NoneP2020, info.MaxLuminance);
                }
            }
        }
        return found;
    }

    public void Render(double elapsed, float white, float peak)
    {
        _effect.Update(elapsed);
        _effect.Render(_terrain!); // ComputeContext completes its queue before returning.
        int vertexCount = _effect.WriteParticles(new Span<ParticleVertex>(_mappedVertices, ParticleCapacity), Width, Height);
        ComposeAndPresent(vertexCount, white, peak);
    }

    private void ComposeAndPresent(int vertexCount, float white, float peak)
    {
        BeginCommands();
        Transition(_terrainNative!, ResourceStates.UnorderedAccess, ResourceStates.PixelShaderResource);
        Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        _commands.SetDescriptorHeaps(_srvs);
        _commands.SetGraphicsRootSignature(_pipelines.Root);
        _commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commands.RSSetViewport(0, 0, Width, Height, 0, 1);
        _commands.RSSetScissorRect(Width, Height);
        _commands.OMSetRenderTargets(Rtv(2), null);
        _commands.SetGraphicsRootDescriptorTable(0, _srvs.GetGPUDescriptorHandleForHeapStart());
        _commands.SetPipelineState(_pipelines.Copy);
        _commands.DrawInstanced(3, 1, 0, 0);
        if (vertexCount > 0)
        {
            _commands.SetPipelineState(_pipelines.Particles);
            var view = new VertexBufferView(_vertices.GPUVirtualAddress, (uint)(ParticleCapacity * sizeof(ParticleVertex)), (uint)sizeof(ParticleVertex));
            _commands.IASetVertexBuffers(0, 1, &view);
            _commands.DrawInstanced((uint)vertexCount, 1, 0, 0);
        }
        Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        int buffer = (int)_swapChain.CurrentBackBufferIndex;
        Transition(_backBuffers[buffer]!, ResourceStates.Common, ResourceStates.RenderTarget);
        _commands.OMSetRenderTargets(Rtv(buffer), null);
        _commands.SetGraphicsRootDescriptorTable(0, _srvs.GetGPUDescriptorHandleForHeapStart() + _srvStride);
        float* constants = stackalloc float[3] { _hdr ? 1 : 0, white, peak };
        _commands.SetGraphicsRoot32BitConstants(1, 3, constants, 0);
        _commands.SetPipelineState(_pipelines.Output);
        _commands.DrawInstanced(3, 1, 0, 0);
        Transition(_backBuffers[buffer]!, ResourceStates.RenderTarget, ResourceStates.Common);
        Transition(_terrainNative!, ResourceStates.PixelShaderResource, ResourceStates.UnorderedAccess);
        Submit();
        var result = _swapChain.Present(0, PresentFlags.DoNotWait);
        if (result.Code != (int)Vortice.DXGI.ResultCode.WasStillDrawing) result.CheckError();
        WaitForGpu();
    }

    private void ClearAndPresent()
    {
        BeginCommands();
        int index = (int)_swapChain.CurrentBackBufferIndex;
        Transition(_backBuffers[index]!, ResourceStates.Common, ResourceStates.RenderTarget);
        _commands.ClearRenderTargetView(Rtv(index), new Vortice.Mathematics.Color4(0, 0, 0, 1));
        Transition(_backBuffers[index]!, ResourceStates.RenderTarget, ResourceStates.Common);
        Submit();
        _swapChain.Present(0, PresentFlags.None).CheckError();
        WaitForGpu();
    }

    private CpuDescriptorHandle Rtv(int index) => _rtvs.GetCPUDescriptorHandleForHeapStart() + index * _rtvStride;
    private void Transition(ID3D12Resource resource, ResourceStates before, ResourceStates after)
        => _commands.ResourceBarrierTransition(resource, before, after, uint.MaxValue, ResourceBarrierFlags.None);
    private void BeginCommands()
    {
        _allocator.Reset();
        _commands.Reset(_allocator, null);
    }
    private void Submit()
    {
        _commands.Close();
        _queue.ExecuteCommandLists(_submission);
    }
    private void WaitForGpu()
    {
        ulong value = ++_fenceValue;
        _queue.Signal(_fence, value).CheckError();
        _fence.SetEventOnCompletion(value, _gpuDone.SafeWaitHandle.DangerousGetHandle()).CheckError();
        while (_fence.CompletedValue < value)
        {
            if (!_gpuDone.WaitOne(100)) _device.DeviceRemovedReason.CheckError();
        }
        _device.DeviceRemovedReason.CheckError();
    }

    private void ReleaseSizeResources()
    {
        for (int i = 0; i < 2; i++)
        {
            DisposeResource(_backBuffers[i]);
            _backBuffers[i] = null;
        }
        DisposeResource(_terrainNative);
        _terrainNative = null;
        DisposeResource(_terrain);
        _terrain = null;
        DisposeResource(_scene);
        _scene = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_attached)
        {
            try { _bind(0); } // UI-thread acknowledgement, while the UI remains free to dispatch.
            catch (Exception ex) { App.WriteCrashLog("Sonic detach", ex.Message, ex); }
            _attached = false;
        }
        try
        {
            if (_fence != null) WaitForGpu();
        }
        catch (Exception ex) { App.WriteCrashLog("Sonic GPU drain", ex.Message, ex); }
        ReleaseSizeResources();
        DisposeResource(_effect);
        DisposeResource(_vertices);
        DisposeResource(_pipelines);
        DisposeResource(_commands);
        DisposeResource(_allocator);
        DisposeResource(_rtvs);
        DisposeResource(_srvs);
        DisposeResource(_swapChain);
        DisposeResource(_factory);
        DisposeResource(_fence);
        DisposeResource(_queue);
        DisposeResource(_device);
        DisposeResource(_compute);
        _gpuDone.Dispose();
    }

    private static void DisposeResource(IDisposable? resource)
    {
        try { resource?.Dispose(); }
        catch (Exception ex) { App.WriteCrashLog("Sonic resource disposal", ex.Message, ex); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect bounds);
}

internal readonly record struct DisplayOutput(bool HdrEnabled, float PeakNits);
