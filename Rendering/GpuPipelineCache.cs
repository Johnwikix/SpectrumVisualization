using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SharpGen.Runtime;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

/// <summary>Reuses immutable PSOs on the leased device; driver blobs accelerate later processes.</summary>
internal static class GpuPipelineCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<(nint Device, string Key), ID3D12PipelineState> Pipelines = new();
    private static readonly Dictionary<nint, (string Key, bool Persistent)> Drivers = new();
    private static (string Key, bool Persistent) DriverIdentity(ID3D12Device device)
    {
        if (Drivers.TryGetValue(device.NativePointer, out var known)) return known;
        (string, bool) identity;
        try
        {
            using var factory = DXGI.CreateDXGIFactory2<IDXGIFactory4>(false);
            using var adapter = factory.EnumAdapterByLuid<IDXGIAdapter1>(System.Runtime.CompilerServices.Unsafe.BitCast<long, Vortice.Luid>(device.AdapterLuid));
            var info = adapter.Description1;
            bool available = adapter.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out long driver).Success && driver != 0;
            identity = (FormattableString.Invariant($"{device.AdapterLuid:X16}/{info.VendorId:X8}/{info.DeviceId:X8}/{info.SubsystemId:X8}/{info.Revision:X8}/{driver:X16}/{Environment.OSVersion.Version}"), available);
        }
        catch (SharpGenException) { identity = (device.AdapterLuid.ToString(), false); }
        Drivers.Add(device.NativePointer, identity);
        return identity;
    }
    internal static int MemoryHits { get; private set; }
    internal static int DiskHits { get; private set; }
    internal static int Created { get; private set; }

    // layout identifies the root signature and fixed pipeline variant at the call site. Shader edits invalidate automatically.
    internal static unsafe ID3D12PipelineState Create(ID3D12Device device, GraphicsPipelineStateDescription description, string layout)
    {
        lock (Gate)
        {
            var driver = DriverIdentity(device);
            string material = "graphics-pso-v2\n" + driver.Key + "\n" + layout + "\n"
                + Convert.ToHexString(SHA256.HashData(description.VertexShader.Span)) + "\n"
                + Convert.ToHexString(SHA256.HashData(description.PixelShader.Span));
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
            var identity = (device.NativePointer, key);
            if (Pipelines.TryGetValue(identity, out var existing))
            {
                MemoryHits++;
                return existing.QueryInterface<ID3D12PipelineState>();
            }
            string directory = RenderCacheFile.DirectoryFor("PipelineCache");
            byte[]? cached = driver.Persistent ? RenderCacheFile.Read(directory, key, 64 * 1024 * 1024) : null;
            ID3D12PipelineState? pipeline = null;
            if (cached != null)
            {
                fixed (byte* pointer = cached)
                {
                    description.CachedPSO = new CachedPipelineState { CachedBlob = (nint)pointer, CachedBlobSizeInBytes = (nuint)cached.Length };
                    try { pipeline = device.CreateGraphicsPipelineState(description); DiskHits++; }
                    // Cached PSOs are driver-specific. An upgrade or incompatible blob simply rebuilds this pipeline.
                    catch (SharpGenException) { }
                    finally { description.CachedPSO = default; }
                }
            }
            if (pipeline == null)
            {
                pipeline = device.CreateGraphicsPipelineState(description);
                Created++;
                try
                {
                    using var blob = pipeline.CachedBlob;
                    byte[] bytes = new byte[checked((int)(ulong)blob.BufferSize)];
                    Marshal.Copy(blob.BufferPointer, bytes, 0, bytes.Length);
                    if (driver.Persistent) RenderCacheFile.Write(directory, key, bytes);
                }
                catch (SharpGenException) { } // Some drivers do not expose a serializable PSO.
            }
            Pipelines.Add(identity, pipeline.QueryInterface<ID3D12PipelineState>());
            return pipeline;
        }
    }

    internal static void ClearForRebuild()
    {
        lock (Gate)
        {
            ReleaseDeviceCache();
            RenderCacheFile.Clear(RenderCacheFile.DirectoryFor("PipelineCache"));
        }
    }

    /// <summary>Releases cache-owned references. Live effects retain separate COM references;
    /// the last device lease also calls this after every owning renderer has drained.</summary>
    internal static void ReleaseDeviceCache()
    {
        lock (Gate)
        {
            foreach (var pipeline in Pipelines.Values) pipeline.Dispose();
            Pipelines.Clear();
            Drivers.Clear();
        }
    }
}
