using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original semantic chunk planning on the cold path; native shared word transitions per frame.</summary>
internal sealed class FoliaPartita
{
    private readonly record struct Word(int First, int End, int State, double Start, double Finish);
    private sealed record Chunk(Vector2 Position, float Scale, Word[] Words);
    private sealed record Line(float Density, Chunk[] Chunks);
    private long _atlasIdentity;
    private Line[] _lines = [];
    private float _width, _height, _scale;

    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float scale, string family, int weight)
    {
        if (atlas.Identity == _atlasIdentity && width == _width && height == _height && scale == _scale)
            return;
        using var document = JsonDocument.Parse(FoliaSourcePlanner.Compile(FoliaSourcePlanner.CreateInput(atlas, width, height, scale, family, weight), "compilePartita"));
        var layouts = new List<Line>();
        int li = 0;
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var chunks = new List<Chunk>();
            var glyphs = atlas.Lines[li++].Glyphs;
            foreach (var chunk in row.GetProperty("chunks").EnumerateArray())
            {
                var words = new List<Word>();
                foreach (var word in chunk.GetProperty("words").EnumerateArray())
                {
                    int firstWord = word.GetProperty("first").GetInt32(), endWord = word.GetProperty("end").GetInt32();
                    int first = 0;
                    while (first < glyphs.Length && glyphs[first].Word < firstWord)
                        first++;
                    int end = first;
                    while (end < glyphs.Length && glyphs[end].Word < endWord)
                        end++;
                    if (end > first)
                        words.Add(new(first, end, firstWord, word.GetProperty("start").GetDouble(), word.GetProperty("finish").GetDouble()));
                }
                var config = chunk.GetProperty("config");
                chunks.Add(new(new(config.GetProperty("x").GetSingle(), config.GetProperty("y").GetSingle()), config.GetProperty("scale").GetSingle(), words.ToArray()));
            }
            layouts.Add(new(row.GetProperty("graphemes").GetInt32() > 40 ? .8f : 1, chunks.ToArray()));
        }
        _lines = layouts.ToArray();
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _scale = scale;
    }
    internal void Write(ref FoliaPainter p, FoliaPreparedLine line, int index, double time, float animation, float scale, Vector3 primary, Vector3 accent, FoliaGlowWords motion)
    {
        var layout = _lines[index];
        float font = Math.Clamp(p.Width * .055f, 40, 72) * scale * layout.Density;
        float lineHeight = font * 1.22f, rowHeight = lineHeight + 16;
        float top = (p.Height - layout.Chunks.Length * rowHeight) * .5f;
        int firstInstance = p.Count;
        for (int row = 0; row < layout.Chunks.Length; row++)
        {
            var chunk = layout.Chunks[row];
            float width = 16;
            foreach (var word in chunk.Words)
                width += (line.Glyphs[word.End - 1].Offset + line.Glyphs[word.End - 1].Glyph.Advance - line.Glyphs[word.First].Offset) * font + 12.8f;
            float left = (p.Width - width) * .5f + chunk.Position.X;
            float y = top + row * rowHeight + chunk.Position.Y;
            if (chunk.Words.Length > 0 && time >= chunk.Words[0].Start - .15)
            {
                bool passed = time > chunk.Words[^1].Finish;
                Vector3 guide = passed ? Vector3.One : accent;
                float alpha = passed ? .22f : 1;
                float x = row % 2 == 0 ? left - 8 : left + width + 8;
                p.Line(x, y + lineHeight + 6.4f - 16, x, y + lineHeight + 6.4f + 16, guide, alpha);
                p.Line(left - 16, y + lineHeight + 6.4f + 8, left + width + 20, y + lineHeight + 6.4f + 8, guide, alpha);
            }
            float xCursor = left + 8;
            for (int wi = 0; wi < chunk.Words.Length; wi++)
            {
                var word = chunk.Words[wi];
                float offset = line.Glyphs[word.First].Offset;
                float span = (line.Glyphs[word.End - 1].Offset + line.Glyphs[word.End - 1].Glyph.Advance - offset) * font;
                double seed = chunk.Words[0].Start + wi * 7.13;
                var pose = motion.Resolve(word.State, word.Start, word.Finish, time,
                    new((FoliaPainter.Random(seed, 1) - .5f) * 12, (FoliaPainter.Random(seed, 2) - .5f) * 12),
                    (FoliaPainter.Random(seed, 3) - .5f) * 6 * MathF.PI / 180, chunk.Scale,
                    (FoliaPainter.Random(seed, 8) - .5f) * 20 * MathF.PI / 180);
                float cos = MathF.Cos(pose.Rotation), sin = MathF.Sin(pose.Rotation), size = font * pose.Scale;
                float centerX = xCursor + span * .5f + pose.Offset.X, centerY = y + 3.2f + lineHeight * .5f + pose.Offset.Y;
                for (int gi = word.First; gi < word.End; gi++)
                {
                    ref readonly var glyph = ref line.Glyphs[gi];
                    float localX = ((glyph.Offset - offset + glyph.Glyph.BearingX + glyph.Glyph.Width * .5f) * font - span * .5f) * pose.Scale;
                    float localY = ((glyph.Glyph.BearingY + glyph.Glyph.Height * .5f + (1.22f - glyph.Glyph.LineHeight) * .5f) * font - lineHeight * .5f) * pose.Scale;
                    float cx = centerX + localX * cos - localY * sin, cy = centerY + localX * sin + localY * cos;
                    p.Glyph(glyph.Glyph, cx - (glyph.Glyph.BearingX + glyph.Glyph.Width * .5f) * size,
                        cy - (glyph.Glyph.BearingY + glyph.Glyph.Height * .5f) * size, size, Vector3.Lerp(primary, accent, pose.ColorMix), pose.Alpha,
                        pose.Rotation, pose.Blur, pose.Glow);
                }
                xCursor += span + 12.8f;
            }
        }
        FoliaGlowWords.Breathe(ref p, firstInstance, animation);
    }
}
