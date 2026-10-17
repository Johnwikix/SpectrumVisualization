using System;
using System.Threading;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original article placement and camera focus compiled to immutable native arrays.</summary>
internal sealed partial class FoliaFume
{
    private sealed record Row(int First, int End, Vector2 Position, float[] Offsets);
    private sealed record Block(int Line, Vector2 Position, Vector2 Size, float Font, float LineHeight, bool Hero, Row[] Rows);
    private long _atlasIdentity;
    private float _width, _height, _scale;
    private Block[] _blocks = [];
    private Vector3[] _camera = [];
    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float scale, string family, int weight, CancellationToken token = default)
    {
        if (atlas.Identity == _atlasIdentity && width == _width && height == _height && scale == _scale)
            return;
        string output = FoliaSourcePlanner.Compile(FoliaSourcePlanner.CreateInput(atlas, width, height, scale, family, weight), "compileFume", token);
        using var document = JsonDocument.Parse(output);
        var blocks = new List<Block>();
        var camera = new List<Vector3>();
        foreach (var c in document.RootElement.GetProperty("camera").EnumerateArray())
            camera.Add(new(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle()));
        foreach (var b in document.RootElement.GetProperty("blocks").EnumerateArray())
        {
            var rows = new List<Row>();
            foreach (var row in b.GetProperty("rows").EnumerateArray())
            {
                var values = row.GetProperty("offsets");
                var offsets = new float[values.GetArrayLength()];
                for (int i = 0; i < offsets.Length; i++)
                    offsets[i] = values[i].GetSingle();
                rows.Add(new(row.GetProperty("start").GetInt32(), row.GetProperty("end").GetInt32(), new(F(row, "left"), F(row, "top")), offsets));
            }
            blocks.Add(new(b.GetProperty("index").GetInt32(), new(F(b, "x"), F(b, "y")), new(F(b, "width"), F(b, "height")), F(b, "font"), F(b, "lineHeight"), b.GetProperty("hero").GetBoolean(), rows.ToArray()));
        }
        ReadBackground(document.RootElement);
        _blocks = blocks.ToArray();
        _camera = camera.ToArray();
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _scale = scale;
    }
    private static float F(JsonElement value, string field) => value.GetProperty(field).GetSingle();
    internal void Write(ref FoliaPainter p, FoliaTextAtlas atlas, int index, double time, Vector3 primary, Vector3 secondary, Vector3 accent)
    {
        if (_camera.Length == 0)
            return;
        float phase = Math.Clamp((float)time * 20, 0, _camera.Length - 1);
        int first = Math.Min((int)phase, _camera.Length - 2);
        Vector3 camera = Vector3.Lerp(_camera[first], _camera[first + 1], phase - first);
        WriteBackground(ref p, camera, time, secondary, accent);
        foreach (var block in _blocks)
        {
            Vector2 origin = new(p.Width * .5f + (block.Position.X - camera.X) * camera.Z, p.Height * .5f + (block.Position.Y - camera.Y) * camera.Z);
            if (origin.X + block.Size.X * camera.Z < -180 || origin.Y + block.Size.Y * camera.Z < -180 || origin.X > p.Width + 180 || origin.Y > p.Height + 180)
                continue;
            var line = atlas.Lines[block.Line];
            float font = block.Font * camera.Z;
            if (time >= line.Source.Start && time <= line.Source.End)
            {
                float phaseLine = Math.Clamp((float)((time - line.Source.Start) / Math.Max(.18, line.Source.End - line.Source.Start)), 0, 1);
                float envelope = phaseLine <= .8f ? 1 - MathF.Pow(1 - phaseLine / .8f, 3) : 1 - MathF.Pow((phaseLine - .8f) / .2f, 3);
                float glowAlpha = (block.Hero ? .16f : .12f) + envelope * (block.Hero ? .26f : .2f);
                foreach (var glowRow in block.Rows)
                    for (int gi = glowRow.First; gi < Math.Min(glowRow.End, line.Glyphs.Length); gi++)
                    {
                        ref readonly var glyph = ref line.Glyphs[gi];
                        Vector2 point = origin + (glowRow.Position + new Vector2(glowRow.Offsets[gi - glowRow.First], 0)) * camera.Z;
                        p.GlyphCentered(glyph.Glyph, point.X + glyph.Glyph.Advance * font * .5f,
                            point.Y + block.LineHeight * camera.Z * .52f, font, accent, glowAlpha, glow: 1);
                    }
            }
            foreach (var row in block.Rows)
                for (int gi = row.First; gi < Math.Min(row.End, line.Glyphs.Length); gi++)
                {
                    ref readonly var glyph = ref line.Glyphs[gi];
                    double start = glyph.Start, end = glyph.End;
                    if (!line.Source.WordSynced)
                    {
                        double duration = Math.Max(.08, line.Source.End - line.Source.Start);
                        start = line.Source.Start + gi * duration / Math.Max(1, line.Glyphs.Length);
                        end = line.Source.Start + (gi + 1) * duration / Math.Max(1, line.Glyphs.Length);
                    }
                    double durationGlyph = Math.Max(.001, end - start), trailStart = start + durationGlyph * .18;
                    bool waiting = time < start;
                    float activeAlpha = block.Hero ? .985f : .92f, waitingAlpha = block.Hero ? .06f : .035f;
                    float passedAlpha = block.Hero ? .74f : .58f;
                    float span = Math.Clamp((float)(line.Source.End - line.Source.Start) * (block.Hero ? .42f : .52f), .45f, 1.45f);
                    float trail = MathF.Pow(Math.Clamp((float)(time - trailStart) / span, 0, 1), 1.35f);
                    float progress = Math.Clamp((float)((time - start) / durationGlyph) + .16f, 0, 1);
                    float enter = 1 - MathF.Pow(1 - progress, 3);
                    float opacity = waiting ? waitingAlpha : time <= trailStart ? waitingAlpha + (activeAlpha - waitingAlpha) * enter : activeAlpha + (passedAlpha - activeAlpha) * trail;
                    Vector3 color = waiting ? primary : Vector3.Lerp(accent, primary, .18f + trail * .82f);
                    Vector2 xy = origin + (row.Position + new Vector2(row.Offsets[gi - row.First], 0)) * camera.Z;
                    float baseline = xy.Y + block.LineHeight * camera.Z * .52f;
                    float glow = waiting ? 0 : (time <= trailStart ? .84f * enter : .1f + .16f * (1 - trail));
                    p.GlyphCentered(glyph.Glyph, xy.X + glyph.Glyph.Advance * font * .5f, baseline, font, color, opacity, glow: glow);
                    float lead = Math.Clamp((float)Math.Min(durationGlyph * .86, (line.Source.End - line.Source.Start) * .16), .055f, block.Hero ? .2f : .16f);
                    double activation = trailStart - lead, finish = trailStart + lead * .42;
                    if (time > activation && time < finish)
                    {
                        bool dropping = time <= trailStart;
                        float drop = dropping ? 1 - MathF.Pow(1 - Math.Clamp((float)(time - activation) / lead, 0, 1), 3) : 1;
                        float fade = FoliaPainter.Ease((float)(time - trailStart) / (lead * .42f));
                        float pulse = dropping ? .18f + .82f * MathF.Pow(drop, .78f) : MathF.Pow(1 - fade, 1.2f);
                        float width = Math.Max(glyph.Glyph.Advance * font * .88f, font * .56f) + font * (block.Hero ? .18f : .12f);
                        float height = font * (block.Hero ? .72f : .62f);
                        float centerY = baseline - font * .38f - block.LineHeight * camera.Z * (block.Hero ? .24f : .2f) * (1 - drop);
                        p.Shape(1, xy.X + glyph.Glyph.Advance * font * .5f - width * .5f, centerY - height * .5f, width, height, accent, pulse * (block.Hero ? .82f : .72f));
                    }
                }
        }
    }
}
