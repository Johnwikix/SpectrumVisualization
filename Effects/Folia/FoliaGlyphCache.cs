using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Graphics.Canvas.Geometry;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Immutable glyph tiles, cached by their actual resolved outline. Never called from a steady-state frame.</summary>
internal static class FoliaGlyphCache
{
    private const long MemoryLimit = 64L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, byte[]> Tiles = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();
    private static long _bytes;
    internal static string CacheDirectory => RenderCacheFile.DirectoryFor("GlyphCache");

    internal static void ClearMemory()
    {
        lock (Gate)
        {
            Tiles.Clear();
            Order.Clear();
            _bytes = 0;
        }
    }

    internal static byte[] Get(CanvasGeometry outline, int width, int height, float x, float y,
        Span<float> field, Span<float> coverage, Span<float> horizontal, float[] kernel)
    {
        // Dimensions, translation, MTSDF range, half encoding and shadow recipe are part of the versioned identity.
        Span<byte> identity = stackalloc byte[60];
        FoliaDistanceField.Signature(outline, identity);
        BinaryPrimitives.WriteInt32LittleEndian(identity[32..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(identity[36..], width);
        BinaryPrimitives.WriteInt32LittleEndian(identity[40..], height);
        BinaryPrimitives.WriteSingleLittleEndian(identity[44..], x);
        BinaryPrimitives.WriteSingleLittleEndian(identity[48..], y);
        BinaryPrimitives.WriteSingleLittleEndian(identity[52..], FoliaDistanceField.Range);
        BinaryPrimitives.WriteInt32LittleEndian(identity[56..], kernel.Length);
        string key = Convert.ToHexString(SHA256.HashData(identity));
        lock (Gate) if (Tiles.TryGetValue(key, out var tile)) return tile;
        int count = checked(width * height), length = checked(count * 9);
        byte[]? data = RenderCacheFile.Read(CacheDirectory, key, length);
        if (data == null || data.Length != length)
        {
            data = new byte[length];
            FoliaDistanceField.Render(outline, width, height, x, y, field);
            var distances = MemoryMarshal.Cast<byte, Half>(data.AsSpan(0, count * 8));
            for (int i = 0; i < distances.Length; i++) distances[i] = (Half)field[i];
            FoliaGlyphShadow.Write(field, width, height, kernel, coverage, horizontal, data.AsSpan(count * 8), width, 0, 0);
            RenderCacheFile.Write(CacheDirectory, key, data);
        }
        lock (Gate)
        {
            if (Tiles.TryGetValue(key, out var existing)) return existing;
            while (_bytes + data.Length > MemoryLimit && Order.TryDequeue(out string? oldest))
                if (Tiles.Remove(oldest, out var removed)) _bytes -= removed.Length;
            if (data.Length <= MemoryLimit)
            {
                Tiles.Add(key, data);
                Order.Enqueue(key);
                _bytes += data.Length;
            }
        }
        return data;
    }
}
