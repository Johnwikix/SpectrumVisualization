using System;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
namespace WinExSpectrumTest.Effects.Folia;

internal sealed class FoliaTilt
{
    private readonly record struct Segment(int First, int End, bool Tilt, bool Short);
    private sealed record Layout(float Scale, Segment[] Segments);
    private long _atlasIdentity;
    private float _width, _height, _scale;
    private Layout[] _layouts = [];
    internal HashSet<string> RequiredItalicCharacters(FoliaTextAtlas atlas)
    {
        var required = new HashSet<string>(StringComparer.Ordinal);
        for (int li = 0; li < _layouts.Length; li++)
        {
            // Match the atlas's per-word grapheme indexing, including combining sequences
            // split by a parser word boundary. Only glyphs a tilted segment draws are needed.
            var texts = new List<string>(atlas.Lines[li].Glyphs.Length);
            foreach (var word in atlas.Lines[li].Source.Words)
            {
                var elements = StringInfo.GetTextElementEnumerator(word.Text);
                while (elements.MoveNext())
                {
                    string text = elements.GetTextElement();
                    if (atlas.CharacterGlyphs.ContainsKey(text)) texts.Add(text);
                }
            }
            foreach (var segment in _layouts[li].Segments)
                if (segment.Tilt)
                    for (int i = segment.First; i < segment.End; i++) required.Add(texts[i]);
        }
        return required;
    }
    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float scale, string family, int weight, CancellationToken token = default)
    {
        if (atlas.Identity == _atlasIdentity && _width == width && _height == height && _scale == scale)
            return;
        using var document = JsonDocument.Parse(FoliaSourcePlanner.Compile(FoliaSourcePlanner.CreateInput(atlas, width, height, scale, family, weight), "compileTilt", token));
        var layouts = new List<Layout>();
        int li = 0;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var segments = new List<Segment>();
            int[] offsets = StringInfo.ParseCombiningCharacters(atlas.Lines[li].Source.Text);
            foreach (var segment in item.GetProperty("segments").EnumerateArray())
            {
                int offset = segment.GetProperty("charOffset").GetInt32();
                int first = Array.BinarySearch(offsets, offset);
                if (first < 0)
                    first = ~first;
                int count = StringInfo.ParseCombiningCharacters(segment.GetProperty("text").GetString()!).Length;
                segments.Add(new(first, Math.Min(first + count, atlas.Lines[li].Glyphs.Length), segment.GetProperty("isTilt").GetBoolean(), segment.GetProperty("isShortLastLine").GetBoolean()));
            }
            layouts.Add(new(item.GetProperty("scaleMultiplier").GetSingle(), segments.ToArray()));
            li++;
        }
        _layouts = layouts.ToArray();
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _scale = scale;
    }
    internal void Write(ref FoliaPainter p, FoliaPreparedLine line, int index, double time, float scale, Vector3 primary, Vector3 accent)
    {
        if ((uint)index >= (uint)_layouts.Length)
            return;
        var layout = _layouts[index];
        float font = Math.Clamp(p.Width * .06875f, 50, 90) * scale * layout.Scale;
        float gap = p.Width >= 640 ? 16 : 12, total = -gap;
        foreach (ref readonly var segment in layout.Segments.AsSpan())
            total += font * (segment.Short && !segment.Tilt ? 1.18f : 1) * (segment.Tilt ? 1.25f : 1.35f) + gap;
        float y = (p.Height - total) * .5f;
        foreach (ref readonly var segment in layout.Segments.AsSpan())
        {
            float size = font * (segment.Short && !segment.Tilt ? 1.18f : 1);
            float height = size * (segment.Tilt ? 1.25f : 1.35f);
            if (segment.First >= segment.End)
            {
                y += height + gap;
                continue;
            }
            int word = Math.Clamp(line.Glyphs[segment.First].Word, 0, line.Source.Words.Length - 1);
            float start = (float)line.Source.Words[word].Start - .25f;
            float presence = FoliaMotion.Bezier(.25f, .46f, .45f, .94f, (float)(time - start) / (segment.Tilt ? .6f : .55f));
            float spacing = segment.Tilt ? .15f : .08f, width = 0;
            for (int i = segment.First; i < segment.End; i++)
                width += ((segment.Tilt ? line.TiltItalic[i] : line.Glyphs[i].Glyph).Advance + spacing) * size;
            float left = (p.Width - width) * .5f;
            for (int i = segment.First; i < segment.End; i++)
            {
                ref readonly var glyph = ref line.Glyphs[i];
                var shape = segment.Tilt ? line.TiltItalic[i] : glyph.Glyph;
                int local = i - segment.First;
                float reveal = FoliaMotion.Bezier(.25f, .46f, .45f, .94f,
                    ((float)(time - start) - local * (segment.Tilt ? .05f : .04f)) / .5f);
                float elapsed = (float)(time - glyph.Start), duration = Math.Clamp((float)(glyph.End - glyph.Start), .2f, .9f);
                float pulse = elapsed < 0 ? 0 : elapsed <= duration ? MathF.Sin(elapsed / duration * MathF.PI)
                    : .25f * Math.Clamp((elapsed - duration) / (duration * 1.2f), 0, 1);
                float glyphScale = 1 + pulse * (segment.Tilt ? .18f : .15f);
                float groupScale = segment.Tilt ? .92f + .08f * presence : 1;
                float advance = (shape.Advance + spacing) * size;
                float dy = segment.Tilt ? (local % 2 == 0 ? -1 : 1) * font / 6 * (2 - reveal) : 0;
                float cx = left + advance * .5f, cy = y + height * .5f;
                float x = cx - advance * glyphScale * .5f;
                float top = cy - shape.LineHeight * size * glyphScale * .5f + dy + (1 - presence) * (segment.Tilt ? 24 : 20);
                int first = p.Count;
                p.Glyph(shape, x, top, size * glyphScale, glyph.ActiveColor ?? (segment.Tilt ? accent : primary), presence * reveal);
                if (groupScale < 1)
                    p.TransformFrom(first, new(p.Width * .5f, y + height * .5f), new(p.Width * .5f, y + height * .5f), groupScale, 0);
                left += advance;
            }
            y += height + gap;
        }
    }
}
