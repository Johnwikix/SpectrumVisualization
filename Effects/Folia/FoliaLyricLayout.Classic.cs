using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal static partial class FoliaLyricLayout
{
    private static void Classic(ref FoliaPainter p, FoliaPreparedLine line, double time, float animation,
        float fontScale, Vector3 primary, Vector3 accent, FoliaGlowWords motion)
    {
        int count = Math.Min(256, line.Source.Words.Length);
        if (count == 0)
            return;
        Span<float> widths = stackalloc float[count];
        Span<float> offsets = stackalloc float[count];
        Span<Vector4> boxes = stackalloc Vector4[count];
        widths.Clear();
        offsets.Fill(float.NaN);
        float font = Math.Clamp(p.Width * .06f, 36, 72) * fontScale;
        float available = Math.Max(32, Math.Min(1152, p.Width - 64));
        foreach (ref readonly var glyph in line.Glyphs.AsSpan())
        {
            if (glyph.Word >= count)
                continue;
            if (float.IsNaN(offsets[glyph.Word]))
                offsets[glyph.Word] = glyph.Offset;
            widths[glyph.Word] = (glyph.Offset + glyph.Glyph.Advance - offsets[glyph.Word]) * font;
        }
        int rowStart = 0, row = 0;
        float rowWidth = 0;
        for (int word = 0; word < count; word++)
        {
            double seed = line.Source.Start + word;
            float size = 1.1f + FoliaPainter.Random(seed, 4) * .2f;
            float nextSize = 1.1f + FoliaPainter.Random(seed + 1, 4) * .2f;
            float x = (FoliaPainter.Random(seed, 1) - .5f) * 40;
            float nextX = word + 1 < count ? (FoliaPainter.Random(seed + 1, 1) - .5f) * 40 : 0;
            float nextWidth = word + 1 < count ? widths[word + 1] : 0;
            float margin = Math.Max(font * .12f * .7f,
                ((widths[word] * (size * 1.4f - 1) + nextWidth * (nextSize * 1.4f - 1)) * .5f + x - nextX + font * .05f) * .7f);
            if (word > rowStart && rowWidth + widths[word] + margin > available)
            {
                PlaceRow(boxes, rowStart, word, rowWidth, available, row, line.Source.Start);
                row++;
                rowStart = word;
                rowWidth = 0;
            }
            boxes[word] = new(rowWidth, row, widths[word], margin);
            rowWidth += widths[word] + margin;
        }
        PlaceRow(boxes, rowStart, count, rowWidth, available, row, line.Source.Start);
        float rowHeight = font * 1.22f;
        float top = (p.Height - (row + 1) * rowHeight) * .5f;
        float left = (p.Width - available) * .5f;
        float presence = Presence(line.Source, time);
        int firstInstance = p.Count;
        foreach (ref readonly var glyph in line.Glyphs.AsSpan())
        {
            int wordIndex = glyph.Word;
            if (wordIndex >= count)
                continue;
            var word = line.Source.Words[wordIndex];
            double seed = line.Source.Start + wordIndex;
            float baseScale = 1.1f + FoliaPainter.Random(seed, 4) * .2f;
            var pose = motion.Resolve(wordIndex, word.Start, word.End, time,
                new((FoliaPainter.Random(seed, 1) - .5f) * 40, (FoliaPainter.Random(seed, 2) - .5f) * 40),
                (FoliaPainter.Random(seed, 3) - .5f) * 10 * MathF.PI / 180, baseScale,
                (FoliaPainter.Random(seed, 8) - .5f) * 45 * MathF.PI / 180);
            if (pose.Alpha <= .001f)
                continue;
            float sizeScale = pose.Scale, rotation = pose.Rotation;
            Vector4 box = boxes[wordIndex];
            float centerX = left + box.X + box.Z * .5f + pose.Offset.X;
            float centerY = top + box.Y * rowHeight + rowHeight * .5f + pose.Offset.Y;
            float localX = ((glyph.Offset - offsets[wordIndex] + glyph.Glyph.BearingX + glyph.Glyph.Width * .5f) * font - box.Z * .5f) * sizeScale;
            float localY = ((glyph.Glyph.BearingY + glyph.Glyph.Height * .5f + (1.22f - glyph.Glyph.LineHeight) * .5f) * font - rowHeight * .5f) * sizeScale;
            float c = MathF.Cos(rotation), s = MathF.Sin(rotation), size = font * sizeScale;
            float x = centerX + localX * c - localY * s - (glyph.Glyph.BearingX + glyph.Glyph.Width * .5f) * size;
            float y = centerY + localX * s + localY * c - (glyph.Glyph.BearingY + glyph.Glyph.Height * .5f) * size;
            p.Glyph(glyph.Glyph, x, y, size, Vector3.Lerp(primary, accent, pose.ColorMix), pose.Alpha * presence,
                rotation, pose.Blur, pose.Glow);

        }
        FoliaGlowWords.Breathe(ref p, firstInstance, animation);
    }

    private static void PlaceRow(Span<Vector4> boxes, int first, int end, float used, float available, int row, double start)
    {
        int justify = (int)Math.Floor(start % 5);
        int count = end - first;
        float slack = Math.Max(0, available - used);
        float leading = justify switch
        {
            1 => slack * .5f,
            2 => slack,
            3 => slack / Math.Max(1, count) * .5f,
            _ => 0
        };
        float gap = justify == 3 ? slack / Math.Max(1, count) : justify == 4 ? slack / Math.Max(1, count - 1) : 0;
        for (int i = first; i < end; i++)
            boxes[i] = boxes[i] with
            {
                X = boxes[i].X + leading + (i - first) * gap,
                Y = row
            };
    }
}
