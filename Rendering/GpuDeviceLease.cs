using ComputeSharp;
using ComputeSharp.Interop;
using System;
using Vortice.Direct3D12;

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

    internal unsafe ID3D12Device OpenNativeDevice()
    {
        Guid deviceId = new("189819F1-1DB6-4B57-BE54-1821339B85F7");
        void* native = null;
        InteropServices.GetID3D12Device(Device, &deviceId, &native);
        return new ID3D12Device((nint)native);
    }

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
            GpuPipelineCache.ReleaseDeviceCache();
            device?.Dispose();
        }
    }
}
