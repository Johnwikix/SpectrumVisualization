using System.Numerics;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Rendering;

/// <summary>Declares the full NRD RELAX dispatch adapter and its borrowed-resource frame contract.</summary>
internal static unsafe partial class NativeNrd
{
    // Preserve stationary convergence while limiting low-SPP history lag during camera motion.
    internal const uint DefaultMaxAccumulatedFrames = 30, MotionMaxAccumulatedFrames = 8;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Frame
    {
        internal nint Color, NormalRoughness, ViewZ, Motion, Output;
        // System.Numerics row-vector matrices have the same bytes as NRD column-major column-vector matrices.
        internal Matrix4x4 WorldToView, WorldToViewPrevious, ViewToClip, ViewToClipPrevious;
        internal Vector2 Jitter, PreviousJitter;
        internal float Milliseconds;
        internal uint FrameIndex, Reset, FrameSlot;
        internal nint Specular, SpecularOutput;
        internal float DenoisingRange;
        internal uint MaxAccumulatedFrames;
        internal nint DiffuseSh, SpecularSh, DiffuseShOutput, SpecularShOutput;
    }

    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdAvailable")]
    internal static partial int Available();
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdDiagnostic")]
    internal static partial nint Diagnostic();
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdCreate")]
    internal static partial int Create(nint device, uint width, uint height, out nint context);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdCreatePbr")]
    internal static partial int CreatePbr(nint device, uint width, uint height, out nint context);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdCreateOpticalPair")]
    internal static partial int CreateOpticalPair(nint device, uint width, uint height, out nint reflection, out nint transmission);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdCreateRadiance")]
    internal static partial int CreateRadiance(nint device, uint width, uint height, out nint context);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdExecute")]
    internal static partial int Execute(nint context, nint commands, Frame* frame);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdDispatchCount")]
    internal static partial uint DispatchCount(nint context);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdMaxAccumulatedFrames")]
    internal static partial uint MaxAccumulatedFrames(nint context);
    [LibraryImport("Spectrum.Reconstruction", EntryPoint = "NrdDestroy")]
    internal static partial void Destroy(nint context);
}
