using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D12;
using WinExSpectrumTest.Rendering;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class MachiDxrBackend
{
    // Immutable for a D3D12 device. Scene geometry, attachments and vendor contexts stay in the backend.
    internal sealed class TracePipeline : IDisposable
    {
        private readonly List<IDisposable> _owned = [];
        internal readonly ID3D12RootSignature TraceRoot;
        internal readonly ID3D12StateObject Trace;
        internal readonly DispatchRaysDescription Dispatch;

        internal TracePipeline(ID3D12Device5 device)
        {
            try
            {
                TraceRoot = Keep(device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
                    [
                        Root(RootParameterType.ConstantBufferView, 0),
                        Root(RootParameterType.ShaderResourceView, 0),
                        Root(RootParameterType.ShaderResourceView, 1),
                        Root(RootParameterType.ShaderResourceView, 2),
                        Table(DescriptorRangeType.UnorderedAccessView, TraceOutputs, 0),
                        Table(DescriptorRangeType.ShaderResourceView, 2, 3),
                        Root(RootParameterType.ShaderResourceView, 5),
                        Root(RootParameterType.ShaderResourceView, 6),
                        Root(RootParameterType.ShaderResourceView, 7),
                        Root(RootParameterType.ShaderResourceView, 8)
                    ],
                    [new StaticSamplerDescription(0, Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
                        TextureAddressMode.Clamp, 0, 1, ComparisonFunction.Never, StaticBorderColor.TransparentBlack,
                        0, float.MaxValue, ShaderVisibility.All, 0)])));
                Trace = Keep(CreateTrace(device));
                var shaderTable = Keep(device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
                        ResourceDescription.Buffer(192), ResourceStates.GenericRead));
                using (var properties = Trace.QueryInterface<ID3D12StateObjectProperties>())
                {
                    byte* table = shaderTable.Map<byte>(0);
                    try
                    {
                        new Span<byte>(table, 192).Clear();
                        CopyIdentifier(properties, "RayGen", table);
                        CopyIdentifier(properties, "Miss", table + 64);
                        CopyIdentifier(properties, "MachiTriangles", table + 128);
                    }
                    finally { shaderTable.Unmap(0); }
                }
                ulong address = shaderTable.GPUVirtualAddress;
                Dispatch = new DispatchRaysDescription
                {
                    RayGenerationShaderRecord = new(address, 32),
                    MissShaderTable = new(address + 64, 32, 32),
                    HitGroupTable = new(address + 128, 32, 32),
                    Depth = 1
                };
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private ID3D12StateObject CreateTrace(ID3D12Device5 device)
        {
            var options = device.CheckFeatureSupport<FeatureDataD3D12Options5>(Feature.Options5);
            if (options.RaytracingTier >= RaytracingTier.Tier1_1 &&
                device.CheckHighestShaderModel(ShaderModel.Model6_5) >= ShaderModel.Model6_5)
            {
                byte[] library = ShaderCompiler.Compile(MachiDxrShaders.InlineTrace, "", "lib_6_5");
                try { return CreateStateObject(device, library); }
                // Some drivers reject the inline library even when they advertise DXR 1.1.
                catch (SharpGenException exception) when (exception.HResult is
                    unchecked((int)0x80070057) or unchecked((int)0x80004001) or unchecked((int)0x887A0004))
                {
                    AppLog.Caught(exception);
                }
            }
            return CreateStateObject(device, ShaderCompiler.Compile(MachiDxrShaders.Trace, "", "lib_6_3"));
        }

        private ID3D12StateObject CreateStateObject(ID3D12Device5 device, byte[] library) =>
            device.CreateStateObject<ID3D12StateObject>(new StateObjectDescription(StateObjectType.RaytracingPipeline,
            [
                new(new DxilLibraryDescription(library, [new("RayGen"), new("Miss"), new("ClosestHit"), new("AnyHit")])),
                new(new HitGroupDescription("MachiTriangles", HitGroupType.Triangles,
                    anyHitShaderImport: "AnyHit", closestHitShaderImport: "ClosestHit")),
                new(new GlobalRootSignature(TraceRoot)),
                new(new RaytracingPipelineConfig(1)),
                new(new RaytracingShaderConfig(20, 8))
            ]));

        private T Keep<T>(T item) where T : IDisposable
        {
            _owned.Add(item);
            return item;
        }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
                GpuGraphics.DisposeResource(_owned[i]);
            _owned.Clear();
        }
        private static RootParameter1 Root(RootParameterType type, uint register) =>
            new(type, new RootDescriptor1(register, 0), ShaderVisibility.All);

        private static void CopyIdentifier(ID3D12StateObjectProperties properties, string name, byte* target)
        {
            nint identifier = properties.GetShaderIdentifier(name);
            if (identifier == 0) throw new InvalidOperationException("Machi ray tracing shader export is missing.");
            NativeMemory.Copy((void*)identifier, target, 32);
        }
    }

    private static RootParameter1 Table(DescriptorRangeType type, uint count, uint register) =>
        new(new RootDescriptorTable1(new DescriptorRange1(type, count, register)), ShaderVisibility.All);
}
