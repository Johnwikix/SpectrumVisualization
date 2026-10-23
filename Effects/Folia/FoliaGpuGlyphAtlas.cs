using WinExSpectrumTest.Service;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed class FoliaCpuGlyphsRequiredException() : Exception("Missing glyph tiles must be prepared by the CPU worker.");

/// <summary>Render-worker-owned resident array. Existing pages survive growth via GPU copies; UVs never move.</summary>
internal sealed unsafe class FoliaGpuGlyphAtlas : IDisposable
{
    private readonly ID3D12Device _device;
    private readonly FoliaGlyphStore _store;
    private ID3D12RootSignature? _root;
    private ID3D12PipelineState? _generate, _correct, _horizontal, _vertical;
    private ID3D12Resource? _oldDistance, _oldShadow;
    private int _oldPages;
    private bool _growthPending, _growthRecorded;
    internal ID3D12Resource? Distance { get; private set; }
    internal ID3D12Resource? Shadow { get; private set; }
    internal int Pages { get; private set; }
    internal int Version { get; private set; }
    internal long Generated { get; private set; }
    internal long CpuUploaded { get; private set; }
    internal long PixelUploadBytes { get; private set; }
    internal long OutlineUploadBytes { get; private set; }

    internal FoliaGpuGlyphAtlas(ID3D12Device device, FoliaGlyphStore store, bool forceCpu)
    {
        _device = device;
        _store = store;
        if (forceCpu) return;
        try
        {
            foreach (var format in new[] { Format.R16G16B16A16_Float, Format.R8_UNorm, Format.R32_Float, Format.R32G32B32A32_Float })
            {
                var support = new FeatureDataFormatSupport { Format = format };
                if (!device.CheckFeatureSupport(Vortice.Direct3D12.Feature.FormatSupport, ref support)) return;
                if ((support.Support2 & FormatSupport2.UnorderedAccessViewTypedStore) == 0) return;
            }
            _root = device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
                [new RootParameter1(new RootConstants(0, 0, 4), ShaderVisibility.All),
                 new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(0, 0), ShaderVisibility.All),
                 new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(1, 0), ShaderVisibility.All),
                 new RootParameter1(RootParameterType.ShaderResourceView, new RootDescriptor1(2, 0), ShaderVisibility.All),
                 new RootParameter1(new RootDescriptorTable1([
                     new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 3, 3, 0, 0, DescriptorRangeFlags.DataVolatile),
                     new DescriptorRange1(DescriptorRangeType.UnorderedAccessView, 4, 0, 0, 3, DescriptorRangeFlags.DataVolatile)]), ShaderVisibility.All)]));
            string source = FoliaShaderLibrary.Load("GlyphGenerator");
            _generate = Pipeline(source, "GenerateMtsdf");
            _correct = Pipeline(source, "CorrectMtsdf");
            _horizontal = Pipeline(source, "ShadowHorizontal");
            _vertical = Pipeline(source, "ShadowVertical");
            store.SetGpuEnabled(true);
        }
        catch (Exception error)
        {
            store.SetGpuEnabled(false);
            App.WriteCrashLog("Folia GPU glyphs; using CPU fallback", error.Message, error);
        }
    }

    private ID3D12PipelineState Pipeline(string source, string entry) => GpuPipelineCache.Create(_device,
        new ComputePipelineStateDescription { RootSignature = _root!, ComputeShader = ShaderCompiler.Compile(source, entry, "cs_5_0") },
        "Folia/glyph-generator/root-v5-fp32-correction/" + entry);

    internal void FinishFrame()
    {
        if (!_growthRecorded) return;
        _oldDistance?.Dispose();
        _oldShadow?.Dispose();
        _oldDistance = _oldShadow = null;
        _growthRecorded = false;
    }

    internal FoliaGlyphBatch? Prepare(FoliaCachedGlyph[] entries)
    {
        var missing = new List<FoliaCachedGlyph>(12);
        int pixels = 0, requiredPages = Pages;
        foreach (var entry in entries)
        {
            if (entry.Resident) continue;
            if (missing.Count > 0 && (missing.Count == 12 || pixels + entry.Rect.Width * entry.Rect.Height > 240000)) break;
            if (!_store.GpuEnabled && entry.CpuTile == null) throw new FoliaCpuGlyphsRequiredException();
            missing.Add(entry);
            pixels += entry.Rect.Width * entry.Rect.Height;
            requiredPages = Math.Max(requiredPages, entry.Rect.Page + 1);
        }
        if (missing.Count == 0) return null;
        if (_growthPending) return null; // A cancelled scene still leaves its page preservation copy to be recorded.
        if (requiredPages > Pages) Grow(requiredPages);
        return new(this, missing.ToArray());
    }

    private ID3D12Resource Array(Format format, int pages)
        => _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(format, FoliaGlyphStore.PageSize, FoliaGlyphStore.PageSize, (ushort)pages, 1, 1, 0,
                _store.GpuEnabled ? ResourceFlags.AllowUnorderedAccess : ResourceFlags.None), ResourceStates.CopyDest);

    private void Grow(int required)
    {
        int count = Math.Max(1, Pages);
        while (count < required) count *= 2;
        ID3D12Resource? distance = null, shadow = null;
        try
        {
            distance = Array(Format.R16G16B16A16_Float, count);
            shadow = Array(Format.R8_UNorm, count);
        }
        catch (global::System.Exception caughtException) { AppLog.Caught(caughtException); distance?.Dispose(); shadow?.Dispose(); throw; }
        _oldDistance = Distance;
        _oldShadow = Shadow;
        _oldPages = Pages;
        Distance = distance;
        Shadow = shadow;
        Pages = count;
        _growthPending = true;
        Version++;
    }

    internal void RecordGrowth(ID3D12GraphicsCommandList commands)
    {
        if (!_growthPending) return;
        if (_oldDistance != null)
        {
            commands.ResourceBarrierTransition(_oldDistance, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
            commands.ResourceBarrierTransition(_oldShadow!, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
            for (uint page = 0; page < _oldPages; page++)
            {
                commands.CopyTextureRegion(new TextureCopyLocation(Distance!, page), 0, 0, 0, new TextureCopyLocation(_oldDistance, page));
                commands.CopyTextureRegion(new TextureCopyLocation(Shadow!, page), 0, 0, 0, new TextureCopyLocation(_oldShadow!, page));
            }
        }
        commands.ResourceBarrierTransition(Distance!, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        commands.ResourceBarrierTransition(Shadow!, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        _growthPending = false;
        _growthRecorded = true;
    }

    internal static ShaderResourceViewDescription Srv(ID3D12Resource texture) => new()
    {
        Format = texture.Description.Format, ViewDimension = ShaderResourceViewDimension.Texture2DArray,
        Shader4ComponentMapping = ShaderComponentMapping.Default,
        Texture2DArray = new Texture2DArrayShaderResourceView { MipLevels = 1, ArraySize = texture.Description.DepthOrArraySize }
    };

    public void Dispose()
    {
        _oldDistance?.Dispose(); _oldShadow?.Dispose();
        Distance?.Dispose(); Shadow?.Dispose();
        _generate?.Dispose(); _correct?.Dispose(); _horizontal?.Dispose(); _vertical?.Dispose(); _root?.Dispose();
        Distance = Shadow = _oldDistance = _oldShadow = null;
    }

    /// <summary>One bounded cold batch. Its entries remain pinned by the owning scene until the preceding-frame fence completes.</summary>
    internal sealed class FoliaGlyphBatch : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Job { internal uint X, Y, Width, Height, Page, FirstContour, ContourCount, FirstEdge; internal Vector4 Offset; }
        private readonly record struct CpuCopy(FoliaCachedGlyph Entry, ulong DistanceOffset, ulong ShadowOffset, uint DistancePitch, uint ShadowPitch);
        private readonly FoliaGpuGlyphAtlas _owner;
        private readonly FoliaCachedGlyph[] _entries;
        private readonly CpuCopy[] _copies;
        private ID3D12Resource? _edges, _contours, _jobs, _pixels, _scratch, _raw;
        private ID3D12DescriptorHeap? _heap;
        private uint _width, _height, _jobCount;
        internal bool Submitted { get; private set; }
        private bool _completed;

        internal FoliaGlyphBatch(FoliaGpuGlyphAtlas owner, FoliaCachedGlyph[] entries)
        {
            _owner = owner;
            _entries = entries;
            var copies = new List<CpuCopy>();
            var edges = new List<FoliaGpuEdge>();
            var contours = new List<FoliaGpuContour>();
            var jobs = new List<Job>();
            ulong size = 0;
            try
            {
                foreach (var entry in entries)
                {
                    var r = entry.Rect;
                    if (entry.CpuTile != null)
                    {
                        size = Align(size, 512); ulong distance = size;
                        uint dp = (uint)Align((ulong)r.Width * 8, 256); size += dp * (ulong)r.Height;
                        size = Align(size, 512); ulong shadow = size;
                        uint sp = (uint)Align((ulong)r.Width, 256); size += sp * (ulong)r.Height;
                        copies.Add(new(entry, distance, shadow, dp, sp));
                        continue;
                    }
                    var source = entry.Source;
                    jobs.Add(new Job { X = (uint)r.X, Y = (uint)r.Y, Width = (uint)r.Width, Height = (uint)r.Height,
                        Page = (uint)r.Page, FirstContour = (uint)contours.Count, ContourCount = (uint)source.Outline.Contours.Length,
                        FirstEdge = (uint)edges.Count, Offset = new(source.X, source.Y, source.Outline.Flip ? -1 : 1, 0) });
                    edges.AddRange(source.Outline.Edges);
                    contours.AddRange(source.Outline.Contours);
                    _width = Math.Max(_width, (uint)r.Width); _height = Math.Max(_height, (uint)r.Height);
                }
                _copies = copies.ToArray();
                if (size != 0)
                {
                    _pixels = owner._device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
                        ResourceDescription.Buffer(size), ResourceStates.GenericRead);
                    byte* target = _pixels.Map<byte>(0);
                    foreach (var copy in _copies)
                    {
                        var r = copy.Entry.Rect; byte[] tile = copy.Entry.CpuTile!;
                        for (int y = 0; y < r.Height; y++)
                        {
                            tile.AsSpan(y * r.Width * 8, r.Width * 8).CopyTo(new Span<byte>(target + copy.DistanceOffset + y * copy.DistancePitch, r.Width * 8));
                            tile.AsSpan(r.Width * r.Height * 8 + y * r.Width, r.Width).CopyTo(new Span<byte>(target + copy.ShadowOffset + y * copy.ShadowPitch, r.Width));
                        }
                    }
                    _pixels.Unmap(0);
                }
                _jobCount = (uint)jobs.Count;
                if (_jobCount != 0)
                {
                    _edges = Buffer<FoliaGpuEdge>(edges.ToArray());
                    _contours = Buffer<FoliaGpuContour>(contours.ToArray());
                    _jobs = Buffer<Job>(jobs.ToArray());
                    _scratch = owner._device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                        ResourceDescription.Texture2D(Format.R32_Float, _width, _height, (ushort)_jobCount, 1, 1, 0, ResourceFlags.AllowUnorderedAccess), ResourceStates.UnorderedAccess);
                    _raw = owner._device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                        ResourceDescription.Texture2D(Format.R32G32B32A32_Float, _width, _height, (ushort)_jobCount, 1, 1, 0, ResourceFlags.AllowUnorderedAccess), ResourceStates.UnorderedAccess);
                    _heap = owner._device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 7, DescriptorHeapFlags.ShaderVisible));
                    var handle = _heap.GetCPUDescriptorHandleForHeapStart();
                    int step = (int)owner._device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
                    owner._device.CreateShaderResourceView(owner.Distance!, Srv(owner.Distance!), handle);
                    owner._device.CreateShaderResourceView(_scratch, Srv(_scratch), handle + step);
                    owner._device.CreateShaderResourceView(_raw, Srv(_raw), handle + step * 2);
                    Uav(owner.Distance!, handle + step * 3);
                    Uav(owner.Shadow!, handle + step * 4);
                    Uav(_scratch, handle + step * 5);
                    Uav(_raw, handle + step * 6);
                    owner.OutlineUploadBytes += edges.Count * 80L + contours.Count * 16L + jobs.Count * 48L;
                }
            }
            catch (global::System.Exception caughtException2) { AppLog.Caught(caughtException2); Dispose(); throw; }
        }

        private static ulong Align(ulong value, ulong alignment) => (value + alignment - 1) & ~(alignment - 1);
        private ID3D12Resource Buffer<T>(ReadOnlySpan<T> values) where T : unmanaged
        {
            var resource = _owner._device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
                ResourceDescription.Buffer((ulong)Math.Max(16, values.Length * sizeof(T))), ResourceStates.GenericRead);
            values.CopyTo(new Span<T>(resource.Map<T>(0), values.Length));
            resource.Unmap(0);
            return resource;
        }
        private void Uav(ID3D12Resource resource, CpuDescriptorHandle handle) => _owner._device.CreateUnorderedAccessView(resource, null,
            new UnorderedAccessViewDescription { Format = resource.Description.Format, ViewDimension = UnorderedAccessViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayUnorderedAccessView { ArraySize = resource.Description.DepthOrArraySize } }, handle);

        internal void Record(ID3D12GraphicsCommandList commands)
        {
            if (Submitted) return;
            var o = _owner;
            if (_copies.Length > 0)
            {
                commands.ResourceBarrierTransition(o.Distance!, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
                commands.ResourceBarrierTransition(o.Shadow!, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
                foreach (var copy in _copies)
                {
                    var r = copy.Entry.Rect;
                    commands.CopyTextureRegion(new TextureCopyLocation(o.Distance!, (uint)r.Page), (uint)r.X, (uint)r.Y, 0,
                        Footprint(_pixels!, copy.DistanceOffset, Format.R16G16B16A16_Float, r, copy.DistancePitch));
                    commands.CopyTextureRegion(new TextureCopyLocation(o.Shadow!, (uint)r.Page), (uint)r.X, (uint)r.Y, 0,
                        Footprint(_pixels!, copy.ShadowOffset, Format.R8_UNorm, r, copy.ShadowPitch));
                    o.PixelUploadBytes += r.Width * (long)r.Height * 9;
                }
                commands.ResourceBarrierTransition(o.Distance!, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
                commands.ResourceBarrierTransition(o.Shadow!, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
            }
            if (_jobCount > 0)
            {
                commands.SetDescriptorHeaps(_heap!);
                commands.SetComputeRootSignature(o._root!);
                Span<uint> constants = stackalloc uint[4] { _jobCount, BitConverter.SingleToUInt32Bits(FoliaDistanceField.Range), BitConverter.SingleToUInt32Bits(96 * .09f), 26 };
                fixed (uint* p = constants) commands.SetComputeRoot32BitConstants(0, 4, p, 0);
                commands.SetComputeRootShaderResourceView(1, _edges!.GPUVirtualAddress);
                commands.SetComputeRootShaderResourceView(2, _contours!.GPUVirtualAddress);
                commands.SetComputeRootShaderResourceView(3, _jobs!.GPUVirtualAddress);
                commands.ResourceBarrierTransition(o.Distance!, ResourceStates.PixelShaderResource, ResourceStates.UnorderedAccess);
                commands.SetPipelineState(o._generate!);
                commands.SetComputeRootDescriptorTable(4, _heap!.GetGPUDescriptorHandleForHeapStart());
                commands.Dispatch((_width + 7) / 8, (_height + 7) / 8, _jobCount);
                commands.ResourceBarrierTransition(_raw!, ResourceStates.UnorderedAccess, ResourceStates.NonPixelShaderResource);
                commands.SetPipelineState(o._correct!);
                commands.SetComputeRootDescriptorTable(4, _heap!.GetGPUDescriptorHandleForHeapStart());
                commands.Dispatch((_width + 7) / 8, (_height + 7) / 8, _jobCount);
                commands.ResourceBarrierTransition(o.Distance!, ResourceStates.UnorderedAccess, ResourceStates.NonPixelShaderResource);
                commands.SetPipelineState(o._horizontal!);
                commands.SetComputeRootDescriptorTable(4, _heap!.GetGPUDescriptorHandleForHeapStart());
                commands.Dispatch((_width + 7) / 8, (_height + 7) / 8, _jobCount);
                commands.ResourceBarrierTransition(_scratch!, ResourceStates.UnorderedAccess, ResourceStates.NonPixelShaderResource);
                commands.ResourceBarrierTransition(o.Shadow!, ResourceStates.PixelShaderResource, ResourceStates.UnorderedAccess);
                commands.SetPipelineState(o._vertical!);
                commands.SetComputeRootDescriptorTable(4, _heap!.GetGPUDescriptorHandleForHeapStart());
                commands.Dispatch((_width + 7) / 8, (_height + 7) / 8, _jobCount);
                commands.ResourceBarrierTransition(o.Distance!, ResourceStates.NonPixelShaderResource, ResourceStates.PixelShaderResource);
                commands.ResourceBarrierTransition(o.Shadow!, ResourceStates.UnorderedAccess, ResourceStates.PixelShaderResource);
            }
            Submitted = true;
        }

        private static TextureCopyLocation Footprint(ID3D12Resource resource, ulong offset, Format format, FoliaGlyphRect r, uint pitch)
            => new(resource, new PlacedSubresourceFootPrint { Offset = offset, Footprint = new SubresourceFootPrint(format, (uint)r.Width, (uint)r.Height, 1, pitch) });

        internal void CompleteAfterFence()
        {
            if (_completed) return;
            if (!Submitted) throw new InvalidOperationException("The glyph batch was not submitted.");
            _completed = true;
            _owner.Generated += _jobCount;
            _owner.CpuUploaded += _copies.Length;
            foreach (var glyph in _entries) { glyph.Resident = true; glyph.CpuTile = null; }
        }
        public void Dispose()
        {
            _edges?.Dispose(); _contours?.Dispose(); _jobs?.Dispose(); _pixels?.Dispose(); _scratch?.Dispose(); _raw?.Dispose(); _heap?.Dispose();
            _edges = _contours = _jobs = _pixels = _scratch = _raw = null;
            _heap = null;
        }
    }
}
