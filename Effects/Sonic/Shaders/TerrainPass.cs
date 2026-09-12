using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ComputeSharp;
using ComputeSharp.D2D1;
using ComputeSharp.D2D1.Descriptors;
using ComputeSharp.D2D1.Interop;

namespace WinExSpectrumTest.Effects.Sonic.Shaders;

// D2D's default Sample uses derivatives, forcing FXC to unroll the entire DDA.
// This mip-free data texture needs explicit LOD 0, allowing a compact loop and
// immediate termination on a hit. Constants and metadata retain generated layouts.
[D2DInputCount(1)]
internal readonly struct TerrainPass(TerrainShader shader) : ID2D1PixelShader, ID2D1PixelShaderDescriptor<TerrainPass>
{
    private readonly TerrainShader _shader = shader;
    public float4 Execute() => _shader.Execute();
    public static implicit operator TerrainPass(TerrainShader shader) => new(shader);
    private static readonly Guid Id = new("c4b33837-f5a6-4f7d-9d56-d67244a11e99");
    public static ref readonly Guid EffectId => ref Id;
    public static unsafe nint EffectFactory => (nint)(delegate* unmanaged[Stdcall]<void**, int>)&CreateEffect;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int CreateEffect(void** effect) => D2D1PixelShaderEffect.CreateEffectUnsafe<TerrainPass>(effect);
    public static string? EffectDisplayName => Metadata<TerrainShader>.EffectDisplayName;
    public static string? EffectDescription => Metadata<TerrainShader>.EffectDescription;
    public static string? EffectCategory => Metadata<TerrainShader>.EffectCategory;
    public static string? EffectAuthor => Metadata<TerrainShader>.EffectAuthor;
    public static int ConstantBufferSize => Metadata<TerrainShader>.ConstantBufferSize;
    public static int InputCount => Metadata<TerrainShader>.InputCount;
    public static int ResourceTextureCount => Metadata<TerrainShader>.ResourceTextureCount;
    public static global::System.ReadOnlyMemory<global::ComputeSharp.D2D1.Interop.D2D1PixelShaderInputType> InputTypes => Metadata<TerrainShader>.InputTypes;
    public static global::System.ReadOnlyMemory<global::ComputeSharp.D2D1.Interop.D2D1InputDescription> InputDescriptions => Metadata<TerrainShader>.InputDescriptions;
    public static global::System.ReadOnlyMemory<global::ComputeSharp.D2D1.Interop.D2D1ResourceTextureDescription> ResourceTextureDescriptions => Metadata<TerrainShader>.ResourceTextureDescriptions;
    public static ComputeSharp.D2D1.D2D1PixelOptions PixelOptions => Metadata<TerrainShader>.PixelOptions;
    public static ComputeSharp.D2D1.D2D1BufferPrecision BufferPrecision => Metadata<TerrainShader>.BufferPrecision;
    public static ComputeSharp.D2D1.D2D1ChannelDepth ChannelDepth => Metadata<TerrainShader>.ChannelDepth;
    public static ComputeSharp.D2D1.D2D1CompileOptions CompileOptions => Metadata<TerrainShader>.CompileOptions;
    public static D2D1ShaderProfile ShaderProfile => D2D1ShaderProfile.PixelShader50;
    public static string HlslSource => Metadata<TerrainShader>.HlslSource.Replace(
        "#include \"d2d1effecthelpers.hlsli\"",
        "#include \"d2d1effecthelpers.hlsli\"\n#undef D2DSampleInputAtPosition\n#define D2DSampleInputAtPosition(index, pos) InputTexture##index.SampleLevel(InputSampler##index, __d2dstatic_uv##index.xy + __d2dstatic_uv##index.zw * (pos - __d2dstatic_scenePos.xy), 0)\n");
    private static readonly ReadOnlyMemory<byte> Bytecode = D2D1ShaderCompiler.Compile(HlslSource, "Execute", ShaderProfile, CompileOptions);
    public static ReadOnlyMemory<byte> HlslBytecode => Bytecode;
    public static TerrainPass CreateFromConstantBuffer(ReadOnlySpan<byte> buffer) => new(Metadata<TerrainShader>.Create(buffer));
    public static void LoadConstantBuffer<TLoader>(in TerrainPass shader, ref TLoader loader) where TLoader : struct, ID2D1ConstantBufferLoader
        => Metadata<TerrainShader>.Load(in shader._shader, ref loader);

    private static class Metadata<T> where T : unmanaged, ID2D1PixelShader, ID2D1PixelShaderDescriptor<T>
    {
        public static string? EffectDisplayName => T.EffectDisplayName;
        public static string? EffectDescription => T.EffectDescription;
        public static string? EffectCategory => T.EffectCategory;
        public static string? EffectAuthor => T.EffectAuthor;
        public static int ConstantBufferSize => T.ConstantBufferSize;
        public static int InputCount => T.InputCount;
        public static int ResourceTextureCount => T.ResourceTextureCount;
        public static global::System.ReadOnlyMemory<global::ComputeSharp.D2D1.Interop.D2D1PixelShaderInputType> InputTypes => T.InputTypes;
        public static global::System.ReadOnlyMemory<global::ComputeSharp.D2D1.Interop.D2D1InputDescription> InputDescriptions => T.InputDescriptions;
        public static global::System.ReadOnlyMemory<global::ComputeSharp.D2D1.Interop.D2D1ResourceTextureDescription> ResourceTextureDescriptions => T.ResourceTextureDescriptions;
        public static ComputeSharp.D2D1.D2D1PixelOptions PixelOptions => T.PixelOptions;
        public static ComputeSharp.D2D1.D2D1BufferPrecision BufferPrecision => T.BufferPrecision;
        public static ComputeSharp.D2D1.D2D1ChannelDepth ChannelDepth => T.ChannelDepth;
        public static ComputeSharp.D2D1.D2D1ShaderProfile ShaderProfile => T.ShaderProfile;
        public static ComputeSharp.D2D1.D2D1CompileOptions CompileOptions => T.CompileOptions;
        public static string HlslSource => T.HlslSource;
        public static T Create(ReadOnlySpan<byte> buffer) => T.CreateFromConstantBuffer(buffer);
        public static void Load<TLoader>(in T shader, ref TLoader loader) where TLoader : struct, ID2D1ConstantBufferLoader
            => T.LoadConstantBuffer(in shader, ref loader);
    }
}

