using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal static partial class FoliaLyricLayout
{
    private static void Monet(ref FoliaPainter p, FoliaTextAtlas atlas, int index, double time, float scale, Vector3 primary, Vector3 secondary, Vector3 accent)
    {
        // Match the original CSS flex columns, including the square portrait's left bleed.
        float large = 1 + .16f * Math.Clamp((p.Width - 1536) / 664, 0, 1);
        float rowWidth = Math.Min(p.Width, MathF.Round(1520 * large));
        float rowLeft = (p.Width - rowWidth) * .5f;
        float columnWidth = p.Width >= 768 ? Math.Clamp(p.Width * .28f, 220, MathF.Round(430 * large)) : 0;
        float inset = p.Width >= 1536 ? 80 : p.Width >= 1024 ? 56 : p.Width >= 640 ? 32 : 20;
        float textLeft = rowLeft + inset;
        float textWidth = Math.Max(1, rowWidth - columnWidth - inset * 2);
        if (columnWidth > 0)
        {
            float rightPadding = p.Width >= 1280 ? 48 : p.Width >= 1024 ? 40 : p.Width >= 640 ? 32 : 20;
            float innerWidth = Math.Min(columnWidth - 12 - rightPadding, Math.Clamp(p.Width * .26f, 210, MathF.Round(380 * large)));
            float cover = innerWidth * 1.35135f;
            float left = rowLeft + rowWidth - columnWidth + 12 - innerWidth * .35135f;
            float top = (p.Height - cover) * .5f;
            p.Shape(10, left, top, cover, cover, Vector3.One, 1, radius: 32, uv: new(0, 0, 1, 1));
            p.Shape(3, left + cover - 44, top - 12, 12, 56, new(.0549f, .06667f, .09412f), .86f, radius: 6);
        }
        float titleFont = Math.Clamp(p.Width * .033f, 23.2f, 44.8f * large);
        float artistFont = Math.Clamp(p.Width * .018f, 16, 28.8f * large);
        float reserve = columnWidth > 0 ? Math.Max(0, .35135f * Math.Clamp(p.Width * .26f, 210, MathF.Round(380 * large)) - 48) : 0;
        float headerWidth = Math.Max(192, textWidth - reserve);
        Span<int> titleRows = stackalloc int[3];
        int titleRowCount = MonetWrap(atlas.MonetTitle, titleFont, headerWidth, titleRows, atomicWords: false);
        float headerHeight = artistFont * 1.5f + 74;
        float titleHeight = titleFont * (1.06f * titleRowCount + .13f) + 48;
        float font = Math.Clamp(p.Width * .0275f, 21.44f, 36.48f) * 1.2f * scale * large;
        float inactiveFont = Math.Clamp(p.Width * .02f, 17.28f, 23.68f) * 1.2f * scale * large;
        float railHeight = Math.Clamp(p.Height * .52f, 280, MathF.Round(Math.Max(520 * large, font * 1.18f * 7)));
        float paddingY = p.Width >= 1024 ? 32 : p.Width >= 640 ? 24 : 20;
        float naturalHeight = headerHeight + titleHeight + railHeight + 50 + paddingY * 2;
        float contentTop = (p.Height - naturalHeight) * .5f + paddingY;
        float artistY = contentTop + CssLeading(atlas.Artist, artistFont, 1.5f);
        p.Text(atlas.Artist, textLeft, artistY, artistFont, primary, .96f, shear: -.12f);
        float stemTop = contentTop + artistFont * 1.5f + 6;
        for (int part = 0; part < 16; part++)
            p.Line(textLeft + .5f, stemTop + part * 3.5f, textLeft + .5f, stemTop + (part + 1) * 3.5f, primary, .72f * (1 - (part + .5f) / 16));
        float titleY = contentTop + headerHeight;
        for (int row = 0; row < titleRowCount; row++)
        {
            var glyphs = atlas.MonetTitle.AsSpan(titleRows[row], titleRows[row + 1] - titleRows[row]);
            float offset = glyphs.IsEmpty ? 0 : glyphs[0].Offset;
            foreach (ref readonly var glyph in glyphs)
                p.Glyph(glyph.Glyph, textLeft + (glyph.Offset - offset) * titleFont,
                    titleY + row * titleFont * 1.06f + CssLeading(glyphs, titleFont, 1.06f), titleFont, primary, 1);
        }
        float albumTop = titleY + titleFont * (titleRowCount * 1.06f + .13f) + 4;
        p.Text(atlas.MonetLabel, textLeft, albumTop + CssLeading(atlas.MonetLabel, 14, 20f / 14), 14, secondary, .84f);

        float railTop = contentTop + headerHeight + titleHeight;
        float glowBuffer = MathF.Round(font * 1.2f);
        float contentWidth = Math.Max(32, Math.Min(textWidth + glowBuffer * 2, MathF.Round(780 * large)) - glowBuffer * 2 - 8);
        int first = Math.Max(0, index - 2), last = Math.Min(atlas.Lines.Length - 1, index + 2);
        Span<float> heights = stackalloc float[5], sizes = stackalloc float[5], positions = stackalloc float[5];
        Span<int> rows = stackalloc int[15];
        float inactiveScale = Math.Clamp(inactiveFont / font, .72f, .92f);
        for (int i = first; i <= last; i++)
        {
            int distance = Math.Abs(i - index), slot = i - first;
            float factor = distance == 0 ? 1 : Math.Clamp(inactiveScale * MathF.Pow(.9f, distance - 1), .68f, .92f);
            int rowCount = MonetWrap(atlas.Lines[i].Glyphs, font, contentWidth, rows, atomicWords: true);
            rowCount = Math.Min(rowCount, distance == 0 ? 14 : 2);
            sizes[slot] = factor;
            heights[slot] = (rowCount * font * 1.18f + Math.Max(font * .16f, 8) + Math.Max(font * .34f, 14)) * factor;
        }
        int active = index - first;
        positions[active] = railHeight * .46f - heights[active] * .5f;
        for (int slot = active + 1; slot <= last - first; slot++)
            positions[slot] = positions[slot - 1] + heights[slot - 1] + Math.Max(slot == active + 1 ? 18 : 14, font * (slot == active + 1 ? .49f : .38f));
        for (int slot = active - 1; slot >= 0; slot--)
            positions[slot] = positions[slot + 1] - heights[slot] - Math.Max(slot == active - 1 ? 18 : 14, font * (slot == active - 1 ? .49f : .38f));
        int railFirst = p.Count;
        for (int i = first; i <= last; i++)
        {
            var line = atlas.Lines[i];
            int distance = Math.Abs(i - index), slot = i - first;
            float factor = sizes[slot], size = font * factor;
            float opacity = distance == 0 ? 1 : Math.Max(i > index ? .36f : .28f, (i > index ? .72f : .52f) - (distance - 1) * (i > index ? .18f : .12f));
            float blur = distance == 0 ? 0 : i > index ? distance == 1 ? .7f : 1.8f + (distance - 2) * .8f : 1.1f + (distance - 1) * .7f;
            int rowCount = MonetWrap(line.Glyphs, font, contentWidth, rows, atomicWords: true);
            rowCount = Math.Min(rowCount, distance == 0 ? 14 : 2);
            float top = railTop + positions[slot] + Math.Max(font * .16f, 8) * factor;
            for (int row = 0; row < rowCount; row++)
            {
                var glyphs = line.Glyphs.AsSpan(rows[row], rows[row + 1] - rows[row]);
                float offset = glyphs.IsEmpty ? 0 : glyphs[0].Offset;
                float y = top + row * size * 1.18f + CssLeading(glyphs, size, 1.18f);
                foreach (ref readonly var glyph in glyphs)
                {
                    float baseAlpha = i > index ? .46f : i < index && !line.Source.WordSynced ? .36f : i < index ? .98f : .34f;
                    float x = textLeft + (glyph.Offset - offset) * size;
                    p.Glyph(glyph.Glyph, x, y, size, primary, opacity * baseAlpha, blur: blur);
                    if (i == index)
                        p.Glyph(glyph.Glyph, x, y, size, primary, opacity * .98f, reveal: Progress(glyph, time), glow: .18f * Progress(glyph, time));
                }
            }
        }
        p.RailMaskFrom(railFirst, railTop, railTop + railHeight);
        float capsuleTop = railTop + railHeight + 16;
        float capsuleWidth = 32 + 10 + 12 + atlas.MonetLabel[^1].Offset * 12 + atlas.MonetLabel[^1].Glyph.Advance * 12;
        p.Shape(3, textLeft, capsuleTop, capsuleWidth, 34, new(.0549f, .06667f, .09412f), .18f, radius: 17);
        p.Shape(16, textLeft + 16, capsuleTop + 12, 10, 10, accent, 1);
        p.Text(atlas.MonetLabel, textLeft + 38, capsuleTop + 8 + CssLeading(atlas.MonetLabel, 12, 16f / 12), 12, primary, .9f);
    }

    private static float CssLeading(ReadOnlySpan<FoliaPreparedGlyph> glyphs, float font, float lineHeight)
        => glyphs.IsEmpty ? 0 : (lineHeight - glyphs[0].Glyph.LineHeight) * font * .5f;

    // Rows break between timed tokens; a multi-character syllable remains an atomic inline box.
    private static int MonetWrap(ReadOnlySpan<FoliaPreparedGlyph> glyphs, float font, float width, Span<int> starts, bool atomicWords)
    {
        int count = 1, first = 0;
        starts[0] = 0;
        for (int i = 0; i < glyphs.Length;)
        {
            int end = i + 1;
            if (atomicWords)
                while (end < glyphs.Length && glyphs[end].Word == glyphs[i].Word)
                    end++;
            float span = (glyphs[end - 1].Offset + glyphs[end - 1].Glyph.Advance - glyphs[first].Offset) * font;
            if (span > width && i > first)
            {
                if (count == starts.Length - 1)
                {
                    starts[count] = i;
                    return count;
                }
                starts[count++] = first = i;
            }
            i = end;
        }
        starts[count] = glyphs.Length;
        return count;
    }
}
