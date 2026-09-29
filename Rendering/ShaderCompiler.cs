using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace WinExSpectrumTest.Rendering;

/// <summary>Compiles statically selected shader sources during resource initialization.</summary>
internal static class ShaderCompiler
{
    // Only initialization calls this cache. Reuse bytecode across sizes and hosts;
    // Lazy also prevents startup warmup and a consumer compiling the same source twice.
    private static readonly ConcurrentDictionary<(string Source, string Entry, string Profile), Lazy<byte[]>> Cache = new();

    internal static byte[] Compile(string sourceText, string entry, string profile) =>
        Cache.GetOrAdd((sourceText, entry, profile), static key => new Lazy<byte[]>(
            () => CompileCore(key.Source, key.Entry, key.Profile), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static unsafe byte[] CompileCore(string sourceText, string entry, string profile)
    {
        byte[] source = Encoding.UTF8.GetBytes(sourceText);
        fixed (byte* pointer = source)
        {
            var result = Compiler.Compile(pointer, new PointerUSize((nuint)source.Length), "Spectrum.hlsl",
                null, null, entry, profile, ShaderFlags.OptimizationLevel3, EffectFlags.None, out Blob shader, out Blob errors);
            using (shader)
            using (errors)
            {
                if (shader == null || result.Failure)
                    throw new InvalidOperationException($"Shader {entry}: {errors?.AsString()} ({result})");
                byte[] bytes = new byte[(int)(ulong)shader.BufferSize];
                Marshal.Copy(shader.BufferPointer, bytes, 0, bytes.Length);
                return bytes;
            }
        }
    }
}
