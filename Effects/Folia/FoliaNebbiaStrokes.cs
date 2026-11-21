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
    private const int Grid = 64;
    private static ReadOnlySpan<int> NeighborOffsets => [1, Grid + 1, Grid, Grid - 1, -1, -Grid - 1, -Grid, -Grid + 1];
    private static ReadOnlySpan<int> DirectionChanges => [0, 1, -1, 2, -2, 3, -3, 4];

    internal static Dictionary<int, Segment[]> ReadMedialPaths(HashSet<int> wanted, CancellationToken token)
    {
        var paths = new Dictionary<int, Segment[]>();
        if (wanted.Count == 0)
            return paths;
        int lastWanted = 0;
        foreach (int codepoint in wanted)
            lastWanted = Math.Max(lastWanted, codepoint);
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
            // Build/FoliaNebbia/strokes.py writes records in codepoint order.
            if (codepoint > lastWanted)
                break;
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

        // Match the upstream handwriting square, centered on the advance and aligned to an alphabetic baseline.
        // Sampling at a uniform scale preserves narrow letters, descenders and diacritics.
        float scale = .78f / format.FontSize;
        float originX = .5f - (float)layout.LayoutBoundsIncludingTrailingWhitespace.Width * scale * .5f;
        // Win2D's projected LineMetrics array requires non-blittable WinRT marshaling.
        float originY = .8f - FoliaDistanceField.TextMetrics(layout).Baseline * scale;
        int left = Math.Clamp((int)Math.Floor((originX + bounds.Left * scale) * Grid), 1, Grid - 1);
        int right = Math.Clamp((int)Math.Ceiling((originX + bounds.Right * scale) * Grid), left, Grid - 1);
        int top = Math.Clamp((int)Math.Floor((originY + bounds.Top * scale) * Grid), 1, Grid - 1);
        int bottom = Math.Clamp((int)Math.Ceiling((originY + bounds.Bottom * scale) * Grid), top, Grid - 1);
        Span<byte> grid = stackalloc byte[Grid * Grid];
        Span<int> pending = stackalloc int[Grid * Grid];
        grid.Clear();
        for (int row = top; row < bottom; row++)
            for (int col = left; col < right; col++)
            {
                float x = ((col + .5f) / Grid - originX) / scale;
                float y = ((row + .5f) / Grid - originY) / scale;
                grid[row * Grid + col] = geometry.FillContainsPoint(new(x, y)) ? (byte)1 : (byte)0;
            }
        Span<byte> coverage = stackalloc byte[Grid * Grid];
        grid.CopyTo(coverage);
        // Zhang–Suen thinning preserves connected paths for fonts without recorded stroke order.
        bool changed;
        do
        {
            changed = Thin(grid, pending, left, top, right, bottom, false);
            changed |= Thin(grid, pending, left, top, right, bottom, true);
        } while (changed);

        RestoreDots(grid, coverage, pending, left, top, right, bottom);
        return Trace(grid, left, top, right, bottom);
    }

    private static void RestoreDots(Span<byte> grid, Span<byte> coverage, Span<int> queue, int left, int top, int right, int bottom)
    {
        // Thinning can delete all pixels of a tiny component in one pass, leaving punctuation and accents blank.
        var offsets = NeighborOffsets;
        for (int row = top; row < bottom; row++)
            for (int col = left; col < right; col++)
            {
                int cell = row * Grid + col;
                if (coverage[cell] == 0)
                    continue;
                queue[0] = cell;
                coverage[cell] = 0;
                int count = 1;
                bool hasSkeleton = false;
                for (int i = 0; i < count; i++)
                {
                    int point = queue[i];
                    hasSkeleton |= grid[point] != 0;
                    foreach (int offset in offsets)
                    {
                        int next = point + offset;
                        if (coverage[next] == 0)
                            continue;
                        coverage[next] = 0;
                        queue[count++] = next;
                    }
                }
                if (!hasSkeleton)
                    grid[cell] = 1;
            }
    }

    private static Segment[] Trace(ReadOnlySpan<byte> grid, int left, int top, int right, int bottom)
    {
        Span<byte> links = stackalloc byte[Grid * Grid];
        links.Clear();
        var offsets = NeighborOffsets;
        var segments = new List<Segment>();
        for (int row = top; row < bottom; row++)
            for (int col = left; col < right; col++)
            {
                int cell = row * Grid + col;
                if (grid[cell] == 0)
                    continue;
                for (int direction = 0; direction < offsets.Length; direction++)
                {
                    if (grid[cell + offsets[direction]] == 0)
                        continue;
                    // A diagonal is redundant if the same two pixels meet through an orthogonal neighbor.
                    if ((direction & 1) != 0 && (grid[cell + offsets[direction - 1]] | grid[cell + offsets[(direction + 1) & 7]]) != 0)
                        continue;
                    links[cell] |= (byte)(1 << direction);
                }
            }

        // Trace endpoints first, then close loops and consume any unused branches.
        // Each emitted stroke is continuous, which the wet-ink warp expects.
        var points = new List<Vector2>();
        var simplified = new List<Vector2>();
        for (int pass = 0; pass < 2; pass++)
            for (int row = top; row < bottom; row++)
                for (int col = left; col < right; col++)
                {
                    int cell = row * Grid + col;
                    if (pass == 0 && grid[cell] != 0 && links[cell] == 0 && IsIsolated(grid, cell))
                    {
                        var point = Point(cell);
                        segments.Add(new(point, point + new Vector2(1f / Grid, 0), true));
                    }
                    else if (links[cell] != 0 && (pass == 1 || BitOperations.PopCount((uint)links[cell]) == 1))
                    {
                        Walk(links, cell, points);
                        AddStroke(points, simplified, segments);
                    }
                }
        return segments.ToArray();
    }

    private static void Walk(Span<byte> links, int cell, List<Vector2> points)
    {
        points.Clear();
        points.Add(Point(cell));
        int heading = 0;
        var offsets = NeighborOffsets;
        while (links[cell] != 0)
        {
            foreach (int change in DirectionChanges)
            {
                int direction = (heading + change) & 7;
                if ((links[cell] & (1 << direction)) == 0)
                    continue;
                links[cell] &= (byte)~(1 << direction);
                cell += offsets[direction];
                links[cell] &= (byte)~(1 << (direction ^ 4));
                points.Add(Point(cell));
                heading = direction;
                break;
            }
        }
    }

    private static void AddStroke(List<Vector2> points, List<Vector2> simplified, List<Segment> segments)
    {
        // Keep the pen moving from the upper-left end of each traced stroke.
        Vector2 first = points[0], last = points[^1];
        if (first.Y + first.X * .6f > last.Y + last.X * .6f)
            points.Reverse();

        simplified.Clear();
        simplified.Add(points[0]);
        Simplify(points, 0, points.Count - 1, simplified);
        Vector2 previous = simplified[0];
        bool startsStroke = true;
        if (simplified.Count > 2)
            for (int i = 1; i < simplified.Count; i++)
            {
                Vector2 a = simplified[i - 1], b = simplified[i];
                Add(Vector2.Lerp(a, b, .25f));
                Add(Vector2.Lerp(a, b, .75f));
            }
        Add(simplified[^1]);

        void Add(Vector2 point)
        {
            if (point == previous)
                return;
            segments.Add(new(previous, point, startsStroke));
            startsStroke = false;
            previous = point;
        }
    }

    // Ramer–Douglas–Peucker simplification keeps per-frame stroke instances bounded by the shape's detail.
    private static void Simplify(List<Vector2> points, int first, int last, List<Vector2> result)
    {
        Vector2 start = points[first], span = points[last] - start;
        float spanLengthSquared = Math.Max(span.LengthSquared(), 1e-6f);
        const float Tolerance = .8f / Grid;
        float furthestDistance = Tolerance * Tolerance;
        int furthestPoint = first;
        for (int i = first + 1; i < last; i++)
        {
            Vector2 offset = points[i] - start;
            float along = Math.Clamp(Vector2.Dot(offset, span) / spanLengthSquared, 0, 1);
            float distance = (offset - span * along).LengthSquared();
            if (distance <= furthestDistance)
                continue;
            furthestDistance = distance;
            furthestPoint = i;
        }
        if (furthestPoint == first)
        {
            result.Add(points[last]);
            return;
        }
        Simplify(points, first, furthestPoint, result);
        Simplify(points, furthestPoint, last, result);
    }

    private static Vector2 Point(int cell) => new((cell % Grid + .5f) / Grid, (cell / Grid + .5f) / Grid);

    private static bool IsIsolated(ReadOnlySpan<byte> grid, int i) => (grid[i - Grid - 1] | grid[i - Grid] | grid[i - Grid + 1]
        | grid[i - 1] | grid[i + 1] | grid[i + Grid - 1] | grid[i + Grid] | grid[i + Grid + 1]) == 0;

    private static bool Thin(Span<byte> grid, Span<int> pending, int left, int top, int right, int bottom, bool second)
    {
        Span<int> neighbors = stackalloc int[8];
        int count = 0;
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
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
