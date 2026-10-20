using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal static partial class FoliaLyricLayout
{
    private static void Cappella(ref FoliaPainter p, FoliaTextAtlas atlas, int index, double time, float scale, Vector3 themeBackground, Vector3 primary, Vector3 secondary, Vector3 accent, FoliaChatMotion motion)
    {
        var lines = atlas.Lines;
        motion.Begin(time);
        float baseFont = Math.Clamp(18 * scale, 15, 26);
        float panel = Math.Min(Math.Max(p.Width - 32, 1), 896);
        float maxText = Math.Max(96, MathF.Floor(panel * (p.Width >= 640 ? .68f : .78f) - 56));
        int first = Math.Max(0, index - 19), count = index - first + 1;
        Span<Vector4> metrics = stackalloc Vector4[count];
        Span<float> rawHeights = stackalloc float[count], fullHeights = stackalloc float[count];
        for (int i = first; i <= index; i++)
        {
            bool active = i == index;
            float rawFont = baseFont * (active ? 1.34f : .94f);
            float font = rawFont * (active ? 1.12f : .92f);
            float groupScale = active ? 1.12f : .92f;
            float paddingX = (active ? 20 : 16) * groupScale, paddingY = (active ? 16 : 12) * groupScale;
            float rowWidth = 0, longest = 0;
            int rows = 1;
            foreach (ref readonly var glyph in lines[i].Glyphs.AsSpan())
            {
                if (glyph.Start > time + .2)
                    break;
                float advance = glyph.Glyph.Advance * font;
                if (rowWidth > 0 && (rowWidth + advance) / groupScale > maxText)
                {
                    rows++;
                    longest = Math.Max(longest, rowWidth);
                    rowWidth = 0;
                }
                rowWidth += advance;
            }
            longest = Math.Max(longest, rowWidth);
            float width = MathF.Ceiling((Math.Max(font, longest) + paddingX * 2) / groupScale + 2) * groupScale;
            float rawHeight = Math.Max(active ? 64 : 44, MathF.Ceiling((rows * font * 1.45f + paddingY * 2) / groupScale + 2));
            float height = rawHeight * groupScale;
            int fullRows = 1;
            float fullWidth = 0;
            foreach (ref readonly var glyph in lines[i].Glyphs.AsSpan())
            {
                float advance = glyph.Glyph.Advance * rawFont;
                if (fullWidth > 0 && fullWidth + advance > maxText)
                {
                    fullRows++;
                    fullWidth = 0;
                }
                fullWidth += advance;
            }
            rawHeights[i - first] = rawHeight;
            fullHeights[i - first] = Math.Max(active ? 64 : 44, MathF.Ceiling(fullRows * rawFont * 1.45f + (active ? 16 : 12) * 2 + 2));
            if (lines[i].Source.Text == "......")
                width = height = active ? 160 : 110;
            metrics[i - first] = new(width, height, font, paddingX);
        }
        float usable = Math.Max(200, p.Height - 240), used = 0;
        int selected = index;
        for (int i = index; i >= first; i--)
        {
            float fullHeight = fullHeights[i - first];
            float estimated = fullHeight + (i == index ? MathF.Ceiling(fullHeight * .12f) : 0) + 12;
            if (used + estimated > usable && index - selected + 1 >= 2)
                break;
            selected = i;
            used += estimated;
        }
        float y = p.Width >= 1024 ? 80 : p.Width >= 640 ? 64 : 48;
        for (int i = first; i < selected; i++)
            y -= rawHeights[i - first] + 12;
        for (int i = first; i <= index; i++)
        {
            int firstInstance = p.Count;
            Vector4 metric = metrics[i - first];
            float overflow = i == index ? MathF.Ceiling(Math.Max(fullHeights[i - first], 40) * .12f) : 0;
            float paintY = y + overflow + rawHeights[i - first] - metric.Y;
            var sender = atlas.ChatSenders[i];
            bool right = sender.Right;
            float edge = (p.Width - panel) * .5f;
            float groupScale = i == index ? 1.12f : .92f;
            float avatar = 40 * groupScale, gap = 12 * groupScale;
            float x = right ? p.Width - edge - avatar - gap - metric.X : edge + avatar + gap;
            float alpha = i == index ? 1 : .82f;
            if (lines[i].Source.Text == "......")
            {
                var emoji = FoliaAssets.Emojis;
                int choice = (int)(lines[i].Source.Start * 31 + i * 17) % emoji.Rects.Length;
                p.Shape(13, x, paintY, metric.X, metric.Y, Vector3.One, alpha, uv: emoji.Rects[choice]);
            }
            else
            {
                float mix = .18f + sender.Avatar / 8f * .62f;
                Vector3 background = right ? Vector3.Lerp(accent, primary, .18f) : Vector3.Lerp(secondary, accent, mix);
                p.Shape(24, x, paintY, metric.X, metric.Y, background, alpha * (right ? .94f : 1), radius: 24 * groupScale, phase: right ? 1 : 0);
                float dx = 0, dy = 0;
                foreach (ref readonly var glyph in lines[i].Glyphs.AsSpan())
                {
                    float advance = glyph.Glyph.Advance * metric.Z;
                    if (dx > 0 && (dx + advance) / groupScale > maxText)
                    {
                        dx = 0;
                        dy += metric.Z * 1.45f;
                    }
                    float reveal = FoliaPainter.Ease((float)(time - glyph.Start) / .22f);
                    p.Glyph(glyph.Glyph, x + metric.W + dx, paintY + (i == index ? 16 : 12) * groupScale + dy + (1.45f - glyph.Glyph.LineHeight) * metric.Z * .5f,
                        metric.Z, right ? themeBackground : primary, alpha * reveal);
                    dx += advance;
                }
            }
            float avatarX = right ? x + metric.X + gap : x - avatar - gap;
            int source = sender.Avatar;
            p.Shape(11, avatarX, paintY + metric.Y - avatar, avatar, avatar, Vector3.One, alpha,
                uv: new(source % 3 / 3f, source / 3 / 3f, 1f / 3, 1f / 3));
            if (i < index || time >= lines[i].Source.End)
            {
                float size = 11 * groupScale, labelWidth = atlas.ChatTimestampWidths[i] * size;
                float labelX = right ? x - 8 * groupScale - labelWidth : x + metric.X + 8 * groupScale;
                p.Text(atlas.ChatTimestamps[i], labelX, paintY + metric.Y - 4 * groupScale - size * 1.25f, size, secondary, .62f * alpha);
            }
            motion.Apply(ref p, firstInstance, i, new(edge, paintY, panel, metric.Y), i >= selected);
            y += rawHeights[i - first] + overflow + 12;
        }
    }
}
