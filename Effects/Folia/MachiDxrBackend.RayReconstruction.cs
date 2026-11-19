using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class MachiDxrBackend
{
    private const string NativeLibrary = "Spectrum.Reconstruction";
    private nint _rayReconstruction;
    private bool _rayReconstructionChecked;
    private readonly bool[] _rrBound = new bool[FrameSlots];

    internal int RayReconstructionResult { get; private set; }
    internal uint RayReconstructionDispatchCount { get; private set; }
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

    private bool PrepareRayReconstruction(int frameSlot, bool requested)
    {
        if (!requested) return false;
        if (!_rayReconstructionChecked) InitializeRayReconstruction();
        if (!RayReconstructionAvailable) return false;
        if (_rrOutput is null)
        {
            try
            {
                for (int i = 0; i < TraceOutputs; i++)
                    if (IsRayReconstructionGuide(i))
                        _signals[i] = Texture(SignalFormat(i));
                _rrOutput = Texture(HdrFormat);
            }
            catch (SharpGen.Runtime.SharpGenException exception)
            {
                for (int i = 0; i < TraceOutputs; i++)
                {
                    if (!IsRayReconstructionGuide(i)) continue;
                    _signals[i]?.Dispose();
                    _signals[i] = null!;
                }
                RayReconstructionAvailable = false;
                RayReconstructionResult = exception.HResult;
                return false;
            }
        }

        if (!_rrBound[frameSlot])
        {
            for (int i = 0; i < TraceOutputs; i++)
                if (IsRayReconstructionGuide(i))
                    BindTraceOutput(frameSlot, i);
            _device.CreateShaderResourceView(_rrOutput, null, Cpu(frameSlot, TraceOutputs + 2 + 12));
            _rrBound[frameSlot] = true;
        }
        return true;
    }

    private bool TryRayReconstruction(ID3D12GraphicsCommandList commands, in Camera camera, bool reset,
        float seconds, bool requested)
    {
        if (!requested) return false;
        if (_rayReconstruction == 0)
        {
            RayReconstructionResult = CreateRayReconstruction(_device.NativePointer, commands.NativePointer,
                6, 0, (uint)_width, (uint)_height, (uint)_width, (uint)_height, out _rayReconstruction);
            if (RayReconstructionResult != 0)
            {
                RayReconstructionAvailable = false;
                return false;
            }
        }
        RayReconstructionFrame frame = new()
        {
            Color = Signal(MachiDxrSignal.Noisy).NativePointer,
            Depth = Signal(MachiDxrSignal.ReconstructionDepth).NativePointer,
            Motion = Signal(MachiDxrSignal.ReconstructionMotion).NativePointer,
            Output = _rrOutput.NativePointer, Width = (uint)_width, Height = (uint)_height,
            Milliseconds = Math.Clamp(seconds * 1000, .01f, 200),
            Reset = reset ? 1u : 0, NearPlane = .1f, FarPlane = 200,
            DiffuseAlbedo = Signal(MachiDxrSignal.Albedo).NativePointer, SpecularAlbedo = Signal(MachiDxrSignal.SpecularAlbedo).NativePointer,
            NormalRoughness = Signal(MachiDxrSignal.LinearNormal).NativePointer, SpecularHitDistance = Signal(MachiDxrSignal.SpecularDistance).NativePointer, ReflectionMotion = Signal(MachiDxrSignal.ReflectionMotion).NativePointer,
            WorldToView = camera.View, ViewToClip = camera.Projection
        };
        commands.ResourceBarrierTransition(_rrOutput, Read, Write);
        RayReconstructionResult = ExecuteRayReconstruction(_rayReconstruction, commands.NativePointer, &frame);
        commands.ResourceBarrierTransition(_rrOutput, Write, Read);
        if (RayReconstructionResult != 0)
        {
            RayReconstructionAvailable = false;
            return false;
        }
        RayReconstructionDispatchCount++;
        return true;
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
