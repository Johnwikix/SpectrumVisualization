using System;
using System.Collections.Generic;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaNebbia
{
    private readonly record struct Bead(float Along, Vector2 Point, Vector2 Normal, float Radius, float Jitter);
    private readonly record struct Drip(float Delay, float Length, float Duration, float Radius, float Phase);
    private readonly record struct WetStroke(Bead[] Beads, Drip Drip, float EndRadius);
    private readonly record struct GlyphArt(PenPath Path, WetStroke[] Wet);

    private struct InkRandom(uint seed)
    {
        private uint _state = seed;
        public double Next()
        {
            uint z = _state = unchecked(_state + 0x6d2b79f5u);
            z = unchecked((z ^ (z >> 15)) * (1 | z));
            z = unchecked(z + (z ^ (z >> 7)) * (61 | z)) ^ z;
            return (z ^ (z >> 14)) / 4294967296d;
        }
        public float Signed() => (float)(Next() * 2 - 1);
    }

    private static GlyphArt PrepareArt(in PenPath original, float size, int seed)
    {
        var random = new InkRandom(unchecked((uint)seed));
        var warpRandom = new InkRandom((uint)(random.Next() * 1e9));
        PenPath path = Warp(original, ref warpRandom);
        var wet = new WetStroke[path.Strokes.Length];
        var beads = new List<Bead>();
        float interval = 7 / Math.Max(1, size) / path.Length;
        for (int i = 0; i < wet.Length; i++)
        {
            ref readonly var stroke = ref path.Strokes[i];
            beads.Clear();
            int lineIndex = stroke.First;
            for (float at = stroke.Start + interval * (float)random.Next(); at < stroke.End;
                at += interval * (.5f + (float)random.Next()))
            {
                if (random.Next() > .35)
                    continue;
                while (lineIndex < stroke.First + stroke.Count - 1 && path.Lines[lineIndex].End < at)
                    lineIndex++;
                ref readonly var edge = ref path.Lines[lineIndex];
                Vector2 point = Vector2.Lerp(edge.From, edge.To, Math.Clamp((at - edge.Start) / Math.Max(1e-6f, edge.End - edge.Start), 0, 1));
                Vector2 delta = edge.To - edge.From;
                var axis = new Vector2(-delta.Y, delta.X) / Math.Max(1e-6f, delta.Length());
                float side = random.Next() < .5 ? -1 : 1;
                float radius = 1.6f + (float)Math.Pow(random.Next(), 2.2) * 3.6f;
                beads.Add(new(at, point, axis * side, radius, (float)random.Next()));
            }
            wet[i] = new(beads.ToArray(), default, 0);
        }
        for (int i = 0; i < wet.Length; i++)
            if (random.Next() < .055)
                wet[i] = wet[i] with
                {
                    Drip = new(.3f + 1.6f * (float)random.Next(), 30 + 130 * (float)random.Next(),
                        2 + 3.5f * (float)random.Next(), 3.2f + 2.6f * (float)random.Next(), 100 * (float)random.Next())
                };
        for (int i = 0; i < wet.Length; i++)
        {
            wet[i] = wet[i] with { EndRadius = .031f + .018f * (float)random.Next() };
            random.Next();
        }
        return new(path, wet);
    }

    private static PenPath Warp(in PenPath path, ref InkRandom random)
    {
        const float Warp = .6f;
        float shear = random.Signed() * .16f * Warp;
        float sx = 1 + random.Signed() * .1f * Warp;
        float sy = 1 + random.Signed() * .1f * Warp;
        float bx = random.Signed() * .035f * Warp;
        float by = random.Signed() * .035f * Warp;
        float px = (float)random.Next() * 6.28f, py = (float)random.Next() * 6.28f;
        float strength = Warp * Math.Clamp(8f / path.Strokes.Length, .35f, 1);
        var segments = new FoliaNebbiaStrokes.Segment[path.Lines.Length];
        foreach (ref readonly var stroke in path.Strokes.AsSpan())
        {
            ReadOnlySpan<PenLine> edges = path.Lines.AsSpan(stroke.First, stroke.Count);
            Vector2 head = edges[0].From;
            Vector2 mean = head;
            foreach (ref readonly var edge in edges)
                mean += edge.To;
            mean /= edges.Length + 1;
            float angle = random.Signed() * .09f * strength;
            float stretch = 1 + random.Signed() * .1f * strength;
            var offset = new Vector2(random.Signed(), random.Signed()) * (.022f * strength);
            float frequency = 18 + 14 * (float)random.Next();
            float pulse = 6.28f * (float)random.Next();
            var (sin, cos) = MathF.SinCos(angle);
            Vector2 prior = At(head, edges[0].To - head, 0);
            for (int i = 0; i < edges.Length; i++)
            {
                ref readonly var edge = ref edges[i];
                Vector2 tangent = (i + 1 < edges.Length ? edges[i + 1].To : edge.To) - edge.From;
                Vector2 p = At(edge.To, tangent, (edge.End - stroke.Start) * path.Length);
                segments[stroke.First + i] = new(prior, p, i == 0);
                prior = p;
            }

            Vector2 At(Vector2 point, Vector2 tangent, float distance)
            {
                Vector2 local = head + (point - head) * stretch - mean;
                Vector2 q = mean + new Vector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos) + offset;
                q += new Vector2(-tangent.Y, tangent.X) * (MathF.Sin(distance * frequency + pulse) * (.006f * Warp) /
                    Math.Max(1e-5f, tangent.Length()));
                float u = (q.X - .5f) * sx, v = (q.Y - .5f) * sy;
                u += v * shear + bx * MathF.Sin(v * MathF.PI * 1.6f + px);
                v += by * MathF.Sin(u * MathF.PI * 1.6f + py);
                return new(u + .5f, v + .5f);
            }
        }
        return PreparePath(segments);
    }
}
