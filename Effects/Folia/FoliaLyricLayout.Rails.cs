using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal static partial class FoliaLyricLayout
{
    private static void Claddagh(ref FoliaPainter p, FoliaPreparedLine[] lines, int index, double time, float animation, float scale, Vector3 primary, Vector3 accent, float energy)
    {
        // CladdaghRingLine: project the narrow ellipse, then rotate its major axis -45 degrees.
        float radius = Math.Min(p.Width * .44f, 560), font = 72 * scale;
        float turn = index * MathF.PI;
        float entry = FoliaPainter.Ease((float)(time - lines[index].Source.Start) / .34f);
        turn -= (1 - entry) * MathF.PI;
        float activeEnd = (float)Math.Max(lines[index].Source.End,
            index + 1 < lines.Length ? lines[index + 1].Source.Start - .34 : lines[index].Source.End + 10);
        float activeOffset = RingWordOffset(lines[index], time, activeEnd, font, radius);
        float playback = Math.Clamp((float)((time - lines[index].Source.Start) / Math.Max(.2, activeEnd - lines[index].Source.Start)), 0, 1);
        float orbit = MathF.PI * .52f * (1 - MathF.Pow(1 - playback, 1.35f));
        const float diagonal = .70710678f;
        float axis = Math.Min(p.Width, p.Height) * .3f;
        p.Line(p.Width * .5f - axis * diagonal, p.Height * .5f - axis * diagonal,
            p.Width * .5f + axis * diagonal, p.Height * .5f + axis * diagonal, primary, .2f);
        for (int i = Math.Max(0, index - 1); i <= Math.Min(lines.Length - 1, index + 2); i++)
        {
            var line = lines[i];
            float difference = Math.Abs(turn - i * MathF.PI) / MathF.PI;
            float end = (float)Math.Max(line.Source.End, i + 1 < lines.Length ? lines[i + 1].Source.Start - .34 : line.Source.End + 10);
            float offset = RingWordOffset(line, time, end, font, radius);
            if (i >= index)
                offset += (activeOffset * .28f + orbit) * (i > index ? 1 : Math.Clamp(difference, 0, 1));
            float total = (line.Width + Math.Max(0, line.Glyphs.Length - 1) * .22f) * font;
            float fit = Math.Min(1, 4.25f * radius / Math.Max(total, 1));
            float lengthFade = 1, lengthScale = 1;
            if (i > index && lines[index].Glyphs.Length > 10)
            {
                float strength = Math.Clamp((lines[index].Glyphs.Length - 10) / 8f, 0, 1)
                    * Math.Clamp((line.Glyphs.Length - 5) / 5f, .4f, 1);
                float blend = Math.Clamp((difference - .4f) / .5f, 0, 1);
                lengthFade -= strength * blend;
                lengthScale -= strength * .25f * blend;
            }
            foreach (ref readonly var glyph in line.Glyphs.AsSpan())
            {
                float nominal = ((glyph.Offset + glyph.Index * .22f + glyph.Glyph.Advance * .5f) * font - total * .5f) / radius * fit;
                float theta = i * MathF.PI + nominal - turn - offset;
                float cosine = MathF.Cos(theta), sine = MathF.Sin(theta), depth = (cosine + 1) * .5f;
                float rawX = sine * radius * (.35f + .65f * MathF.Pow(depth, 1.2f));
                float rawY = cosine * radius * .09f;
                if (line.Source.IsChorus)
                    rawY += (glyph.Index % 2 == 0 ? 1 : -1) * font * (.06f + energy * .12f);
                float x = (rawX + rawY) * diagonal, y = (rawY - rawX) * diagonal;
                float tangent = MathF.Atan2((-cosine - sine * .09f) * diagonal, (cosine - sine * .09f) * diagonal);
                while (tangent > MathF.PI * .5f)
                    tangent -= MathF.PI;
                while (tangent < -MathF.PI * .5f)
                    tangent += MathF.PI;
                float focus = Math.Max(0, 1 - difference) * MathF.Pow(1 - Math.Min(1, Math.Abs(theta) / .48f), 1.8f);
                float opacity = (.35f + .65f * MathF.Pow(depth, 1.5f) * (.35f + .65f * focus))
                    * (.68f + .32f * MathF.Pow(depth, 1.9f)) * Math.Clamp(2 - difference, 0, 1) * lengthFade;
                if (i < index)
                    opacity *= Math.Max(0, 1 - difference);
                if (i > index)
                    opacity *= .18f + .82f * Math.Clamp((float)(time - line.Source.Start + .34) / .34f, 0, 1);
                if (difference > .02f)
                {
                    float threshold = 1 - 1.2f * Math.Clamp((difference - .3f) / .6f, 0, 1);
                    if (cosine > threshold)
                        opacity *= Math.Clamp(1 - (cosine - threshold) / .15f, 0, 1);
                }
                float size = font * (.22f + .98f * MathF.Pow(depth, 1.5f)) * (1 + .65f * focus) * lengthScale * fit;
                float progress = Progress(glyph, time), colorProgress = Math.Min(1, progress * 15);
                float rotation = Math.Clamp(tangent * (.4f + .6f * depth), -38 * MathF.PI / 180, 38 * MathF.PI / 180);
                p.Glyph(glyph.Glyph, p.Width * .5f + x - glyph.Glyph.Advance * size * .5f,
                    p.Height * .5f + y - size * .6f, size, Vector3.Lerp(primary, accent, colorProgress),
                    opacity * (.55f + .45f * colorProgress), rotation, blur: 8 * (1 - depth) * (1 - .5f * focus), glow: focus * focus * .45f);
            }
        }
    }

    private static float RingWordOffset(FoliaPreparedLine line, double time, double end, float font, float radius)
    {
        var glyphs = line.Glyphs;
        if (glyphs.Length == 0)
            return 0;
        float total = (line.Width + Math.Max(0, glyphs.Length - 1) * .22f) * font;
        float fit = Math.Min(1, 4.25f * radius / Math.Max(total, 1));
        int current = 0;
        while (current + 1 < glyphs.Length && glyphs[current + 1].Start <= time)
            current++;
        ref readonly var a = ref glyphs[current];
        float angleA = ((a.Offset + current * .22f + a.Glyph.Advance * .5f) * font - total * .5f) / radius * fit;
        if (current + 1 < glyphs.Length)
        {
            ref readonly var b = ref glyphs[current + 1];
            float angleB = ((b.Offset + (current + 1) * .22f + b.Glyph.Advance * .5f) * font - total * .5f) / radius * fit;
            return angleA + (angleB - angleA) * Math.Clamp((float)((time - a.Start) / Math.Max(.001, b.Start - a.Start)), 0, 1);
        }
        if (current == 0)
            return angleA;
        ref readonly var previous = ref glyphs[current - 1];
        float step = (a.Offset - previous.Offset + .22f + (a.Glyph.Advance - previous.Glyph.Advance) * .5f) * font / radius * fit;
        return angleA + step * Math.Clamp((float)((time - a.Start) / Math.Max(.2, end - a.Start)), 0, 1) * 2;
    }

}
