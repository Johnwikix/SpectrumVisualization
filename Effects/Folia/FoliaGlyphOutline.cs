using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.Geometry;

namespace WinExSpectrumTest.Effects.Folia;

[StructLayout(LayoutKind.Sequential)]
internal struct FoliaGpuEdge
{
    internal Vector4 Points, Control, Domains, Meta, Tangents;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FoliaGpuContour
{
    internal int First, Count, Winding, Reserved;
}

/// <summary>Owned immutable native shape; no WinRT object or managed COM callback crosses the worker boundary.</summary>
internal sealed unsafe partial class FoliaGlyphOutline : IDisposable
{
    private sealed class OutlineHandle(nint value) : SafeHandle(0, true)
    {
        public override bool IsInvalid => handle == 0;
        internal void Initialize() => SetHandle(value);
        protected override bool ReleaseHandle() { Release(handle); return true; }
    }
    private readonly OutlineHandle _handle;
    internal byte[] Signature { get; } = new byte[32];
    internal FoliaGpuEdge[] Edges { get; }
    internal FoliaGpuContour[] Contours { get; }
    internal bool EvenOdd { get; }
    internal bool Flip { get; }
    internal bool CanGenerateOnGpu { get; }
    internal bool IsDisposed => _handle.IsClosed;

    [LibraryImport("Spectrum.FoliaText.dll", EntryPoint = "FoliaCreateGlyphOutline")]
    private static partial int Create(nint geometry, out nint handle, out int edges, out int contours, out int flags, byte* digest);
    [LibraryImport("Spectrum.FoliaText.dll", EntryPoint = "FoliaCopyGlyphOutline")]
    private static partial int Copy(OutlineHandle handle, FoliaGpuEdge* edges, int edgeCount, FoliaGpuContour* contours, int contourCount);
    [LibraryImport("Spectrum.FoliaText.dll", EntryPoint = "FoliaReleaseGlyphOutline")]
    private static partial void Release(nint handle);
    [LibraryImport("Spectrum.FoliaText.dll", EntryPoint = "FoliaRenderGlyphOutline")]
    private static partial int Render(OutlineHandle handle, int width, int height, float x, float y, float range, float* pixels);

    internal FoliaGlyphOutline(CanvasGeometry geometry)
    {
        nint pointer = ((WinRT.IWinRTObject)geometry).NativeObject.ThisPtr;
        nint handle;
        int edgeCount, contourCount, flags;
        fixed (byte* digest = Signature)
        {
            int result = Create(pointer, out handle, out edgeCount, out contourCount, out flags, digest);
            GC.KeepAlive(geometry);
            Marshal.ThrowExceptionForHR(result);
        }
        _handle = new(handle);
        _handle.Initialize();
        try
        {
            if (edgeCount is < 0 or > 2048 || contourCount is < 0 or > 32) throw new InvalidOperationException("Invalid native glyph outline dimensions.");
            Edges = new FoliaGpuEdge[edgeCount];
            Contours = new FoliaGpuContour[contourCount];
            fixed (FoliaGpuEdge* edges = Edges)
            fixed (FoliaGpuContour* contours = Contours)
                Marshal.ThrowExceptionForHR(Copy(_handle, edges, edgeCount, contours, contourCount));
            EvenOdd = (flags & 1) != 0;
            Flip = (flags & 4) != 0;
            CanGenerateOnGpu = (flags & 2) == 0;
        }
        catch { _handle.Dispose(); throw; }
    }

    internal void RenderCpu(int width, int height, float x, float y, Span<float> pixels)
    {
        if (pixels.Length < checked(width * height * 4)) throw new ArgumentException("Glyph output is too small.", nameof(pixels));
        fixed (float* target = pixels)
            Marshal.ThrowExceptionForHR(Render(_handle, width, height, x, y, FoliaDistanceField.Range, target));
    }
    public void Dispose() => _handle.Dispose();
}
