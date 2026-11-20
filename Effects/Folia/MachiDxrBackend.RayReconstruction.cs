using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class MachiDxrBackend
{
    private const string NativeLibrary = "Spectrum.Reconstruction";
    private nint _rayReconstruction;
    private bool _rayReconstructionChecked;
    private TemporalReconstruction? _superResolution;
    private bool _superResolutionAttempted, _superResolutionFailed;
    private uint _jitterIndex;

    internal int RayReconstructionResult { get; private set; }
    internal uint RayReconstructionDispatchCount { get; private set; }
    internal int SuperResolutionResult { get; private set; }
    internal uint SuperResolutionDispatchCount { get; private set; }
    internal ReconstructionMode SuperResolutionActive { get; private set; }
    internal int DenoiserResult { get; private set; }
    internal uint DenoiserDispatchCount { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RayReconstructionFrame
    {
        public nint Color, Depth, Motion, Reactive, Output;
        public uint Width, Height;
        public float JitterX, JitterY, Milliseconds;
        public uint Reset;
        public float NearPlane, FarPlane, VerticalFieldOfView, Padding;
        public nint DiffuseAlbedo, SpecularAlbedo, NormalRoughness, SpecularHitDistance, ReflectionMotion;
        public Matrix4x4 WorldToView, ViewToClip;
    }

    private void InitializeRayReconstruction()
    {
        _rayReconstructionChecked = true;
        try
        {
            RayReconstructionAvailable = _width >= 32 && _height >= 32
                && QueryRayReconstruction(_device.NativePointer) != 0;
        }
        catch (DllNotFoundException exception) { RayReconstructionResult = exception.HResult; }
        catch (EntryPointNotFoundException exception) { RayReconstructionResult = exception.HResult; }
    }

    private static bool IsRayReconstructionGuide(int index) => (MachiDxrSignal)index is MachiDxrSignal.Albedo or MachiDxrSignal.SpecularAlbedo
        or MachiDxrSignal.LinearNormal or MachiDxrSignal.SpecularDistance or MachiDxrSignal.ReconstructionDepth
        or MachiDxrSignal.ReconstructionMotion;

    private bool PrepareRayReconstruction(ID3D12GraphicsCommandList commands, bool requested)
    {
        if (!requested) return false;
        if (!_rayReconstructionChecked) InitializeRayReconstruction();
        if (!RayReconstructionAvailable) return false;
        if (_rayReconstruction == 0)
        {
            try
            {
                for (int i = 0; i < TraceOutputs; i++)
                    if (IsRayReconstructionGuide(i) && _signals[i] == null)
                    {
                        _signals[i] = Texture(SignalFormat(i));
                        for (int slot = 0; slot < FrameSlots; slot++) BindTraceOutput(slot, i);
                    }
                RayReconstructionResult = CreateRayReconstruction(_device.NativePointer, commands.NativePointer,
                    6, 0, (uint)_renderSize.OutputWidth, (uint)_renderSize.OutputHeight, (uint)_width, (uint)_height, out _rayReconstruction);
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                RayReconstructionResult = exception.HResult;
            }
            if (RayReconstructionResult != 0) RayReconstructionAvailable = false;
        }
        return RayReconstructionAvailable;
    }

    private bool TryRayReconstruction(ID3D12GraphicsCommandList commands, in Camera camera,
        ID3D12Resource output, in TemporalFrame temporal, float seconds)
    {
        RayReconstructionFrame frame = new()
        {
            Color = Signal(MachiDxrSignal.Noisy).NativePointer,
            Depth = Signal(MachiDxrSignal.ReconstructionDepth).NativePointer,
            Motion = Signal(MachiDxrSignal.ReconstructionMotion).NativePointer,
            Output = output.NativePointer, Width = (uint)_width, Height = (uint)_height,
            JitterX = temporal.JitterX, JitterY = temporal.JitterY,
            Milliseconds = Math.Clamp(seconds * 1000, .01f, 200),
            Reset = temporal.Reset ? 1u : 0, NearPlane = .1f, FarPlane = 200,
            DiffuseAlbedo = Signal(MachiDxrSignal.Albedo).NativePointer, SpecularAlbedo = Signal(MachiDxrSignal.SpecularAlbedo).NativePointer,
            NormalRoughness = Signal(MachiDxrSignal.LinearNormal).NativePointer, SpecularHitDistance = Signal(MachiDxrSignal.SpecularDistance).NativePointer, ReflectionMotion = Signal(MachiDxrSignal.ReflectionMotion).NativePointer,
            WorldToView = camera.View, ViewToClip = camera.Projection
        };
        RayReconstructionResult = ExecuteRayReconstruction(_rayReconstruction, commands.NativePointer, &frame);
        if (RayReconstructionResult != 0)
        {
            RayReconstructionAvailable = false;
            return false;
        }
        RayReconstructionDispatchCount++;
        return true;
    }

    private bool PrepareSuperResolution(ID3D12GraphicsCommandList commands)
    {
        if (_superResolutionMode < ReconstructionMode.XeSS || _superResolutionFailed) return false;
        if (!_superResolutionAttempted)
        {
            _superResolutionAttempted = true;
            try { _superResolution = TemporalReconstruction.Create(_device, commands, _superResolutionMode, _preset, _renderSize, reactiveMask: false); }
            catch (ReconstructionSizeException) { SuperResolutionResult = -30; }
            catch (Exception error)
            {
                SuperResolutionResult = error.HResult;
                App.WriteCrashLog("Machi super resolution initialization", error.Message, error);
            }
        }
        return _superResolution != null;
    }

    private bool TrySuperResolution(ID3D12GraphicsCommandList commands, ID3D12Resource output, in TemporalFrame temporal, float seconds)
    {
        var depth = Signal(_superResolutionMode == ReconstructionMode.Fsr ? MachiDxrSignal.ReconstructionDepth : MachiDxrSignal.Depth);
        SuperResolutionResult = _superResolution!.Execute(commands, _resolveInput!, depth,
            Signal(MachiDxrSignal.Motion), null, output, new TemporalCamera(.1f, 200, MathF.PI / 3), temporal, seconds);
        if (SuperResolutionResult != 0)
        {
            _superResolutionFailed = true;
            App.WriteCrashLog("Machi super resolution dispatch", $"{_superResolutionMode} failed ({SuperResolutionResult}).", null);
            return false;
        }
        SuperResolutionActive = _superResolutionMode;
        SuperResolutionDispatchCount++;
        return true;
    }

    private void DisposeSuperResolution()
    {
        _superResolution?.Dispose();
        _superResolution = null;
        _superResolutionAttempted = _superResolutionFailed = false;
        SuperResolutionResult = 0;
        SuperResolutionActive = ReconstructionMode.Off;
        _jitterIndex = 0;
    }

    private void DisposeRayReconstruction()
    {
        if (_rayReconstruction == 0) return;
        DestroyRayReconstruction(_rayReconstruction);
        _rayReconstruction = 0;
    }

    [LibraryImport(NativeLibrary, EntryPoint = "ReconstructionRayReconstructionAvailable")]
    private static partial int QueryRayReconstruction(nint device);
    [LibraryImport(NativeLibrary, EntryPoint = "ReconstructionCreateV2")]
    private static partial int CreateRayReconstruction(nint device, nint commands, int mode, int preset,
        uint width, uint height, uint inputWidth, uint inputHeight, out nint context);
    [LibraryImport(NativeLibrary, EntryPoint = "ReconstructionExecuteDlssd")]
    private static partial int ExecuteRayReconstruction(nint context, nint commands, RayReconstructionFrame* frame);
    [LibraryImport(NativeLibrary, EntryPoint = "ReconstructionDestroy")]
    private static partial void DestroyRayReconstruction(nint context);
}
