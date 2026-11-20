using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class MachiDxrBackend
{
    private sealed class Mesh(uint firstIndex, uint kind, ID3D12Resource blas, ID3D12Resource scratch,
        BuildRaytracingAccelerationStructureInputs inputs)
    {
        internal readonly uint FirstIndex = firstIndex;
        internal readonly uint Kind = kind;
        internal readonly ID3D12Resource Blas = blas, Scratch = scratch;
        internal readonly BuildRaytracingAccelerationStructureInputs Inputs = inputs;
        internal Matrix4x4 Current = Matrix4x4.Identity;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InstanceData
    {
        public Matrix4x4 Current, Previous;
        public uint FirstIndex, Kind, Padding1, Padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Emitter(uint Instance, uint Primitive, uint Alias, float Threshold, float AreaPdf);
    private readonly record struct AliasEntry(uint Alias, float Threshold);

    private Mesh[] _instances = [];
    private readonly List<IDisposable> _sceneOwned = [];
    private ID3D12Resource _vertices = null!, _indices = null!, _verticesUpload = null!, _indicesUpload = null!;
    private ID3D12Resource _tlas = null!, _tlasScratch = null!, _instanceDescriptions = null!, _instanceData = null!;
    private ID3D12Resource _emitters = null!, _emitterPdf = null!;
    private ID3D12Resource _sign = null!, _signUpload = null!;
    private PlacedSubresourceFootPrint _signFootprint;
    private byte* _instanceDescriptionsMapped, _instanceDataMapped;
    private uint _emitterCount;
    private float _emitterWeight;
    private int _initialUploadSlot = -1;
    private BuildRaytracingAccelerationStructureInputs _tlasInputs = null!;

    /// <summary>Replaces static scene assets and invalidates history after the host completes prior submissions.</summary>
    internal void ReplaceScene(ReadOnlySpan<FoliaMachi.Vertex> vertices, ReadOnlySpan<uint> indices,
        ReadOnlySpan<byte> signPixels, int signWidth, int signHeight, int staticIndexCount,
        ReadOnlySpan<FoliaMachi.Actor> actors, ReadOnlySpan<FoliaMachi.Light> lights)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (vertices.IsEmpty || indices.IsEmpty || indices.Length % 3 != 0)
            throw new ArgumentException("Invalid Machi scene geometry.");
        if (signPixels.IsEmpty)
        {
            signPixels = [0, 0, 0, 0];
            signWidth = signHeight = 1;
        }
        if (signWidth <= 0 || signHeight <= 0 || (long)signWidth * signHeight * 4 != signPixels.Length)
            throw new ArgumentException("Invalid Machi sign atlas.");

        ReleaseScene();
        try
        {
            InitializeGeometry(vertices, indices, staticIndexCount, actors);
            InitializeAnalyticLights(lights);
            _sign = KeepScene(_device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.R8G8B8A8_UNorm_SRgb, (uint)signWidth, (uint)signHeight, 1, 1), ResourceStates.CopyDest));
            int pitch = checked((signWidth * 4 + 255) & ~255);
            _signFootprint = new PlacedSubresourceFootPrint
            {
                Footprint = new SubresourceFootPrint(Format.R8G8B8A8_UNorm_SRgb, (uint)signWidth, (uint)signHeight, 1, (uint)pitch)
            };
            _signUpload = SceneBuffer(checked((ulong)pitch * (uint)signHeight));
            var signData = _signUpload.Map<byte>(0);
            for (int y = 0; y < signHeight; y++)
                signPixels.Slice(y * signWidth * 4, signWidth * 4).CopyTo(new Span<byte>(signData + y * pitch, signWidth * 4));
            _signUpload.Unmap(0);
            for (int slot = 0; slot < FrameSlots; slot++)
                _device.CreateShaderResourceView(_sign, null, Cpu(slot, TraceOutputs + 1));
        }
        catch
        {
            ReleaseScene();
            throw;
        }
    }

    private ID3D12Resource KeepScene(ID3D12Resource resource)
    {
        _sceneOwned.Add(resource);
        return resource;
    }

    private ID3D12Resource SceneBuffer(ulong size, HeapType heap = HeapType.Upload, ResourceStates state = ResourceStates.GenericRead,
        ResourceFlags flags = ResourceFlags.None) => KeepScene(CreateBuffer(size, heap, state, flags));

    private ID3D12Resource Upload<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        ID3D12Resource buffer = SceneBuffer((ulong)values.Length * (uint)sizeof(T));
        values.CopyTo(new Span<T>(buffer.Map<T>(0), values.Length));
        buffer.Unmap(0);
        return buffer;
    }

    private (ID3D12Resource Structure, ID3D12Resource Scratch) Acceleration(BuildRaytracingAccelerationStructureInputs input)
    {
        var info = _device.GetRaytracingAccelerationStructurePrebuildInfo(input);
        return (SceneBuffer(info.ResultDataMaxSizeInBytes, HeapType.Default, ResourceStates.RaytracingAccelerationStructure,
                    ResourceFlags.AllowUnorderedAccess),
                SceneBuffer(Math.Max(info.ScratchDataSizeInBytes, info.UpdateScratchDataSizeInBytes), HeapType.Default, Write,
                    ResourceFlags.AllowUnorderedAccess));
    }

    private void InitializeGeometry(ReadOnlySpan<FoliaMachi.Vertex> vertices, ReadOnlySpan<uint> indices,
        int staticIndexCount, ReadOnlySpan<FoliaMachi.Actor> actors)
    {
        if (actors.IsEmpty) staticIndexCount = indices.Length;
        if (staticIndexCount <= 0 || staticIndexCount > indices.Length || staticIndexCount % 3 != 0)
            throw new ArgumentException("Invalid static Machi triangle range.", nameof(staticIndexCount));

        _verticesUpload = Upload(vertices);
        _indicesUpload = Upload(indices);
        _vertices = SceneBuffer((ulong)vertices.Length * (uint)sizeof(FoliaMachi.Vertex), HeapType.Default, ResourceStates.CopyDest);
        _indices = SceneBuffer((ulong)indices.Length * sizeof(uint), HeapType.Default, ResourceStates.CopyDest);
        _instances = new Mesh[actors.Length + 1];
        bool alphaGeometry = false;
        for (int i = 0; i < staticIndexCount && !alphaGeometry; i += 3)
            alphaGeometry = vertices[(int)indices[i]].PositionMaterial.W == 8;
        _instances[0] = CreateMesh(0, (uint)staticIndexCount, (uint)vertices.Length, 0, alphaGeometry);
        uint nextIndex = (uint)staticIndexCount;
        for (int i = 0; i < actors.Length; i++)
        {
            var actor = actors[i];
            if (actor.FirstIndex != nextIndex || actor.IndexCount == 0 || actor.IndexCount % 3 != 0 ||
                actor.IndexCount > indices.Length - (long)nextIndex)
                throw new ArgumentException("Invalid Machi actor triangle range.", nameof(actors));
            _instances[i + 1] = CreateMesh(actor.FirstIndex, actor.IndexCount, (uint)vertices.Length, actor.Kind, actor.Kind is 5 or 6);
            nextIndex += actor.IndexCount;
        }
        if (nextIndex != indices.Length) throw new ArgumentException("Machi actor ranges do not cover the index buffer.", nameof(actors));

        _instanceDescriptions = SceneBuffer(checked((ulong)_instances.Length * 64 * FrameSlots));
        _instanceData = SceneBuffer(checked((ulong)_instances.Length * (uint)sizeof(InstanceData) * FrameSlots));
        _instanceDescriptionsMapped = _instanceDescriptions.Map<byte>(0);
        _instanceDataMapped = _instanceData.Map<byte>(0);
        _tlasInputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.TopLevel,
            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace
                | (_instances.Length > 1 ? RaytracingAccelerationStructureBuildFlags.AllowUpdate : RaytracingAccelerationStructureBuildFlags.None),
            DescriptorsCount = (uint)_instances.Length,
            Layout = ElementsLayout.Array,
            InstanceDescriptions = _instanceDescriptions.GPUVirtualAddress
        };
        (_tlas, _tlasScratch) = Acceleration(_tlasInputs);
        InitializeEmitters(vertices, indices);
    }

    private Mesh CreateMesh(uint firstIndex, uint count, uint vertexCount, uint kind, bool alphaGeometry)
    {
        var inputs = new BuildRaytracingAccelerationStructureInputs
        {
            Type = RaytracingAccelerationStructureType.BottomLevel,
            Flags = RaytracingAccelerationStructureBuildFlags.PreferFastTrace,
            DescriptorsCount = 1,
            Layout = ElementsLayout.Array,
            GeometryDescriptions = [new RaytracingGeometryDescription(new RaytracingGeometryTrianglesDescription(
                new GpuVirtualAddressAndStride(_vertices.GPUVirtualAddress, (ulong)sizeof(FoliaMachi.Vertex)),
                Format.R32G32B32_Float, vertexCount, transform3x4: 0,
                indexBuffer: _indices.GPUVirtualAddress + firstIndex * sizeof(uint), indexFormat: Format.R32_UInt, indexCount: count),
                alphaGeometry ? RaytracingGeometryFlags.None : RaytracingGeometryFlags.Opaque)]
        };
        var (blas, scratch) = Acceleration(inputs);
        return new(firstIndex, kind, blas, scratch, inputs);
    }

    private void InitializeEmitters(ReadOnlySpan<FoliaMachi.Vertex> vertices, ReadOnlySpan<uint> indices)
    {
        var emitters = new List<Emitter>();
        var weights = new List<float>();
        var pdf = new float[indices.Length / 3];
        float total = 0;
        for (int instance = 0; instance < _instances.Length; instance++)
        {
            int first = (int)_instances[instance].FirstIndex;
            int end = instance + 1 < _instances.Length ? (int)_instances[instance + 1].FirstIndex : indices.Length;
            for (int index = first; index < end; index += 3)
            {
                ref readonly var a = ref vertices[(int)indices[index]];
                uint material = (uint)a.PositionMaterial.W;
                // The dynamic board remains sampleable when its current frame is black. The shader reads its actual radiance.
                float estimate = material switch
                {
                    2 or 6 or 8 => 1.1f,
                    11 => .2126f * a.NormalUvX.W + .7152f * a.UvY.X + .0722f * a.UvY.Y,
                    // The bulb alternates between .18 and 1.9 at equal duty cycle.
                    12 => 1.04f * (.2126f * a.Color.X + .7152f * a.Color.Y + .0722f * a.Color.Z),
                    3 or 5 or 7 or 10 => a.Color.W * Math.Max(.15f, .2126f * a.Color.X + .7152f * a.Color.Y + .0722f * a.Color.Z),
                    _ => 0
                };
                if (estimate <= 0) continue;
                Vector3 p = ToVector(a.PositionMaterial);
                Vector3 u = ToVector(vertices[(int)indices[index + 1]].PositionMaterial) - p;
                Vector3 v = ToVector(vertices[(int)indices[index + 2]].PositionMaterial) - p;
                float area = .5f * Vector3.Cross(u, v).Length();
                if (area <= 1e-8f) continue;
                total += area * estimate;
                emitters.Add(new((uint)instance, (uint)((index - first) / 3), 0, 0, estimate));
                weights.Add(area * estimate);
                pdf[index / 3] = estimate;
            }
        }
        _emitterCount = (uint)emitters.Count;
        _emitterWeight = total;
        if (emitters.Count == 0) emitters.Add(default);
        else
        {
            float reciprocal = 1 / total;
            AliasEntry[] entries = BuildAliasTable(CollectionsMarshal.AsSpan(weights), reciprocal);
            for (int i = 0; i < emitters.Count; i++)
                emitters[i] = emitters[i] with
                {
                    Alias = entries[i].Alias, Threshold = entries[i].Threshold, AreaPdf = emitters[i].AreaPdf * reciprocal
                };
            for (int i = 0; i < pdf.Length; i++) pdf[i] *= reciprocal;
        }
        _emitters = Upload<Emitter>(CollectionsMarshal.AsSpan(emitters));
        _emitterPdf = Upload<float>(pdf);
    }

    private static AliasEntry[] BuildAliasTable(ReadOnlySpan<float> weights, float inverseWeight)
    {
        var entries = new AliasEntry[weights.Length];
        var low = new Stack<int>(weights.Length);
        var high = new Stack<int>(weights.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            float normalized = weights[i] * inverseWeight * entries.Length;
            entries[i] = new((uint)i, normalized);
            (normalized < 1 ? low : high).Push(i);
        }
        while (low.Count > 0 && high.Count > 0)
        {
            int alias = high.Pop();
            int entry = low.Pop();
            entries[entry] = entries[entry] with { Alias = (uint)alias };
            float remaining = entries[alias].Threshold + entries[entry].Threshold - 1;
            entries[alias] = entries[alias] with { Threshold = remaining };
            (remaining < 1 ? low : high).Push(alias);
        }
        foreach (int entry in low) entries[entry] = entries[entry] with { Threshold = 1 };
        foreach (int entry in high) entries[entry] = entries[entry] with { Threshold = 1 };
        return entries;
    }

    private static Vector3 ToVector(Vector4 value) => new(value.X, value.Y, value.Z);

    private void RecordGeometry(ID3D12GraphicsCommandList4 commands, int slot, ReadOnlySpan<Matrix4x4> actorTransforms)
    {
        if (!actorTransforms.IsEmpty && actorTransforms.Length != _instances.Length - 1)
            throw new ArgumentException("Machi requires one transform for each scene actor.", nameof(actorTransforms));
        bool changed = !_sceneBuilt;
        int baseIndex = slot * _instances.Length;
        var data = new Span<InstanceData>(_instanceDataMapped, _instances.Length * FrameSlots)[baseIndex..];
        var descriptors = new Span<byte>(_instanceDescriptionsMapped, _instances.Length * FrameSlots * 64)[(baseIndex * 64)..];
        for (int i = 0; i < _instances.Length; i++)
        {
            var mesh = _instances[i];
            Matrix4x4 transform = i > 0 && !actorTransforms.IsEmpty ? actorTransforms[i - 1] : Matrix4x4.Identity;
            changed |= mesh.Current != transform;
            data[i] = new InstanceData
            {
                Current = transform, Previous = _sceneBuilt ? mesh.Current : transform,
                FirstIndex = mesh.FirstIndex, Kind = mesh.Kind
            };
            mesh.Current = transform;
            WriteInstance(descriptors.Slice(i * 64, 64), i, transform, mesh.Blas.GPUVirtualAddress);
        }
        if (!_sceneBuilt)
        {
            commands.CopyResource(_vertices, _verticesUpload);
            commands.CopyResource(_indices, _indicesUpload);
            commands.ResourceBarrierTransition(_vertices, ResourceStates.CopyDest, Read);
            commands.ResourceBarrierTransition(_indices, ResourceStates.CopyDest, Read);
            commands.CopyTextureRegion(new TextureCopyLocation(_sign, 0), 0, 0, 0,
                new TextureCopyLocation(_signUpload, _signFootprint), null);
            commands.ResourceBarrierTransition(_sign, ResourceStates.CopyDest, Read);
            foreach (var mesh in _instances)
            {
                commands.BuildRaytracingAccelerationStructure(new(mesh.Blas.GPUVirtualAddress, mesh.Inputs, 0, mesh.Scratch.GPUVirtualAddress));
                commands.ResourceBarrierUnorderedAccessView(mesh.Blas);
            }
            _initialUploadSlot = slot;
            SceneBuildCount++;
        }
        else if (_initialUploadSlot == slot)
        {
            // The host has completed this frame slot's first submission before reuse.
            ReleaseUpload(_verticesUpload);
            ReleaseUpload(_indicesUpload);
            ReleaseUpload(_signUpload);
            foreach (var mesh in _instances) ReleaseUpload(mesh.Scratch);
            _initialUploadSlot = -1;
        }
        if (changed)
        {
            var inputs = _tlasInputs;
            inputs.InstanceDescriptions = _instanceDescriptions.GPUVirtualAddress + (ulong)baseIndex * 64;
            if (_sceneBuilt)
            {
                commands.ResourceBarrierUnorderedAccessView(_tlas);
                inputs.Flags |= RaytracingAccelerationStructureBuildFlags.PerformUpdate;
            }
            commands.BuildRaytracingAccelerationStructure(new(_tlas.GPUVirtualAddress, inputs,
                _sceneBuilt ? _tlas.GPUVirtualAddress : 0, _tlasScratch.GPUVirtualAddress));
            commands.ResourceBarrierUnorderedAccessView(_tlas);
            _sceneBuilt = true;
        }
    }

    internal uint SceneBuildCount { get; private set; }

    private void ReleaseUpload(ID3D12Resource resource)
    {
        _sceneOwned.Remove(resource);
        resource.Dispose();
    }

    private void ReleaseScene()
    {
        if (_instanceDescriptionsMapped != null) _instanceDescriptions.Unmap(0);
        _instanceDescriptionsMapped = null;
        if (_instanceDataMapped != null) _instanceData.Unmap(0);
        _instanceDataMapped = null;
        for (int i = _sceneOwned.Count - 1; i >= 0; i--) _sceneOwned[i].Dispose();
        _sceneOwned.Clear();
        _instances = [];
        _initialUploadSlot = -1;
        _sceneBuilt = _historyValid = false;
        _frameIndex = 0;
        _analyticCount = 0;
        _analyticFraction = 0;
    }

    private static void WriteInstance(Span<byte> destination, int index, Matrix4x4 transform, ulong acceleration)
    {
        Matrix4x4 transposed = Matrix4x4.Transpose(transform);
        MemoryMarshal.CreateReadOnlySpan(ref transposed.M11, 12).CopyTo(MemoryMarshal.Cast<byte, float>(destination[..48]));
        MemoryMarshal.Write(destination[48..], (uint)index | 0xff000000u);
        MemoryMarshal.Write(destination[52..], 0u);
        MemoryMarshal.Write(destination[56..], acceleration);
    }
}
