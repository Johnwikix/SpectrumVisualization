using ComputeSharp;
using ComputeSharp.Interop;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Rendering;

/// <summary>
/// All methods run on one render thread. Buffered effects overlap scene work with ordered
/// reconstruction on a separate queue. A frame slot is reused only after its final consumer.
/// </summary>
internal sealed unsafe partial class GpuGraphics : IDisposable
{
    private const Format OutputFormat = Format.R10G10B10A2_UNorm;
    private readonly Action<nint, int, int> _bind;
    private GraphicsDevice _compute = null!;
    private GpuDeviceLease? _deviceLease;
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
    private ID3D12Resource? _primaryScene;
    private ID3D12Resource? _scene => _frameIndex == 0 ? _primaryScene : _secondScene;
    private ID3D12Resource? _antialias;
    private ID3D12Resource? _reconstructed;
    private TemporalReconstruction? _temporal;
    private SmaaPass? _smaa;
    private ReconstructionMode _activeMode;
    private uint _capabilities;
    private bool _unsupportedScale;
    private Func<IGpuVisualizerEffect> _effectFactory;
    internal ReconstructionStatus Reconstruction => new(_capabilities, _options.Mode, _activeMode, _unsupportedScale);
    private ColorOutputPipelines _pipelines = null!;
    private IGpuVisualizerEffect _effect = null!;
    private readonly SpectrumAnalyzer _analyzer;
    private GpuSceneOptions _options;
    private GpuRenderSize _size;
    private bool _attached;
    private bool _hdr;
    private bool _disposed;
    private bool _resized;
    private bool _reconstructionDeferred;
    private bool _reconstructionDirty;
    public int Width { get; private set; }
    public int Height { get; private set; }
    internal int RenderScalePercent => _options.RenderScalePercent;
    internal GpuRenderSize RenderSize => _size;
    internal bool CaptureTimings { get; set; }
    internal bool LastFrameRendered { get; private set; }
    internal double LastPresentMilliseconds { get; private set; }
    internal double LastGpuWaitMilliseconds { get; private set; }

    public GpuGraphics(SpectrumAnalyzer analyzer, Action<nint, int, int> bind, int width, int height,
        Func<IGpuVisualizerEffect> factory, GpuSceneOptions options, bool requestTearing = true, bool deferReconstruction = false)
    {
        _bind = bind;
        _analyzer = analyzer;
        _options = options;
        _effectFactory = factory;
        _reconstructionDeferred = deferReconstruction;
        try
        {
            _deviceLease = GpuDeviceLease.Acquire();
            _compute = _deviceLease.Device;
            Guid deviceId = new("189819F1-1DB6-4B57-BE54-1821339B85F7");
            void* native = null;
            InteropServices.GetID3D12Device(_compute, &deviceId, &native);
            _device = new ID3D12Device((nint)native);
            _queue = _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
            _allocator = _device.CreateCommandAllocator(CommandListType.Direct);
            _commands = _device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, _allocator, null);
            _maintenanceCommands = _commands;
            _commands.Close();
            _submission[0] = _commands;
            _fence = _device.CreateFence(0, FenceFlags.None);
            _factory = DXGI.CreateDXGIFactory2<IDXGIFactory6>(false);
            _rtvs = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.RenderTargetView, 6));
            _srvs = _device.CreateDescriptorHeap(new DescriptorHeapDescription(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 3, DescriptorHeapFlags.ShaderVisible));
            _rtvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
            _srvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            _pipelines = new ColorOutputPipelines(_device);
            _effect = factory();
            _effect.Initialize(new GpuEffectServices(_compute, _device, analyzer));
            if (!deferReconstruction) _capabilities = TemporalReconstruction.Capabilities(_device);
            CreatePresentationSwapChain(width, height, requestTearing);
            CreateSizeResources(width, height);
            // Establish a live SDR chain with black content before setting a color space.
            ClearAndPresent();
            _bind(_swapChain.NativePointer, Width, Height);
            _attached = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Resize(int width, int height) => Resize(width, height, _options);

    internal void Resize(int width, int height, GpuSceneOptions options)
    {
        if (width == Width && height == Height)
        {
            Configure(options);
            return;
        }
        WaitForGpu();
        ReleaseSizeResources();
        // Release every application reference before ResizeBuffers; each resize is followed by a present.
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, OutputFormat, _swapChainFlags).CheckError();
        var actual = _swapChain.Description1;
        if (actual.Width != width || actual.Height != height)
            throw new InvalidOperationException("The swap chain did not apply its requested size.");
        _options = options;
        CreateSizeResources(width, height);
        _swapChain.SetColorSpace1(_hdr ? ColorSpaceType.RgbFullG2084NoneP2020 : ColorSpaceType.RgbFullG22NoneP709);
        // Keep the previous composed image until Render submits a complete replacement.
        // Presenting a black clear here caused a flash on every resize event.
        _resized = true;
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
        CreateSceneResources(_options);
    }

    public void Configure(GpuSceneOptions options)
    {
        if (_options == options && !_reconstructionDirty) return;
        if (_reconstructionDirty || _options.RenderScalePercent != options.RenderScalePercent || _options.Mode != options.Mode ||
            (options.Mode == ReconstructionMode.Dlss && _options.DlssPreset != options.DlssPreset))
        {
            WaitForGpu();
            CreateSceneResources(options);
        }
        else if (_options.Detail != options.Detail && _effect is IBufferedGpuEffect)
        {
            WaitForGpu();
            ConfigurePipeline(options.Detail);
            _effect.Resize(_size);
        }
        ResetHistory();
        _options = options;
    }

    /// <summary>Called on the live worker only after startup releases all of its SDK contexts.</summary>
    internal void CompleteWarmup()
    {
        if (!_reconstructionDeferred) return;
        _capabilities = TemporalReconstruction.Capabilities(_device);
        _reconstructionDeferred = false;
        _reconstructionDirty = _options.Mode >= ReconstructionMode.XeSS;
    }

    public void ChangeEffect(Func<IGpuVisualizerEffect> factory)
    {
        WaitForGpu();
        var candidate = factory();
        try
        {
            candidate.Initialize(new GpuEffectServices(_compute, _device, _analyzer));
            if (candidate is ITemporalGpuEffect temporal) temporal.ConfigureTemporal(_temporal != null);
            candidate.Resize(_size);
        }
        catch
        {
            DisposeResource(candidate);
            throw;
        }
        var previous = _effect;
        _effect = candidate;
        _effectFactory = factory;
        DisposeResource(_temporal);
        _temporal = null;
        DisposeResource(previous);
        // A different effect may not expose temporal inputs. Re-evaluate the selected algorithm.
        CreateSceneResources(_options);
        ResetHistory();
    }

    private void CreateSceneResources(GpuSceneOptions options)
        => CreateSceneResources(options, GpuRenderSize.Create(Width, Height, options.RenderScalePercent));

    private void CreateSceneResources(GpuSceneOptions options, GpuRenderSize size)
    {
        _frameIndex = 0;
        _nextFrameIndex = 0;
        _reconstructionDirty = false;
        DisposeResource(_temporal);
        _temporal = null;
        _activeMode = options.Mode;
        _unsupportedScale = false;
        if (options.Mode >= ReconstructionMode.XeSS)
        {
            if (_effect is ITemporalGpuEffect && (_capabilities & (1u << (int)options.Mode)) != 0)
            {
                BeginCommands();
                try
                {
                    _temporal = TemporalReconstruction.Create(_device, _commands, options.Mode, options.DlssPreset, size);
                }
                catch (Exception ex)
                {
                    // Do not execute a partially recorded SDK initialization on failure.
                    _commands.Close();
                    DisposeResource(_temporal);
                    _temporal = null;
                    _unsupportedScale = ex is ReconstructionSizeException;
                    if (!_unsupportedScale)
                    {
                        _capabilities &= ~(1u << (int)options.Mode);
                        App.WriteCrashLog("Reconstruction initialization", ex.Message, ex);
                    }
                }
                if (_temporal != null)
                {
                    Submit();
                    WaitForGpu();
                    size = _temporal.Size;
                }
            }
            if (_temporal == null) _activeMode = ReconstructionMode.Fxaa;
        }
        ID3D12Resource? scene = null;
        ID3D12Resource? secondScene = null;
        ID3D12Resource? antialias = null;
        ID3D12Resource? reconstructed = null;
        SmaaPass? smaa = null;
        try
        {
            scene = CreateColorTexture(size.Width, size.Height);
            if (PipelineEnabled) secondScene = CreateColorTexture(size.Width, size.Height);
            if (_activeMode is ReconstructionMode.Fxaa or ReconstructionMode.Smaa) antialias = CreateColorTexture(Width, Height);
            if (_temporal != null) reconstructed = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.R16G16B16A16_Float, (uint)Width, (uint)Height, 1, 1, 1, 0,
                    ResourceFlags.AllowUnorderedAccess | ResourceFlags.AllowRenderTarget), ResourceStates.PixelShaderResource);
            if (_activeMode == ReconstructionMode.Smaa)
            {
                BeginCommands();
                smaa = new SmaaPass(_device, _commands, antialias!, Width, Height);
                Submit();
                WaitForGpu();
                smaa.ReleaseUploads();
            }
            if (_effect is ITemporalGpuEffect temporal) temporal.ConfigureTemporal(_temporal != null);
            ConfigurePipeline(options.Detail);
            _effect.Resize(size);
        }
        catch
        {
            DisposeResource(scene);
            DisposeResource(secondScene);
            DisposeResource(antialias);
            DisposeResource(reconstructed);
            DisposeResource(smaa);
            throw;
        }
        DisposeResource(_primaryScene);
        DisposeResource(_secondScene);
        DisposeResource(_antialias);
        DisposeResource(_smaa);
        DisposeResource(_reconstructed);
        _primaryScene = scene;
        _secondScene = secondScene;
        _antialias = antialias;
        _reconstructed = reconstructed;
        _smaa = smaa;
        _size = size;
        _device.CreateRenderTargetView(_scene, null, Rtv(2));
        if (_secondScene != null) _device.CreateRenderTargetView(_secondScene, null, Rtv(5));
        _device.CreateShaderResourceView(_scene, null, _srvs.GetCPUDescriptorHandleForHeapStart());
        if (_antialias != null)
        {
            _device.CreateRenderTargetView(_antialias, null, Rtv(3));
            _device.CreateShaderResourceView(_smaa?.Output ?? _antialias, null, _srvs.GetCPUDescriptorHandleForHeapStart() + _srvStride);
        }
        if (_reconstructed != null)
        {
            _device.CreateRenderTargetView(_reconstructed, null, Rtv(4));
            _device.CreateShaderResourceView(_reconstructed, null, _srvs.GetCPUDescriptorHandleForHeapStart() + 2 * _srvStride);
        }
    }

    private ID3D12Resource CreateColorTexture(int width, int height) => _device.CreateCommittedResource(
        new HeapProperties(HeapType.Default), HeapFlags.None,
        ResourceDescription.Texture2D(Format.R16G16B16A16_Float, (uint)width, (uint)height, 1, 1, 1, 0, ResourceFlags.AllowRenderTarget),
        ResourceStates.PixelShaderResource);

    public bool SetHdr(bool requested)
    {
        if (_hdr == requested) return _hdr;
        WaitForGpu();
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

    public bool Render(double elapsed, float white, float peak)
    {
        LastFrameRendered = false;
        LastPresentMilliseconds = 0;
        LastGpuWaitMilliseconds = 0;
        if (PipelineEnabled) return RenderPipelined(elapsed, white, peak);
        TemporalFrame frame = _temporal?.BeginFrame(elapsed) ?? default;
        if (_effect is ITemporalGpuEffect temporalEffect) temporalEffect.SetTemporalFrame(frame);
        _effect.PrepareFrame(elapsed, _options.Detail);
        BeginCommands();
        Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        _effect.RecordScene(_commands, Rtv(2));
        Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        if (_temporal != null && !RecordReconstruction(in frame, elapsed)) return false;
        return ComposeAndPresent(white, peak);
    }

    private bool RecordReconstruction(in TemporalFrame frame, double elapsed)
    {
        Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.NonPixelShaderResource);
        Transition(_reconstructed!, ResourceStates.PixelShaderResource, ResourceStates.UnorderedAccess);
        try { _temporal!.Record(_commands, _scene!, (ITemporalGpuEffect)_effect, _reconstructed!, frame, elapsed); }
        catch (Exception ex)
        {
            _commands.Close();
            App.WriteCrashLog("Reconstruction dispatch", ex.Message, ex);
            _capabilities &= ~(1u << (int)_activeMode);
            // Older slots and a scene submitted before the SDK call may still own effect resources.
            WaitForGpu();
            DisposeResource(_temporal);
            _temporal = null;
            DisposeResource(_effect);
            _effect = _effectFactory();
            _effect.Initialize(new GpuEffectServices(_compute, _device, _analyzer));
            CreateSceneResources(_options);
            return false;
        }
        Transition(_reconstructed!, ResourceStates.UnorderedAccess, ResourceStates.RenderTarget);
        ((ITemporalGpuEffect)_effect).RecordOverlay(_commands, Rtv(4));
        Transition(_reconstructed!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        Transition(_scene!, ResourceStates.NonPixelShaderResource, ResourceStates.PixelShaderResource);
        return true;
    }

    internal void ResetHistory() => _temporal?.Reset();

    private bool ComposeAndPresent(float white, float peak)
    {
        RecordComposition(white, peak);
        Submit();
        ulong rendered = SignalGpu();
        bool presented = PresentFrame();
        long started = CaptureTimings ? Stopwatch.GetTimestamp() : 0;
        WaitForGpu(rendered);
        if (CaptureTimings) LastGpuWaitMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return presented;
    }

    private void RecordComposition(float white, float peak)
    {
        _commands.SetDescriptorHeaps(_srvs);
        _commands.SetGraphicsRootSignature(_pipelines.Root);
        _commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commands.RSSetViewport(0, 0, Width, Height, 0, 1);
        _commands.RSSetScissorRect(Width, Height);
        _commands.SetGraphicsRootDescriptorTable(0, _srvs.GetGPUDescriptorHandleForHeapStart() + (_temporal != null ? 2 * _srvStride : 0));
        float* constants = stackalloc float[6] { _hdr ? 1 : 0, white, peak, 0, 1f / Width, 1f / Height };
        _commands.SetGraphicsRoot32BitConstants(1, 6, constants, 0);
        if (_antialias != null)
        {
            Transition(_antialias, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            _commands.OMSetRenderTargets(Rtv(3), null);
            _commands.SetPipelineState(_pipelines.PrepareAntialias);
            _commands.DrawInstanced(3, 1, 0, 0);
            Transition(_antialias, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            if (_smaa != null)
            {
                _smaa.Record(_commands, _hdr ? MathF.Sqrt(white / peak) : 1);
                _commands.SetDescriptorHeaps(_srvs);
                _commands.SetGraphicsRootSignature(_pipelines.Root);
                _commands.SetGraphicsRoot32BitConstants(1, 6, constants, 0);
            }
            _commands.SetGraphicsRootDescriptorTable(0, _srvs.GetGPUDescriptorHandleForHeapStart() + _srvStride);
        }
        int buffer = (int)_swapChain.CurrentBackBufferIndex;
        Transition(_backBuffers[buffer]!, ResourceStates.Common, ResourceStates.RenderTarget);
        _commands.OMSetRenderTargets(Rtv(buffer), null);
        _commands.SetPipelineState(_smaa != null ? _pipelines.PreparedOutput : _antialias != null ? _pipelines.AntialiasOutput : _pipelines.Output);
        _commands.DrawInstanced(3, 1, 0, 0);
        Transition(_backBuffers[buffer]!, ResourceStates.RenderTarget, ResourceStates.Common);
    }

    private bool PresentFrame()
    {
        long started = CaptureTimings ? Stopwatch.GetTimestamp() : 0;
        var result = _swapChain.Present(0, PresentationFlags(nonblocking: true));
        if (CaptureTimings) LastPresentMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (result.Code != (int)Vortice.DXGI.ResultCode.WasStillDrawing) result.CheckError();
        LastFrameRendered = true;
        if (_resized && result.Code != (int)Vortice.DXGI.ResultCode.WasStillDrawing)
        {
            _bind(_swapChain.NativePointer, Width, Height);
            _resized = false;
        }
        return result.Code != (int)Vortice.DXGI.ResultCode.WasStillDrawing;
    }

    private void ClearAndPresent()
    {
        BeginCommands();
        int index = (int)_swapChain.CurrentBackBufferIndex;
        Transition(_backBuffers[index]!, ResourceStates.Common, ResourceStates.RenderTarget);
        _commands.ClearRenderTargetView(Rtv(index), new Vortice.Mathematics.Color4(0, 0, 0, 1));
        Transition(_backBuffers[index]!, ResourceStates.RenderTarget, ResourceStates.Common);
        Submit();
        _swapChain.Present(0, PresentationFlags(nonblocking: false)).CheckError();
        WaitForGpu();
    }

    private CpuDescriptorHandle Rtv(int index) => _rtvs.GetCPUDescriptorHandleForHeapStart() +
        (index == 2 && _frameIndex == 1 ? 5 : index) * _rtvStride;
    private void Transition(ID3D12Resource resource, ResourceStates before, ResourceStates after)
        => _commands.ResourceBarrierTransition(resource, before, after, uint.MaxValue, ResourceBarrierFlags.None);
    private void BeginCommands()
    {
        // Maintenance/readback lists have a single allocator and may access any frame slot.
        if (_sceneFence != null) WaitForFence(_sceneFence, _sceneFenceValue);
        WaitForGpu(_fenceValue);
        _commands = _maintenanceCommands;
        _allocator.Reset();
        _commands.Reset(_allocator, null);
    }
    private void Submit()
    {
        _commands.Close();
        _submission[0] = _commands;
        _queue.ExecuteCommandLists(_submission);
    }
    private ulong SignalGpu()
    {
        ulong value = ++_fenceValue;
        _queue.Signal(_fence, value).CheckError();
        return value;
    }
    private void WaitForGpu()
    {
        if (_sceneFence != null)
        {
            ulong scene = ++_sceneFenceValue;
            _sceneQueue!.Signal(_sceneFence, scene).CheckError();
            WaitForFence(_sceneFence, scene);
        }
        WaitForGpu(SignalGpu());
    }
    private void WaitForGpu(ulong value)
        => WaitForFence(_fence, value);

    private void WaitForFence(ID3D12Fence fence, ulong value)
    {
        if (fence.CompletedValue >= value)
        {
            _device.DeviceRemovedReason.CheckError();
            return;
        }
        fence.SetEventOnCompletion(value, _gpuDone.SafeWaitHandle.DangerousGetHandle()).CheckError();
        while (fence.CompletedValue < value)
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
        DisposeResource(_antialias);
        _antialias = null;
        DisposeResource(_temporal);
        _temporal = null;
        DisposeResource(_reconstructed);
        _reconstructed = null;
        DisposeResource(_smaa);
        _smaa = null;
        DisposeResource(_primaryScene);
        _primaryScene = null;
        DisposeResource(_secondScene);
        _secondScene = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_attached)
        {
            try { _bind(0, Width, Height); } // UI-thread acknowledgement, while the UI remains free to dispatch.
            catch (Exception ex) { App.WriteCrashLog("GPU detach", ex.Message, ex); }
            _attached = false;
        }
        try
        {
            if (_fence != null) WaitForGpu();
        }
        catch (Exception ex) { App.WriteCrashLog("GPU drain", ex.Message, ex); }
        ReleaseSizeResources();
        DisposeResource(_effect);
        DisposeResource(_pipelines);
        DisposePipeline();
        DisposeResource(_maintenanceCommands);
        DisposeResource(_allocator);
        DisposeResource(_rtvs);
        DisposeResource(_srvs);
        DisposeResource(_swapChain);
        DisposeResource(_frameLatencyHandle);
        DisposeResource(_factory);
        DisposeResource(_fence);
        DisposeResource(_queue);
        DisposeResource(_device);
        DisposeResource(_deviceLease);
        _gpuDone.Dispose();
    }

    internal static void DisposeResource(IDisposable? resource)
    {
        try { resource?.Dispose(); }
        catch (Exception ex) { App.WriteCrashLog("GPU resource disposal", ex.Message, ex); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect bounds);
}

internal readonly record struct DisplayOutput(bool HdrEnabled, float PeakNits);

/// <summary>Configures scene resolution, postprocessing and effect-specific detail at a frame boundary.</summary>
internal readonly record struct GpuSceneOptions(int RenderScalePercent, ReconstructionMode Mode, int Detail, string DlssPreset = "k")
{
    internal GpuSceneOptions(int scale, bool antialias, int detail) : this(scale, antialias ? ReconstructionMode.Fxaa : ReconstructionMode.Off, detail) { }
}

/// <summary>Immutable render-thread status, marshalled to the UI only when it changes.</summary>
internal readonly record struct ReconstructionStatus(uint Capabilities, ReconstructionMode Requested, ReconstructionMode Active, bool UnsupportedScale = false);
