using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Immutable source-generated world geometry, projected into the native instanced stage.</summary>
internal sealed partial class FoliaDiorama
{
    private sealed record Surface(float Radius, Vector3[] Points);
    private readonly record struct Cluster(Vector3 Position, float Scale, float Stretch, int Surface, int Color, bool Far);
    private sealed record Line(Vector3 Position, Vector3 Right, Vector3 Up, Vector3 Forward, float Scale, float Roll, float Yaw, float Look,
        string Kind, float Side, Cluster[] Clusters);
    private long _atlasIdentity;
    private Line[] _lines = [];
    private Surface[] _surfaces = [];
    private Vector3 _camera, _aim;
    internal float FrameDelta;
    private double _seedPhase;
    internal float Energy
    {
        private get; set;
    }

    internal void Prepare(FoliaTextAtlas atlas)
    {
        if (atlas.Identity == _atlasIdentity)
            return;
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("seed", atlas.Seed);
            w.WriteStartArray("lines");
            foreach (var line in atlas.Lines)
            {
                w.WriteStartObject();
                w.WriteString("fullText", line.Source.Text);
                w.WriteNumber("startTime", line.Source.Start);
                w.WriteNumber("endTime", line.Source.End);
                w.WriteStartArray("words");
                foreach (var word in line.Source.Words)
                {
                    w.WriteStartObject();
                    w.WriteString("text", word.Text);
                    w.WriteNumber("startTime", word.Start);
                    w.WriteNumber("endTime", word.End);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        string json = FoliaSourcePlanner.Compile(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length), "compileDiorama");
        using var document = JsonDocument.Parse(json);
        var lines = new List<Line>();
        var surfaces = new List<Surface>();
        foreach (var surface in document.RootElement.GetProperty("surfaces").EnumerateArray())
        {
            var values = surface.GetProperty("points");
            var points = new Vector3[values.GetArrayLength() / 3];
            for (int i = 0; i < points.Length; i++)
                points[i] = new(values[i * 3].GetSingle(), values[i * 3 + 1].GetSingle(), values[i * 3 + 2].GetSingle());
            surfaces.Add(new(F(surface, "radius"), points));
        }
        foreach (var line in document.RootElement.GetProperty("lines").EnumerateArray())
        {
            var frame = line.GetProperty("frame");
            var placement = line.GetProperty("placement");
            Vector3 right = V(frame.GetProperty("right")), up = V(frame.GetProperty("up")), forward = V(frame.GetProperty("forward"));
            Vector3 position = V(frame.GetProperty("position")) + right * F(placement, "offsetR") + up * F(placement, "offsetU");
            var clusters = new List<Cluster>();
            foreach (var cluster in line.GetProperty("clusters").EnumerateArray())
                clusters.Add(new(V(cluster.GetProperty("position")), F(cluster, "scale"), F(cluster, "stretchY"), cluster.GetProperty("surface").GetInt32(), cluster.GetProperty("colorSlot").GetInt32(), cluster.GetProperty("layer").GetString() == "far"));
            double random = Math.Sin((SeedHash(atlas.Seed) + lines.Count * 7 + 3) * 12.9898) * 43758.5453;
            float side = random - Math.Floor(random) < .5 ? -1 : 1;
            lines.Add(new(position, right, up, forward, F(placement, "scale"), F(placement, "roll"), F(placement, "yaw"), F(placement, "lookR"),
                line.GetProperty("kind").GetString()!, side, clusters.ToArray()));
        }
        _lines = lines.ToArray();
        _surfaces = surfaces.ToArray();
        _atlasIdentity = atlas.Identity;
        _seedPhase = SeedHash(atlas.Seed) * .017;
        ResetMotion();
    }
    private static float F(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    private static Vector3 V(JsonElement value) => new(F(value, "x"), F(value, "y"), F(value, "z"));
    internal void Write(ref FoliaPainter p, FoliaTextAtlas atlas, int index, double time, float fontScale, Vector3 primary, Vector3 secondary, Vector3 accent)
    {
        if (_lines.Length == 0)
            return;
        var line = _lines[index];
        var active = atlas.Lines[index];
        float progress = Math.Clamp((float)((time - active.Source.Start) / Math.Max(.001, active.Source.End - active.Source.Start)), 0, 1);
        const float tangent = .52056705f; // 55-degree vertical FOV.
        float halfWidth = 5.2f * tangent * p.Width / p.Height;
        float em = .62f * Math.Max(.28f, Math.Min(1, halfWidth * 2 * .72f / Math.Max(.01f, active.Width * .62f))) * line.Scale * fontScale;
        float trackWidth = active.Width * em, allowed = Math.Max(Math.Abs(halfWidth * .92f - trackWidth * .5f), .001f);
        float wordProgress = progress;
        for (int i = 0; i < active.Glyphs.Length; i++)
            if (time < active.Glyphs[i].End)
            {
                wordProgress = (i + Math.Clamp((float)((time - active.Glyphs[i].Start) / Math.Max(.001, active.Glyphs[i].End - active.Glyphs[i].Start)), 0, 1)) / Math.Max(1, active.Glyphs.Length);
                break;
            }
        float settle = 1 - FoliaPainter.Ease((float)(time - active.Source.End - 3) / 5);
        float truck = allowed * MathF.Tanh((wordProgress - .5f) * trackWidth / allowed) * settle;
        var offset = ShotOffset(line.Kind, progress, wordProgress, line.Side, settle);
        var drift = Drift(time);
        Vector3 head = line.Position + line.Right * truck;
        Vector3 target = head + line.Right * (offset.X + drift.X) + line.Up * (offset.Y + drift.Y) - line.Forward * (offset.Z + drift.Z);
        Vector3 aim = head + line.Right * (line.Look * settle);
        UpdateCamera(line, active.Source, target, aim, time, p.Width / p.Height, out var rightView, out var upView, out var forward);
        float focal = p.Height / (2 * tangent);
        for (int li = Math.Min(_lines.Length - 1, index + 3); li >= Math.Max(0, index - 2); li--)
        {
            foreach (ref readonly var cluster in _lines[li].Clusters.AsSpan())
            {
                var surface = _surfaces[cluster.Surface];
                float distance = Vector3.Distance(cluster.Position, _camera);
                float life = FoliaPainter.Ease((27 - distance) / 7) * FoliaPainter.Ease((distance - 1.2f) / 1.8f);
                if (life < .001f)
                    continue;
                Vector3 color = cluster.Color == 0 ? primary : accent;
                float pulse = 1 + Energy * .1f;
                foreach (ref readonly var point in surface.Points.AsSpan())
                {
                    if (p.Remaining < 1000)
                        break;
                    Vector3 local = new(point.X, point.Y * cluster.Stretch, point.Z);
                    // One coherent low-frequency field preserves welded surfaces at every density.
                    float wave = MathF.Sin(point.X * 2.2f + point.Z * 1.4f + (float)time * 1.2f) * MathF.Sin(point.Y * 2.6f - (float)time * .7f) * Energy * .25f;
                    Vector3 normal = Vector3.Normalize(point + new Vector3(.0001f));
                    Vector3 world = cluster.Position + (local + normal * wave * surface.Radius) * cluster.Scale * pulse;
                    if (!Project(world, _camera, rightView, upView, forward, focal, p.Width, p.Height, out Vector2 xy, out float z))
                        continue;
                    float size = Math.Clamp((.018f + Energy * .025f) * focal / z, 1.4f, 26);
                    p.Shape(6, xy.X - size * .5f, xy.Y - size * .5f, size, size, color, life * (cluster.Far ? .5f : .72f));
                }
            }
        }
        for (int li = Math.Min(_lines.Length - 1, index + 3); li >= Math.Max(0, index - 2); li--)
        {
            var current = _lines[li];
            var text = atlas.Lines[li];
            float opacity = li - index switch
            {
                -2 => .1f,
                -1 => .3f,
                0 => .92f,
                1 => .34f,
                2 => .16f,
                _ => .06f
            };
            float font = .62f * Math.Max(.28f, Math.Min(1, halfWidth * 2 * .72f / Math.Max(.01f, text.Width * .62f))) * current.Scale * fontScale;
            float c = MathF.Cos(current.Roll), s = MathF.Sin(current.Roll);
            Vector3 yawRight = current.Right * MathF.Cos(current.Yaw) + current.Forward * MathF.Sin(current.Yaw);
            Vector3 textRight = yawRight * c + current.Up * s;
            Vector3 textUp = current.Up * c - yawRight * s;
            foreach (ref readonly var glyph in text.Glyphs.AsSpan())
            {
                Vector3 world = current.Position + textRight * ((glyph.Offset + glyph.Glyph.Advance * .5f - text.Width * .5f) * font);
                if (!Project(world, _camera, rightView, upView, forward, focal, p.Width, p.Height, out Vector2 xy, out float z))
                    continue;
                // Canvas middle baseline and full glyph overhang remain in the original 3D plane.
                float localX = (glyph.Glyph.BearingX - glyph.Glyph.Advance * .5f) * font;
                float localY = (glyph.Glyph.CanvasCenterY - glyph.Glyph.Height * .5f) * font;
                Vector3 topLeft = world + textRight * localX - textUp * localY;
                Vector3 topRight = topLeft + textRight * (glyph.Glyph.Width * font);
                Vector3 bottomLeft = topLeft - textUp * (glyph.Glyph.Height * font);
                Vector3 bottomRight = topRight - textUp * (glyph.Glyph.Height * font);
                if (!Project(topLeft, _camera, rightView, upView, forward, focal, p.Width, p.Height, out var a, out float za)
                    || !Project(topRight, _camera, rightView, upView, forward, focal, p.Width, p.Height, out var b, out float zb)
                    || !Project(bottomRight, _camera, rightView, upView, forward, focal, p.Width, p.Height, out var d, out float zd)
                    || !Project(bottomLeft, _camera, rightView, upView, forward, focal, p.Width, p.Height, out var e, out float ze))
                    continue;
                float distance = Vector3.Distance(world, _camera);
                float life = FoliaPainter.Ease((52 - distance) / 16) * FoliaPainter.Ease((distance - .9f) / 1.1f);
                float fog = FoliaPainter.Ease((z - 12) / 18);
                bool activeGlyph = li == index && time >= glyph.Start && time < glyph.End;
                float reveal = time >= glyph.Start ? 1 : .5f;
                Vector3 color = activeGlyph ? accent : li > index ? secondary : primary;
                color = Vector3.Lerp(color, FoliaSong.Empty.Palette[0], fog);
                p.GlyphQuad(glyph.Glyph, a, b, d, e, new(za, zb, zd, ze), color,
                    opacity * life * (li == index ? reveal : 1), activeGlyph ? .4f : 0);
            }
        }
    }
    private static bool Project(Vector3 world, Vector3 camera, Vector3 right, Vector3 up, Vector3 forward, float focal, float width, float height, out Vector2 point, out float z)
    {
        Vector3 delta = world - camera;
        z = Vector3.Dot(delta, forward);
        if (z <= .1f)
        {
            point = default;
            return false;
        }
        point = new(width * .5f + Vector3.Dot(delta, right) * focal / z, height * .5f - Vector3.Dot(delta, up) * focal / z);
        return float.IsFinite(point.X) && float.IsFinite(point.Y);
    }
}
