using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace WinExSpectrumTest.Rendering;

/// <summary>Loads build-time bytecode or a persistent runtime cache; compilation is only a cache-miss fallback.</summary>
internal static class ShaderCompiler
{
    // Only initialization calls this cache. Reuse bytecode across sizes and hosts;
    // Lazy also prevents startup warmup and a consumer compiling the same source twice.
    private static readonly ConcurrentDictionary<(string Source, string Entry, string Profile), Lazy<byte[]>> Cache = new();

    private static readonly ReaderWriterLockSlim Maintenance = new();
    internal static byte[] Compile(string sourceText, string entry, string profile)
    {
        Maintenance.EnterReadLock();
        try
        {
            return Cache.GetOrAdd((sourceText, entry, profile), static key => new Lazy<byte[]>(
                () => CompileCore(key.Source, key.Entry, key.Profile), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        finally { Maintenance.ExitReadLock(); }
    }

    internal static int Rebuild(Action<Action<string, string, string>> catalog)
    {
        Maintenance.EnterWriteLock();
        try
        {
            Cache.Clear();
            RenderCacheFile.Clear(CacheDirectory);
            var completed = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            catalog((source, entry, profile) =>
            {
                string key = Key(source, entry, profile);
                if (!completed.Add(key)) return;
                byte[] bytes = CompileNative(source, entry, profile);
                if (!RenderCacheFile.Write(CacheDirectory, key, bytes))
                    throw new IOException("The application shader cache directory is not writable: " + CacheDirectory);
            });
            return completed.Count;
        }
        finally { Maintenance.ExitWriteLock(); }
    }

    private static int _compiled, _diskHits;
    internal static int CompilationCount => Volatile.Read(ref _compiled);
    internal static int DiskHitCount => Volatile.Read(ref _diskHits);
    internal static string CacheDirectory => RenderCacheFile.DirectoryFor("ShaderCache");

    // Complete source, compiler options, entry point and target profile invalidate bytecode together.
    internal static string Key(string source, string entry, string profile)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "d3dcompiler47/O3/cache-v1\n" + profile + "\n" + entry + "\n" + source)));

    internal static byte[] CompileToDirectory(string source, string entry, string profile, string directory)
    {
        string key = Key(source, entry, profile);
        byte[]? cached = RenderCacheFile.Read(directory, key, 16 * 1024 * 1024);
        if (cached is { Length: >= 32 } && cached.AsSpan(0, 4).SequenceEqual("DXBC"u8))
        {
            Interlocked.Increment(ref _diskHits);
            return cached;
        }
        byte[] bytes = CompileNative(source, entry, profile);
        RenderCacheFile.Write(directory, key, bytes);
        return bytes;
    }

    private static byte[] CompileCore(string source, string entry, string profile)
        => CompileToDirectory(source, entry, profile, CacheDirectory);

    private static unsafe byte[] CompileNative(string sourceText, string entry, string profile)
    {
        Interlocked.Increment(ref _compiled);
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
