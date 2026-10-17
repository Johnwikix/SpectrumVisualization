using System;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Large, explicitly owned CPU pixels. Atlas leases keep spans alive across preparation and upload.</summary>
internal sealed unsafe class FoliaPixelBuffer : SafeHandle
{
    internal int Length { get; }

    internal FoliaPixelBuffer(int length) : base(0, true)
    {
        Length = length;
        void* pixels = NativeMemory.AllocZeroed((nuint)length);
        if (pixels == null) throw new OutOfMemoryException();
        SetHandle((nint)pixels);
    }

    public override bool IsInvalid => handle == 0;
    internal Span<byte> Span => IsClosed ? throw new ObjectDisposedException(nameof(FoliaPixelBuffer))
        : new((void*)handle, Length);

    protected override bool ReleaseHandle()
    {
        NativeMemory.Free((void*)handle);
        return true;
    }
}
