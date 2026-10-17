using System;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold-only native outline conversion. No managed COM callbacks or reflected marshaling.</summary>
internal static unsafe partial class FoliaDistanceField
{
    internal const float Range = 48;

    [LibraryImport("Spectrum.FoliaText.dll", EntryPoint = "FoliaOutlineSignature")]
    private static partial int GetSignature(nint geometry, byte* digest);

    internal static void Signature(CanvasGeometry geometry, Span<byte> signature)
    {
        if (signature.Length < 32) throw new ArgumentException("An outline signature requires 32 bytes.", nameof(signature));
        nint pointer = ((WinRT.IWinRTObject)geometry).NativeObject.ThisPtr;
        fixed (byte* output = signature)
        {
            int result = GetSignature(pointer, output);
            GC.KeepAlive(geometry);
            Marshal.ThrowExceptionForHR(result);
        }
    }

    [LibraryImport("Spectrum.FoliaText.dll", EntryPoint = "FoliaGenerateMtsdf")]
    private static partial int Generate(nint geometry, int width, int height, float translateX, float translateY, float range, float* pixels);

    [LibraryImport("Spectrum.FoliaText.dll", EntryPoint = "FoliaGetTextMetrics")]
    private static partial int GetMetrics(nint layout, out float baseline, out float xHeight);

    internal static float CanvasMiddleCenter(CanvasTextLayout layout, float em)
    {
        nint pointer = ((WinRT.IWinRTObject)layout).NativeObject.ThisPtr;
        int result = GetMetrics(pointer, out float baseline, out float xHeight);
        GC.KeepAlive(layout);
        Marshal.ThrowExceptionForHR(result);
        var bounds = layout.DrawBounds;
        return ((float)(bounds.Y + bounds.Height * .5) - baseline) / em + xHeight * .5f;
    }

    internal static void Render(CanvasGeometry geometry, int width, int height, float translateX, float translateY, Span<float> pixels)
    {
        if (pixels.Length < checked(width * height * 4))
            throw new ArgumentException("Distance field output is too small.", nameof(pixels));
        // The synchronous native call borrows the native object. Keep the WinRT owner alive until it returns.
        nint pointer = ((WinRT.IWinRTObject)geometry).NativeObject.ThisPtr;
        fixed (float* output = pixels)
        {
            int result = Generate(pointer, width, height, translateX, translateY, Range, output);
            GC.KeepAlive(geometry);
            Marshal.ThrowExceptionForHR(result);
        }
    }
}
