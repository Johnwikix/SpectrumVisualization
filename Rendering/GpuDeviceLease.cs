using ComputeSharp;
using System;

namespace WinExSpectrumTest.Rendering;

/// <summary>Keeps the shared ComputeSharp device alive while warmup or a renderer owns resources.</summary>
internal sealed class GpuDeviceLease : IDisposable
{
    private static readonly object Gate = new();
    private static GraphicsDevice? _device;
    private static int _owners;
    private bool _disposed;
    internal GraphicsDevice Device { get; }

    private GpuDeviceLease(GraphicsDevice device) => Device = device;

    internal static GpuDeviceLease Acquire()
    {
        lock (Gate)
        {
            _device ??= GraphicsDevice.GetDefault();
            _owners++;
            return new GpuDeviceLease(_device);
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (--_owners != 0) return;
            var device = _device;
            _device = null;
            device?.Dispose();
        }
    }
}
