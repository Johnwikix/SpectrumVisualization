using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct MachiDxrFrame(MachiCamera Camera, Vector3 Sky, Vector3 SunDirection,
    Vector3 SunRadiance, float Wetness, float DeltaSeconds, bool ResetHistory, bool RequestRayReconstruction)
{
    internal Vector3? SkyBottom { get; init; }
    internal float Artificial { get; init; } = 1;
    internal float SnowCover { get; init; }
    internal float Mist { get; init; }
    internal float Haze { get; init; }
    internal Vector3? HemiSky { get; init; }
    internal Vector3? HemiGround { get; init; }
    internal Vector3 CityGlow { get; init; }
    internal Vector3 Puddles { get; init; }
    internal float PuddleFill { get; init; }
    internal float RainTime { get; init; }
    internal Matrix4x4 RainShelters { get; init; }
    internal float AmbientTime { get; init; }
    internal float Rain { get; init; }
    internal bool NoVacancy { get; init; }
    internal int SamplesPerPixel { get; init; } = 1;
    internal int MaxBounces { get; init; } = 8;
}

/// <summary>Records Machi path tracing and reconstruction on the caller's ordered direct queue.</summary>
internal sealed unsafe partial class MachiDxrBackend : IDisposable
{
    private const int FrameSlots = 2;
    private const int TraceOutputs = (int)MachiDxrSignal.Count;
    private const int ResolveDenoiserInputs = 12;
    private const int ResolveStart = TraceOutputs + 2;
    private const int ResolveScaledSource = ResolveStart + ResolveDenoiserInputs;
    private const int ResolveRawSource = ResolveScaledSource + 1;
    private const int ResolveProxy = ResolveRawSource + 1;
    private const int ResolveOutput = ResolveProxy + 1;
    private const int DescriptorsPerFrame = ResolveOutput + 1;
    private const int ConstantsStride = 512;
    private const int RayReconstructionGuides = 3;
    private const Format HdrFormat = Format.R16G16B16A16_Float;
    private const ResourceStates Read = ResourceStates.NonPixelShaderResource;
    private const ResourceStates Write = ResourceStates.UnorderedAccess;
    private static Format SignalFormat(int index) => MachiDxrShaders.SignalComponents((MachiDxrSignal)index) switch
    {
        1 => Format.R32_Float,
        2 => Format.R16G16_Float,
        _ => HdrFormat
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix4x4 RayBasis, ViewProjection, PreviousViewProjection, View;
        public Vector4 SkyWetness, SunDirectionFrame, SunRadianceFar, SizeJitter, Lighting, SkyBottom, AnalyticLightControl;
        public Vector4 HemiSkyRain, HemiGroundTime, CityGlow, Puddles;
        public Matrix4x4 RainShelters;
        public uint SamplesPerPixel, MaxBounces;
        public float PixelWidthAtUnitDepth;
    }

    private int _width, _height;
    private readonly int _stride;
    private readonly ID3D12Device5 _device;
    private readonly List<IDisposable> _owned = [];
    private readonly ID3D12Resource[] _signals = new ID3D12Resource[TraceOutputs];
    private readonly ID3D12Resource[] _denoised = new ID3D12Resource[6];
    private readonly ID3D12GraphicsCommandList4?[] _commandLists = new ID3D12GraphicsCommandList4?[FrameSlots];
    private readonly nint[] _commandPointers = new nint[FrameSlots];
    private readonly nint[] _outputs = new nint[FrameSlots], _lyrics = new nint[FrameSlots];
    private readonly nint[] _nrd = new nint[3];
    private readonly bool[] _nrdBound = new bool[FrameSlots];
    private bool _preferRayReconstruction;
    private GpuRenderSize _renderSize;
    private ReconstructionMode _superResolutionMode;
    private string _preset = "k";
    private readonly ID3D12DescriptorHeap _descriptors;
    private readonly TracePipeline _tracePipeline;
    private readonly ID3D12RootSignature _resolveRoot;
    private readonly ID3D12PipelineState _resolve, _resolveRaw, _resolveScale;
    private ID3D12Resource? _resolveInput;
    private readonly ID3D12Resource _constantBuffer;
    private DispatchRaysDescription _dispatch;
    private byte* _mapped;
    private MachiCamera _previous;
    private Vector2 _previousJitter;
    private bool _sceneBuilt, _historyValid, _sizeReady, _disposed;
    private bool _denoiserAttempted;
    private uint _frameIndex;
    private int _previousSamplesPerPixel, _previousMaxBounces;

    internal bool DenoiserAvailable { get; private set; }
    internal bool DenoisingDisabled { get; private set; }
    internal bool RayReconstructionAvailable { get; private set; }
    internal bool RayReconstructionActive { get; private set; }
    internal ID3D12Resource SurfaceDepth => Signal(MachiDxrSignal.Depth);
    internal Vector2 SurfaceDepthUvOffset => RayReconstructionActive || SuperResolutionActive >= ReconstructionMode.XeSS
        ? new(-_previousJitter.X / _width, -_previousJitter.Y / _height) : Vector2.Zero;
    private ID3D12Resource Signal(MachiDxrSignal slot) => _signals[(int)slot];

    internal MachiDxrBackend(ID3D12Device device, int width, int height,
        ReadOnlySpan<FoliaMachi.Vertex> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<byte> signPixels = default, int signWidth = 0, int signHeight = 0,
        int staticIndexCount = 0, ReadOnlySpan<FoliaMachi.Actor> actors = default, bool preferRayReconstruction = false,
        ReadOnlySpan<FoliaMachi.Light> lights = default, int outputWidth = 0, int outputHeight = 0,
        ReconstructionMode superResolutionMode = ReconstructionMode.Off, string preset = "k", bool disableDenoising = false,
        TracePipeline? tracePipeline = null)
    {
        _device = device.QueryInterface<ID3D12Device5>();
        try
        {
            _stride = (int)device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
            _descriptors = Keep(device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView,
                FrameSlots * DescriptorsPerFrame, DescriptorHeapFlags.ShaderVisible)));
            _tracePipeline = tracePipeline ?? Keep(new TracePipeline(_device));
            _dispatch = _tracePipeline.Dispatch;
            _resolveRoot = Keep(device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
                [Table(DescriptorRangeType.ShaderResourceView, ResolveDenoiserInputs, 0),
                    Table(DescriptorRangeType.ShaderResourceView, 1, ResolveDenoiserInputs),
                    Table(DescriptorRangeType.ShaderResourceView, 1, ResolveDenoiserInputs + 1),
                    Table(DescriptorRangeType.UnorderedAccessView, 1, 0)])));
            string resolveShader = MachiDxrShaders.Resolve;
            _resolve = ResolvePipeline("Resolve");
            _resolveRaw = ResolvePipeline("ResolveRaw");
            _resolveScale = ResolvePipeline("ResolveScale");
            ID3D12PipelineState ResolvePipeline(string entry) => Keep(GpuPipelineCache.Create(device, new ComputePipelineStateDescription
            {
                RootSignature = _resolveRoot,
                ComputeShader = ShaderCompiler.Compile(resolveShader, entry, "cs_6_0")
            }, "Machi/Resolve/root-v3/" + entry));
            _constantBuffer = Buffer(FrameSlots * 2 * ConstantsStride);
            _mapped = _constantBuffer.Map<byte>(0);
            ReplaceScene(vertices, indices, signPixels, signWidth, signHeight, staticIndexCount, actors, lights);
            Resize(new(outputWidth > 0 ? outputWidth : width, outputHeight > 0 ? outputHeight : height, width, height),
                superResolutionMode, preset, preferRayReconstruction, disableDenoising);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Recreates screen-space resources after the caller drains the queue. Scene acceleration structures survive.</summary>
    internal void Resize(int width, int height) =>
        Resize(new(width, height, width, height), _superResolutionMode, _preset, _preferRayReconstruction, DenoisingDisabled);

    internal void Resize(in GpuRenderSize size, ReconstructionMode mode, string preset, bool preferRayReconstruction, bool disableDenoising = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int width = size.Width, height = size.Height;
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (_renderSize == size && _superResolutionMode == mode && _preset == preset &&
            _preferRayReconstruction == preferRayReconstruction && DenoisingDisabled == disableDenoising && _sizeReady) return;

        ReleaseSizeResources();
        _renderSize = size;
        _superResolutionMode = mode;
        _preset = preset;
        _preferRayReconstruction = preferRayReconstruction;
        DenoisingDisabled = disableDenoising;
        _width = width;
        _height = height;
        _dispatch.Width = (uint)width;
        _dispatch.Height = (uint)height;
        _historyValid = false;
        _frameIndex = 0;
        Array.Clear(_outputs);
        Array.Clear(_lyrics);
        try
        {
            for (int i = 0; i < _signals.Length; i++)
                if (AllocateTraceSignal((MachiDxrSignal)i, mode))
                    _signals[i] = Texture(SignalFormat(i));
            if (width != size.OutputWidth || height != size.OutputHeight || mode >= ReconstructionMode.XeSS)
                _resolveInput = Texture(HdrFormat);
            ReadOnlySpan<MachiDxrSignal> denoiserInputs = [
                MachiDxrSignal.Diffuse, MachiDxrSignal.DiffuseSh, MachiDxrSignal.Specular, MachiDxrSignal.SpecularSh,
                MachiDxrSignal.DiffuseFactor, MachiDxrSignal.SpecularFactor, MachiDxrSignal.Deterministic,
                MachiDxrSignal.Reflection, MachiDxrSignal.Transmission, MachiDxrSignal.ViewZ,
                MachiDxrSignal.ReflectionZ, MachiDxrSignal.TransmissionZ];
            for (int slot = 0; slot < FrameSlots; slot++)
            {
                for (int i = 0; i < TraceOutputs; i++)
                    BindTraceOutput(slot, i);
                for (int i = 0; i < denoiserInputs.Length; i++)
                    BindResolveInput(slot, i, denoiserInputs[i]);
                var noisy = Signal(MachiDxrSignal.Noisy);
                _device.CreateShaderResourceView(_resolveInput ?? noisy, null, Cpu(slot, ResolveScaledSource));
                _device.CreateShaderResourceView(noisy, null, Cpu(slot, ResolveRawSource));
                if (_resolveInput != null)
                    _device.CreateUnorderedAccessView(_resolveInput, null, null, Cpu(slot, ResolveProxy));
            }
            if (!DenoisingDisabled)
            {
                if (_preferRayReconstruction) InitializeRayReconstruction();
                if (!_preferRayReconstruction || !RayReconstructionAvailable)
                    InitializeDenoisers();
            }
            _sizeReady = true;
        }
        catch
        {
            ReleaseSizeResources();
            throw;
        }
    }

    private bool AllocateTraceSignal(MachiDxrSignal signal, ReconstructionMode mode)
    {
        if (DenoisingDisabled)
            return signal is MachiDxrSignal.Noisy or MachiDxrSignal.Depth ||
                signal == MachiDxrSignal.Motion && mode >= ReconstructionMode.XeSS;
        return !IsRayReconstructionGuide((int)signal);
    }

    private void BindResolveInput(int slot, int index, MachiDxrSignal signal)
    {
        var descriptor = Cpu(slot, ResolveStart + index);
        if (_signals[(int)signal] is { } resource)
            _device.CreateShaderResourceView(resource, null, descriptor);
        else
            _device.CreateShaderResourceView(null, new ShaderResourceViewDescription
            {
                Format = SignalFormat((int)signal), ViewDimension = ShaderResourceViewDimension.Texture2D,
                Shader4ComponentMapping = ShaderComponentMapping.Default,
                Texture2D = new Texture2DShaderResourceView { MipLevels = 1 }
            }, descriptor);
    }

    private void ReleaseSizeResources()
    {
        _sizeReady = false;
        DisposeRayReconstruction();
        DisposeSuperResolution();
        DestroyDenoisers();
        _resolveInput?.Dispose();
        _resolveInput = null;
        foreach (var signal in _signals) signal?.Dispose();
        Array.Clear(_signals);
        foreach (var texture in _denoised) texture?.Dispose();
        Array.Clear(_denoised);
        RayReconstructionAvailable = false;
        RayReconstructionActive = false;
        _rayReconstructionChecked = false;
        Array.Clear(_nrdBound);
        DenoiserAvailable = false;
        _denoiserAttempted = false;
        RayReconstructionResult = DenoiserResult = 0;
    }

    internal void ResetHistory()
    {
        _historyValid = false;
        _jitterIndex = 0;
    }

    private void InitializeDenoisers()
    {
        _denoiserAttempted = true;
        try
        {
            for (int i = 0; i < _denoised.Length; i++)
                _denoised[i] = Texture(HdrFormat);
            DenoiserResult = NativeNrd.CreatePbr(_device.NativePointer, (uint)_width, (uint)_height, out _nrd[0]);
            if (DenoiserResult == 0)
                DenoiserResult = NativeNrd.CreateOpticalPair(_device.NativePointer, (uint)_width, (uint)_height, out _nrd[1], out _nrd[2]);
            DenoiserAvailable = DenoiserResult == 0;
        }
        catch (DllNotFoundException exception) { DenoiserResult = exception.HResult; }
        catch (EntryPointNotFoundException exception) { DenoiserResult = exception.HResult; }
        catch (SharpGen.Runtime.SharpGenException exception) { DenoiserResult = exception.HResult; }
        if (!DenoiserAvailable)
        {
            DestroyDenoisers();
            foreach (var texture in _denoised) texture?.Dispose();
            Array.Clear(_denoised);
        }
    }

    private T Keep<T>(T item) where T : IDisposable
    {
        _owned.Add(item);
        return item;
    }
    private ID3D12Resource Buffer(ulong size, HeapType heap = HeapType.Upload, ResourceStates state = ResourceStates.GenericRead,
        ResourceFlags flags = ResourceFlags.None) =>
        Keep(CreateBuffer(size, heap, state, flags));

    private ID3D12Resource CreateBuffer(ulong size, HeapType heap, ResourceStates state, ResourceFlags flags) =>
        _device.CreateCommittedResource(new HeapProperties(heap), HeapFlags.None, ResourceDescription.Buffer(size, flags), state);

    private ID3D12Resource Texture(Format format) =>
        _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(format, (uint)_width, (uint)_height, 1, 1, 1, 0, ResourceFlags.AllowUnorderedAccess), Read);

    private CpuDescriptorHandle Cpu(int slot, int descriptor) =>
        _descriptors.GetCPUDescriptorHandleForHeapStart() + (slot * DescriptorsPerFrame + descriptor) * _stride;
    private GpuDescriptorHandle Gpu(int slot, int descriptor) =>
        _descriptors.GetGPUDescriptorHandleForHeapStart() + (slot * DescriptorsPerFrame + descriptor) * _stride;

    private void BindTraceOutput(int slot, int index)
    {
        if (_signals[index] is { } resource)
            _device.CreateUnorderedAccessView(resource, null, null, Cpu(slot, index));
        else
            _device.CreateUnorderedAccessView(null, null, new UnorderedAccessViewDescription
            {
                ViewDimension = UnorderedAccessViewDimension.Texture2D, Format = SignalFormat(index)
            }, Cpu(slot, index));
    }

    internal void Record(ID3D12GraphicsCommandList commands, ID3D12Resource output, ID3D12Resource lyrics,
        int frameSlot, in MachiDxrFrame frame, ReadOnlySpan<Matrix4x4> actorTransforms = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)frameSlot >= FrameSlots) throw new ArgumentOutOfRangeException(nameof(frameSlot));
        if (_commandPointers[frameSlot] != commands.NativePointer)
        {
            _commandLists[frameSlot]?.Dispose();
            _commandLists[frameSlot] = commands.QueryInterface<ID3D12GraphicsCommandList4>();
            _commandPointers[frameSlot] = commands.NativePointer;
        }
        var list = _commandLists[frameSlot]!;
        RecordGeometry(list, frameSlot, actorTransforms);
        bool wasRayReconstruction = RayReconstructionActive;
        bool requestRayReconstruction = !DenoisingDisabled && PrepareRayReconstruction(list, frame.RequestRayReconstruction);
        // The Folia host completes the preceding Machi frame before recording the next.
        if (!requestRayReconstruction) DisposeRayReconstruction();
        bool requestSuperResolution = !requestRayReconstruction && PrepareSuperResolution(list);

        bool reset = frame.ResetHistory || !_historyValid || frame.DeltaSeconds is <= 0 or > .25f
            || _previousSamplesPerPixel != frame.SamplesPerPixel || _previousMaxBounces != frame.MaxBounces
            || requestRayReconstruction != wasRayReconstruction
            || requestSuperResolution && SuperResolutionActive != _superResolutionMode;
        if (reset) _jitterIndex = 0;
        var temporal = requestRayReconstruction || requestSuperResolution
            ? TemporalReconstruction.Sample(_renderSize, _jitterIndex, reset) : new TemporalFrame(0, 0, reset);
        var jitter = new Vector2(temporal.JitterX, temporal.JitterY);
        var camera = frame.Camera;
        var previous = reset ? camera : _previous;
        Vector3 sun = frame.SunDirection.LengthSquared() > 1e-6f ? Vector3.Normalize(frame.SunDirection) : Vector3.UnitY;
        int offset = frameSlot * 2 * ConstantsStride;
        var constants = new Constants
        {
            RayBasis = camera.RayBasis, ViewProjection = camera.ViewProjection, PreviousViewProjection = previous.ViewProjection,
            View = camera.View, SkyWetness = new(frame.Sky, frame.Wetness), SunDirectionFrame = new(sun, _frameIndex & 0xffff),
            // Ray origins move opposite to the projection shift passed to the SDKs.
            SunRadianceFar = new(frame.SunRadiance, FoliaMachi.Far), SizeJitter = new(_width, _height, -jitter.X, -jitter.Y),
            Lighting = new(_emitterCount, frame.SnowCover, frame.Artificial, requestRayReconstruction ? 1 : 0),
            SkyBottom = new(frame.SkyBottom ?? frame.Sky * .45f, frame.RainTime),
            AnalyticLightControl = new(_analyticCount, _analyticFraction, frame.NoVacancy ? 1 : 0,
                requestRayReconstruction ? RayReconstructionGuides : DenoisingDisabled ? requestSuperResolution ? 2 : 1 : 0),
            HemiSkyRain = new(frame.HemiSky ?? frame.Sky, frame.Rain),
            HemiGroundTime = new(frame.HemiGround ?? frame.SkyBottom ?? frame.Sky * .45f, frame.AmbientTime),
            CityGlow = new(frame.CityGlow, FoliaMachi.BoardWhiteRadiance * frame.Artificial),
            Puddles = new(frame.Puddles, frame.PuddleFill), RainShelters = frame.RainShelters,
            SamplesPerPixel = (uint)frame.SamplesPerPixel, MaxBounces = (uint)frame.MaxBounces,
            PixelWidthAtUnitDepth = 2 / (camera.Projection.M11 * _width)
        };

        if (_lyrics[frameSlot] != lyrics.NativePointer)
        {
            _device.CreateShaderResourceView(lyrics, null, Cpu(frameSlot, TraceOutputs));
            _lyrics[frameSlot] = lyrics.NativePointer;
        }
        if (_outputs[frameSlot] != output.NativePointer)
        {
            _device.CreateUnorderedAccessView(output, null, null, Cpu(frameSlot, ResolveOutput));
            _outputs[frameSlot] = output.NativePointer;
        }
        *(Constants*)(_mapped + offset) = constants;
        RecordTrace(list, frameSlot, offset, requestRayReconstruction);

        SuperResolutionActive = ReconstructionMode.Off;
        RayReconstructionActive = requestRayReconstruction && TryRayReconstruction(list, camera, output, temporal, frame.DeltaSeconds);
        if (!RayReconstructionActive)
        {
            if (requestRayReconstruction) list.ResourceBarrierUnorderedAccessView(output);
            if (!DenoisingDisabled && !_denoiserAttempted)
                InitializeDenoisers();
            if (requestRayReconstruction && DenoiserAvailable)
            {
                // Preserve this frame's samples, motion and jitter. The first dispatch still reads its CBV.
                constants.AnalyticLightControl.W = 0;
                int fallbackOffset = offset + ConstantsStride;
                *(Constants*)(_mapped + fallbackOffset) = constants;
                RecordTrace(list, frameSlot, fallbackOffset, false);
            }
            bool denoised = DenoiserAvailable &&
                Denoise(list, frameSlot, camera, previous, reset || wasRayReconstruction, -jitter, frame.DeltaSeconds);
            if (denoised && !_nrdBound[frameSlot])
            {
                for (int i = 0; i < 4; i++)
                    _device.CreateShaderResourceView(_denoised[i], null, Cpu(frameSlot, ResolveStart + i));
                _device.CreateShaderResourceView(_denoised[4], null, Cpu(frameSlot, ResolveStart + 7));
                _device.CreateShaderResourceView(_denoised[5], null, Cpu(frameSlot, ResolveStart + 8));
                _nrdBound[frameSlot] = true;
            }
            bool resolveDirect = _resolveInput == null;
            if (!resolveDirect) list.ResourceBarrierTransition(_resolveInput!, Read, Write);
            Resolve(list, frameSlot, denoised ? _resolve : _resolveRaw,
                resolveDirect ? ResolveOutput : ResolveProxy, _width, _height);
            if (!resolveDirect)
            {
                list.ResourceBarrierTransition(_resolveInput!, Write, Read);
                if (!requestSuperResolution || !TrySuperResolution(list, output, camera, temporal, frame.DeltaSeconds))
                {
                    if (requestSuperResolution) list.ResourceBarrierUnorderedAccessView(output);
                    Resolve(list, frameSlot, _resolveScale, ResolveOutput, _renderSize.OutputWidth, _renderSize.OutputHeight);
                }
            }
        }
        _historyValid = true;
        _previousSamplesPerPixel = frame.SamplesPerPixel;
        _previousMaxBounces = frame.MaxBounces;
        _previous = camera;
        _previousJitter = -jitter;
        _jitterIndex++;
        _frameIndex++;
    }

    private void RecordTrace(ID3D12GraphicsCommandList4 list, int frameSlot, int offset,
        bool rayReconstruction)
    {
        TransitionTraceSignals(list, Read, Write, rayReconstruction);
        list.SetDescriptorHeaps(_descriptors);
        list.SetComputeRootSignature(_tracePipeline.TraceRoot);
        list.SetComputeRootConstantBufferView(0, _constantBuffer.GPUVirtualAddress + (uint)offset);
        list.SetComputeRootShaderResourceView(1, _tlas.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(2, _vertices.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(3, _indices.GPUVirtualAddress);
        list.SetComputeRootDescriptorTable(4, Gpu(frameSlot, 0));
        list.SetComputeRootDescriptorTable(5, Gpu(frameSlot, TraceOutputs));
        list.SetComputeRootShaderResourceView(6, _instanceData.GPUVirtualAddress + (ulong)(frameSlot * _instances.Length * sizeof(InstanceData)));
        list.SetComputeRootShaderResourceView(7, _emitters.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(8, _emitterPdf.GPUVirtualAddress);
        list.SetComputeRootShaderResourceView(9, _analyticLights.GPUVirtualAddress);
        list.SetPipelineState1(_tracePipeline.Trace);
        list.DispatchRays(_dispatch);
        TransitionTraceSignals(list, Write, Read, rayReconstruction);
    }

    private void TransitionTraceSignals(ID3D12GraphicsCommandList4 list, ResourceStates before, ResourceStates after,
        bool rayReconstruction)
    {
        Span<ResourceBarrier> barriers = stackalloc ResourceBarrier[TraceOutputs];
        int count = 0;
        for (int i = 0; i < _signals.Length; i++)
        {
            var binding = (MachiDxrSignal)i;
            bool used = rayReconstruction
                ? binding is MachiDxrSignal.Noisy or MachiDxrSignal.Depth or MachiDxrSignal.ReflectionMotion || IsRayReconstructionGuide(i)
                : !IsRayReconstructionGuide(i);
            if (used && _signals[i] is { } signal)
                barriers[count++] = ResourceBarrier.BarrierTransition(signal, before, after);
        }
        list.ResourceBarrier(barriers[..count]);
    }

    private void Resolve(ID3D12GraphicsCommandList4 list, int frameSlot, ID3D12PipelineState pipeline, int output, int width, int height)
    {
        list.SetDescriptorHeaps(_descriptors);
        list.SetComputeRootSignature(_resolveRoot);
        // The first resolve writes the intermediate as UAV. Bind its SRV only in the scale pass.
        if (pipeline == _resolveScale)
            list.SetComputeRootDescriptorTable(1, Gpu(frameSlot, ResolveScaledSource));
        else if (pipeline == _resolveRaw)
            list.SetComputeRootDescriptorTable(2, Gpu(frameSlot, ResolveRawSource));
        else
            list.SetComputeRootDescriptorTable(0, Gpu(frameSlot, ResolveStart));
        list.SetComputeRootDescriptorTable(3, Gpu(frameSlot, output));
        list.SetPipelineState(pipeline);
        list.Dispatch((uint)(width + 7) / 8, (uint)(height + 7) / 8, 1);
    }

    private bool Denoise(ID3D12GraphicsCommandList commands, int slot, in MachiCamera camera, in MachiCamera previous,
        bool reset, Vector2 jitter, float seconds)
    {
        NativeNrd.Frame frame = new()
        {
            WorldToView = camera.View, WorldToViewPrevious = previous.View, ViewToClip = camera.Projection,
            ViewToClipPrevious = previous.Projection,
            Jitter = jitter, PreviousJitter = reset ? jitter : _previousJitter,
            FrameIndex = _frameIndex, FrameSlot = (uint)slot, Reset = reset ? 1u : 0,
            Milliseconds = Math.Clamp(seconds * 1000, .01f, 200), DenoisingRange = 200,
            MaxAccumulatedFrames = camera.ViewProjection == previous.ViewProjection ? NativeNrd.DefaultMaxAccumulatedFrames
                : NativeNrd.MotionMaxAccumulatedFrames
        };
        frame.Color = Signal(MachiDxrSignal.Diffuse).NativePointer;
        frame.NormalRoughness = Signal(MachiDxrSignal.Normal).NativePointer;
        frame.ViewZ = Signal(MachiDxrSignal.ViewZ).NativePointer;
        frame.Motion = Signal(MachiDxrSignal.Motion).NativePointer;
        frame.Output = _denoised[0].NativePointer;
        frame.DiffuseSh = Signal(MachiDxrSignal.DiffuseSh).NativePointer;
        frame.DiffuseShOutput = _denoised[1].NativePointer;
        frame.Specular = Signal(MachiDxrSignal.Specular).NativePointer;
        frame.SpecularOutput = _denoised[2].NativePointer;
        frame.SpecularSh = Signal(MachiDxrSignal.SpecularSh).NativePointer;
        frame.SpecularShOutput = _denoised[3].NativePointer;
        DenoiserResult = NativeNrd.Execute(_nrd[0], commands.NativePointer, &frame);
        if (DenoiserResult != 0)
        {
            DenoiserAvailable = false;
            return false;
        }
        for (int i = 0; i < 2; i++)
        {
            int start = (int)(i == 0 ? MachiDxrSignal.Reflection : MachiDxrSignal.Transmission);
            frame.Color = _signals[start].NativePointer;
            frame.NormalRoughness = _signals[start + 1].NativePointer;
            frame.ViewZ = _signals[start + 2].NativePointer;
            frame.Motion = _signals[start + 3].NativePointer;
            frame.Output = _denoised[4 + i].NativePointer;
            frame.DiffuseSh = frame.DiffuseShOutput = frame.Specular = frame.SpecularOutput = 0;
            frame.SpecularSh = frame.SpecularShOutput = 0;
            DenoiserDispatchCount += NativeNrd.DispatchCount(_nrd[i]);
            DenoiserResult = NativeNrd.Execute(_nrd[1 + i], commands.NativePointer, &frame);
            if (DenoiserResult != 0)
            {
                DenoiserAvailable = false;
                return false;
            }
        }
        return true;
    }

    private void DestroyDenoisers()
    {
        for (int i = 0; i < _nrd.Length; i++)
        {
            if (_nrd[i] == 0) continue;
            NativeNrd.Destroy(_nrd[i]);
            _nrd[i] = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The caller drains the queue before disposal or replacing scene resources.
        ReleaseSizeResources();
        if (_mapped != null) _constantBuffer.Unmap(0);
        ReleaseScene();
        foreach (var commands in _commandLists) commands?.Dispose();
        for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
        _device.Dispose();
    }
}
