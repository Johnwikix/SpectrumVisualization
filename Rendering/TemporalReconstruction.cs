using System;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Rendering;

/// <summary>Stable algorithm identifiers shared with the native adapter ABI.</summary>
internal enum ReconstructionMode { Off, Fxaa, Smaa, XeSS, Fsr, Dlss }

/// <summary>Resources and camera state required by temporal reconstruction. Motion is unjittered current-to-previous input pixels.</summary>
internal readonly record struct TemporalFrame(float JitterX, float JitterY, bool Reset);

/// <summary>Perspective camera metadata matching the effect's non-inverted depth buffer.</summary>
internal readonly record struct TemporalCamera(float NearPlane, float FarPlane, float VerticalFieldOfView);

/// <summary>Optional effect contract. Particles and other non-temporal overlays are drawn after reconstruction.</summary>
internal interface ITemporalGpuEffect
{
    /// <summary>Creates or disables temporal-only effect resources at a drained frame boundary.</summary>
    void ConfigureTemporal(bool enabled);
    /// <summary>Supplies jitter and discontinuity state before simulation and scene recording.</summary>
    void SetTemporalFrame(in TemporalFrame frame);
    /// <summary>Gets the current perspective projection used to produce Depth.</summary>
    TemporalCamera Camera { get; }
    /// <summary>Gets non-inverted device depth in [0,1], in NON_PIXEL_SHADER_RESOURCE state after RecordScene.</summary>
    ID3D12Resource Depth { get; }
    /// <summary>Gets unjittered current-to-previous input-pixel velocity, RG16F, in NON_PIXEL_SHADER_RESOURCE state.</summary>
    ID3D12Resource Motion { get; }
    /// <summary>Gets the [0,1] responsive-pixel mask in NON_PIXEL_SHADER_RESOURCE state.</summary>
    ID3D12Resource Reactive { get; }
    /// <summary>Draws output-resolution overlays into the reconstructed linear target.</summary>
    void RecordOverlay(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target);
}

/// <summary>Owns one vendor context on the render thread. No delegate or reflection-based native marshaling.</summary>
internal sealed unsafe partial class TemporalReconstruction : IDisposable
{
    private nint _context;
    private uint _frame;
    private bool _reset = true;
    internal GpuRenderSize Size { get; private set; }

    internal static uint Capabilities(ID3D12Device device)
    {
        try { return ReconstructionCapabilities(device.NativePointer); }
        catch (DllNotFoundException) { return 0; }
        catch (EntryPointNotFoundException) { return 0; }
        catch (BadImageFormatException) { return 0; }
    }

    internal static TemporalReconstruction Create(ID3D12Device device, ID3D12GraphicsCommandList commands,
        ReconstructionMode mode, string quality, int width, int height)
    {
        int q = quality switch { "native" => 0, "balanced" => 2, "performance" => 3, _ => 1 };
        int result = ReconstructionCreate(device.NativePointer, commands.NativePointer, (int)mode, q,
            (uint)width, (uint)height, out nint context, out uint inputWidth, out uint inputHeight);
        if (result != 0) throw new InvalidOperationException($"{mode} initialization failed ({result}).");
        return new TemporalReconstruction { _context = context, Size = new(width, height, (int)inputWidth, (int)inputHeight) };
    }

    internal void Reset() { _reset = true; _frame = 0; }

    internal TemporalFrame BeginFrame(double elapsed)
    {
        if (elapsed > .25 || elapsed <= 0) Reset();
        int phases = Math.Max(8, (int)Math.Ceiling(8.0 * Size.OutputWidth * Size.OutputWidth / ((double)Size.Width * Size.Width)));
        uint sample = _frame % (uint)phases + 1;
        return new(Halton(sample, 2) - .5f, Halton(sample, 3) - .5f, _reset);
    }

    private static float Halton(uint index, uint radix)
    {
        float result = 0, fraction = 1;
        while (index > 0) { fraction /= radix; result += fraction * (index % radix); index /= radix; }
        return result;
    }

    /// <summary>Inputs must be NON_PIXEL_SHADER_RESOURCE and output UAV, preserved across the SDK call.</summary>
    internal void Record(ID3D12GraphicsCommandList commands, ID3D12Resource color, ITemporalGpuEffect effect,
        ID3D12Resource output, in TemporalFrame frame, double elapsed)
    {
        var camera = effect.Camera;
        NativeFrame args = new()
        {
            Color = color.NativePointer, Depth = effect.Depth.NativePointer, Motion = effect.Motion.NativePointer,
            Reactive = effect.Reactive.NativePointer, Output = output.NativePointer,
            Width = (uint)Size.Width, Height = (uint)Size.Height,
            JitterX = frame.JitterX, JitterY = frame.JitterY, Milliseconds = (float)Math.Clamp(elapsed * 1000, 1, 250), Reset = frame.Reset ? 1u : 0,
            NearPlane = camera.NearPlane, FarPlane = camera.FarPlane, VerticalFieldOfView = camera.VerticalFieldOfView
        };
        int result = ReconstructionExecute(_context, commands.NativePointer, &args);
        if (result != 0) throw new InvalidOperationException($"Temporal reconstruction dispatch failed ({result}).");
        _reset = false;
        _frame++;
    }

    public void Dispose()
    {
        nint context = _context;
        _context = 0;
        if (context != 0) ReconstructionDestroy(context);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFrame
    {
        public nint Color, Depth, Motion, Reactive, Output;
        public uint Width, Height;
        public float JitterX, JitterY, Milliseconds;
        public uint Reset;
        public float NearPlane, FarPlane, VerticalFieldOfView, Padding;
    }

    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "ReconstructionCapabilities")]
    private static partial uint ReconstructionCapabilities(nint device);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "ReconstructionCreate")]
    private static partial int ReconstructionCreate(nint device, nint commands, int mode, int quality, uint width, uint height,
        out nint context, out uint inputWidth, out uint inputHeight);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "ReconstructionExecute")]
    private static partial int ReconstructionExecute(nint context, nint commands, NativeFrame* frame);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "ReconstructionDestroy")]
    private static partial void ReconstructionDestroy(nint context);
}
