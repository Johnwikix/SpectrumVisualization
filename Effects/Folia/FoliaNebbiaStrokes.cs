using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Text;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Reads only the medians needed by the current atlas and estimates missing shapes on the CPU worker.</summary>
internal static class FoliaNebbiaStrokes
{
    internal readonly record struct Segment(Vector2 Start, Vector2 End, bool StartsStroke = false);
    private const int Grid = 32;

    internal static Dictionary<int, Segment[]> ReadMedialPaths(HashSet<int> wanted, CancellationToken token)
    {
        var paths = new Dictionary<int, Segment[]>();
        if (wanted.Count == 0)
            return paths;
        using var asset = typeof(FoliaNebbiaStrokes).Assembly.GetManifestResourceStream("Spectrum.Folia.Nebbia.strokes.bin.gz")
            ?? throw new InvalidOperationException("Missing Nebbia stroke resource.");
        using var stream = new GZipStream(asset, CompressionMode.Decompress);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x3153424E)
            throw new InvalidDataException("Invalid Nebbia stroke resource.");
        int count = reader.ReadInt32();
        var segments = new List<Segment>();
        var anchors = new List<Vector2>();
        for (int i = 0; i < count && paths.Count < wanted.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            int codepoint = reader.ReadInt32();
            int strokeCount = reader.ReadByte();
            bool keep = wanted.Contains(codepoint);
            segments.Clear();
            for (int stroke = 0; stroke < strokeCount; stroke++)
            {
                int points = reader.ReadUInt16();
                anchors.Clear();
                for (int j = 0; j < points; j++)
                {
                    var point = new Vector2(reader.ReadInt16(), reader.ReadInt16());
                    if (keep) anchors.Add(point / 1024);
                }
                if (keep) SmoothStroke(anchors, segments);
            }
            if (!keep || segments.Count == 0)
                continue;
            paths.Add(codepoint, segments.ToArray());
        }
        return paths;
    }

    private static void SmoothStroke(List<Vector2> points, List<Segment> output)
    {
        if (points.Count < 2) return;
        Vector2 previous = points[0];
        bool first = true;
        if (points.Count >= 3)
        {
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[Math.Max(0, i - 1)], b = points[i], c = points[i + 1], d = points[Math.Min(points.Count - 1, i + 2)];
                for (int j = 0; j < 6; j++)
                {
                    float t = j / 6f, t2 = t * t;
                    Vector2 p = .5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 +
                        (-a + 3 * b - 3 * c + d) * (t2 * t));
                    if (p == previous) continue;
                    output.Add(new(previous, p, first));
                    first = false;
                    previous = p;
                }
            }
        }
        if (previous != points[^1])
            output.Add(new(previous, points[^1], first));
    }

    internal static Segment[] Estimate(CanvasDevice device, CanvasTextFormat format, string text)
    {
        using var layout = new CanvasTextLayout(device, text, format, 2048, 256);
        using var geometry = CanvasGeometry.CreateText(layout);
        var bounds = geometry.ComputeBounds();
        if (bounds.Width < .1 || bounds.Height < .1)
            return [];
        Span<byte> grid = stackalloc byte[Grid * Grid];
        Span<int> pending = stackalloc int[Grid * Grid];
        grid.Clear();
        for (int row = 1; row < Grid - 1; row++)
            for (int col = 1; col < Grid - 1; col++)
            {
                float x = (float)(bounds.X + bounds.Width * (col - .5) / (Grid - 2));
                float y = (float)(bounds.Y + bounds.Height * (row - .5) / (Grid - 2));
                grid[row * Grid + col] = geometry.FillContainsPoint(new(x, y)) ? (byte)1 : (byte)0;
            }
        // Zhang–Suen thinning preserves connected paths for fonts without recorded stroke order.
        bool changed;
        do
        {
            changed = Thin(grid, pending, false);
            changed |= Thin(grid, pending, true);
        } while (changed);
        var segments = new List<Segment>();
        for (int col = 1; col < Grid - 1; col++)
            for (int row = 1; row < Grid - 1; row++)
            {
                int cell = row * Grid + col;
                if (grid[cell] == 0)
                    continue;
                var from = Point(col, row);
                if (grid[cell + 1] != 0)
                    segments.Add(new(from, Point(col + 1, row)));
                if (grid[cell + Grid] != 0)
                    segments.Add(new(from, Point(col, row + 1)));
                if (grid[cell + 1] == 0 && grid[cell + Grid] == 0 && grid[cell + Grid + 1] != 0)
                    segments.Add(new(from, Point(col + 1, row + 1)));
                if (grid[cell + 1] == 0 && grid[cell - Grid] == 0 && grid[cell - Grid + 1] != 0)
                    segments.Add(new(from, Point(col + 1, row - 1)));
                if (IsIsolated(grid, cell))
                    segments.Add(new(from, from + new Vector2(.012f, 0)));
            }
        return segments.ToArray();
    }

    private static Vector2 Point(int x, int y) => new((x - .5f) / (Grid - 2), (y - .5f) / (Grid - 2));

    private static bool IsIsolated(ReadOnlySpan<byte> grid, int i) => (grid[i - Grid - 1] | grid[i - Grid] | grid[i - Grid + 1]
        | grid[i - 1] | grid[i + 1] | grid[i + Grid - 1] | grid[i + Grid] | grid[i + Grid + 1]) == 0;

    private static bool Thin(Span<byte> grid, Span<int> pending, bool second)
    {
        Span<int> neighbors = stackalloc int[8];
        int count = 0;
        for (int y = 1; y < Grid - 1; y++)
            for (int x = 1; x < Grid - 1; x++)
            {
                int i = y * Grid + x;
                if (grid[i] == 0)
                    continue;
                neighbors[0] = grid[i - Grid];
                neighbors[1] = grid[i - Grid + 1];
                neighbors[2] = grid[i + 1];
                neighbors[3] = grid[i + Grid + 1];
                neighbors[4] = grid[i + Grid];
                neighbors[5] = grid[i + Grid - 1];
                neighbors[6] = grid[i - 1];
                neighbors[7] = grid[i - Grid - 1];
                int adjacent = 0, runs = 0;
                for (int j = 0; j < 8; j++)
                {
                    adjacent += neighbors[j];
                    runs += (1 - neighbors[j]) * neighbors[(j + 1) & 7];
                }
                int n = neighbors[0], e = neighbors[2], s = neighbors[4], w = neighbors[6];
                if (adjacent is >= 2 and <= 6 && runs == 1
                    && (second ? n * e * w + n * s * w : n * e * s + e * s * w) == 0)
                    pending[count++] = i;
            }
        foreach (int index in pending[..count])
            grid[index] = 0;
        return count != 0;
    }
}
