using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>The original deterministic planner runs only at song/font/viewport changes.
/// The render loop reads immutable native arrays; it never invokes the interpreter.</summary>
internal sealed class FoliaSonnet
{
    private sealed record TextRun(float Font, float Rotation, string Role, NativeGlyph[] Glyphs);
    private readonly record struct NativeGlyph(FoliaGlyph Glyph, Vector2 Base, Vector2 Enter, float EntryRotation, double Start, double Settle);
    private readonly record struct CameraFrame(Vector2 Focus, Vector2 Offset, float Scale, float Rotation);
    private sealed record Shot(string Kind, double Start, double End, Vector4 Camera, int Ordinal, TextRun[] Text, CameraFrame[] Frames);
    private long _atlasIdentity;
    private float _width, _height, _scale;
    private Shot[] _shots = [];
    internal int ChromaticCount { get; private set; }

    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float fontScale, string family, int weight)
    {
        if (atlas.Identity == _atlasIdentity && width == _width && height == _height && fontScale == _scale)
            return;
        _shots = [];
        if (atlas.Lines.Length > 0)
        {
            string input = FoliaSourcePlanner.CreateInput(atlas, width, height, fontScale, family, weight);
            string result = FoliaSourcePlanner.Compile(input, "compile");
            using var document = JsonDocument.Parse(result);
            var shots = new List<Shot>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var texts = new List<TextRun>();
                foreach (var text in item.GetProperty("texts").EnumerateArray())
                {
                    var glyphs = new List<NativeGlyph>();
                    foreach (var g in text.GetProperty("glyphs").EnumerateArray())
                    {
                        if (!g.TryGetProperty("line", out var sourceLine) || !g.TryGetProperty("index", out var sourceIndex))
                            continue;
                        int li = sourceLine.GetInt32(), gi = sourceIndex.GetInt32();
                        if ((uint)li >= (uint)atlas.Lines.Length || (uint)gi >= (uint)atlas.Lines[li].Glyphs.Length)
                            continue;
                        glyphs.Add(new(atlas.Lines[li].Glyphs[gi].Glyph, new(F(g, "baseX"), F(g, "baseY")), new(F(g, "enterX"), F(g, "enterY")),
                            F(g, "entryRotation"), g.GetProperty("startTime").GetDouble(), g.GetProperty("settleTime").GetDouble()));
                    }
                    texts.Add(new(F(text, "font"), F(text, "rotation"), text.GetProperty("role").GetString()!, glyphs.ToArray()));
                }
                var frames = new List<CameraFrame>();
                foreach (var f in item.GetProperty("frames").EnumerateArray())
                    frames.Add(new(new(f[0].GetSingle(), f[1].GetSingle()), new(f[2].GetSingle(), f[3].GetSingle()), f[4].GetSingle(), f[5].GetSingle()));
                var camera = item.GetProperty("camera");
                shots.Add(new(item.GetProperty("kind").GetString()!, item.GetProperty("start").GetDouble(), item.GetProperty("end").GetDouble(),
                    new(F(camera, "x"), F(camera, "y"), F(camera, "zoom"), F(camera, "rotation")), shots.Count, texts.ToArray(), frames.ToArray()));
            }
            _shots = shots.ToArray();
        }
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _scale = fontScale;
    }
    private static float F(JsonElement value, string name) => value.GetProperty(name).GetSingle();
    internal void Write(ref FoliaPainter p, double time, Vector3 primary, Vector3 accent)
    {
        ChromaticCount = 0;
        if (_shots.Length == 0)
            return;
        int si = 0;
        while (si + 1 < _shots.Length && _shots[si + 1].Start <= time)
            si++;
        var shot = _shots[si];
        float phase = Math.Clamp((float)((time - shot.Start) / Math.Max(.001, shot.End - shot.Start)), 0, 1) * (shot.Frames.Length - 1);
        int a = Math.Min((int)phase, shot.Frames.Length - 2);
        float blend = phase - a;
        var one = shot.Frames[a];
        var two = shot.Frames[a + 1];
        Vector2 focus = Vector2.Lerp(one.Focus, two.Focus, blend), offset = Vector2.Lerp(one.Offset, two.Offset, blend);
        float cameraScale = shot.Camera.Z * (one.Scale + (two.Scale - one.Scale) * blend);
        float angle = shot.Camera.W + one.Rotation + (two.Rotation - one.Rotation) * blend;
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        Vector2 anchor = new(p.Width * (shot.Kind == "poster-blocks" ? .5f : .5f + shot.Camera.X),
            p.Height * (shot.Kind == "poster-blocks" ? .5f : .48f + shot.Camera.Y + (shot.Ordinal % 2 == 1 ? .025f : -.025f)));
        anchor += new Vector2(offset.X * p.Width, offset.Y * p.Height);
        int firstInstance = p.Count;
        for (int layer = 0; layer < 2; layer++)
        {
        foreach (var text in shot.Text)
        {
            bool decorative = text.Role == "decoration";
            if (layer == 0 && decorative)
                continue;
            foreach (ref readonly var g in text.Glyphs.AsSpan())
            {
                if (time < g.Start)
                    continue;
                float reveal = Math.Clamp((float)((time - g.Start) / Math.Max(.08, g.Settle - g.Start)), 0, 1);
                reveal = reveal >= 1 ? 1 : 1 - MathF.Pow(2, -10 * reveal);
                float entry = 1 - reveal;
                float scale = (text.Role is "hero" or "semi-hero") && shot.Kind == "type-impact" ? .52f + reveal * .48f : .86f + reveal * .14f;
                Vector2 point = (g.Base + g.Enter * entry - focus) * cameraScale;
                Vector2 position = anchor + new Vector2(point.X * c - point.Y * s, point.X * s + point.Y * c);
                float font = text.Font * scale * cameraScale;
                if (layer == 0)
                {
                    bool emphasis = text.Role is "hero" or "semi-hero";
                    float merge = FoliaMotion.Bezier(.65f, 0, .35f, 1, reveal);
                    float distance = text.Font * (emphasis ? .025f : .01f) * (1 - merge * .8f) * scale * cameraScale;
                    float rotation = angle + text.Rotation + g.EntryRotation * entry;
                    float cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
                    var delta = new Vector2(distance * cos + distance * .5f * sin, distance * sin - distance * .5f * cos);
                    float alpha = (.16f + reveal * .84f) * (emphasis ? .8f : .5f);
                    p.Glyph(g.Glyph, position.X - delta.X - g.Glyph.Advance * font * .5f, position.Y - delta.Y - font * .6f,
                        font, primary * new Vector3(0, 1, 1), alpha, rotation, glow: 1);
                    p.Glyph(g.Glyph, position.X + delta.X - g.Glyph.Advance * font * .5f, position.Y + delta.Y - font * .6f,
                        font, primary * new Vector3(1, 0, 68f / 255), alpha, rotation, glow: 1);
                    continue;
                }
                p.Glyph(g.Glyph, position.X - g.Glyph.Advance * font * .5f, position.Y - font * .6f, font,
                    decorative ? accent : primary, decorative ? .12f : .16f + reveal * .84f, angle + text.Rotation + g.EntryRotation * entry,
                    glow: decorative ? 0 : 1, outline: decorative);
            }
        }
        if (layer == 0)
            ChromaticCount = p.Count - firstInstance;
        }
    }
}
