using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Lyric-PV timeline and film layers adapted from Folia's Hanabi program.</summary>
internal sealed class FoliaHanabi
{
    private readonly FoliaStageText[] _layouts;
    private readonly FoliaShot[] _shots;
    private readonly ChorusSpan[] _choruses;
    private readonly record struct ChorusSpan(double Start, double End);

    internal FoliaHanabi(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        _layouts = FoliaStageText.Prepare(atlas, request.Width / Math.Max(1, request.Height), token);
        uint seed = FoliaShotTimeline.Seed(atlas.Seed);
        var shots = new List<FoliaShot>();
        var choruses = new List<ChorusSpan>();
        double cursor = 0;
        for (int i = 0; i < atlas.Lines.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var line = atlas.Lines[i].Source;
            double start = Math.Max(cursor, Math.Max(0, line.Start));
            if (start > cursor)
                FoliaShotTimeline.FillGap(shots, cursor, start, 2.7, seed, token);
            double sungEnd = FoliaShotTimeline.SungEnd(line);
            double next = i + 1 < atlas.Lines.Length ? Math.Max(start + .001, atlas.Lines[i + 1].Source.Start) : FoliaShotTimeline.SongEnd(request.Song);
            next = Math.Max(next, start + .001);
            double end = Math.Min(next, Math.Max(start + .08, sungEnd + .55));
            if (next - end < 1.1)
                end = next;
            int paragraph = shots.Count == 0 || shots[^1].Line < 0 ? 1 : 0;
            shots.Add(new(start, end, i, paragraph, unchecked(seed + (uint)shots.Count)));
            cursor = end;
            if (line.IsChorus)
            {
                if (choruses.Count > 0 && start - choruses[^1].End <= 1.5)
                    choruses[^1] = choruses[^1] with { End = Math.Max(choruses[^1].End, sungEnd) };
                else
                    choruses.Add(new(start, sungEnd));
            }
        }
        FoliaShotTimeline.FillGap(shots, cursor, FoliaShotTimeline.SongEnd(request.Song), 2.7, seed, token);
        _shots = shots.ToArray();
        _choruses = choruses.ToArray();
    }

    internal float Bloom(double position)
    {
        foreach (var chorus in _choruses)
        {
            if (position < chorus.Start - .55)
                break;
            if (position <= chorus.End + .9)
            {
                float rise = FoliaPainter.Ease((float)((position - chorus.Start + .55) / .72));
                float fall = FoliaPainter.Ease((float)((chorus.End + .9 - position) / .85));
                return .38f + rise * fall * 1.05f;
            }
        }
        return .38f;
    }

    internal void Write(ref FoliaPainter painter, FoliaSong song, double position, float scale, in FoliaMotionFrame motion)
    {
        int index = FoliaShotTimeline.Find(_shots, position);
        if (index < 0)
            return;
        var shot = _shots[index];
        float enter = (float)((position - shot.Start) / .56);
        if (index > 0 && enter < 1)
            WriteShot(ref painter, song, _shots[index - 1], position, scale, 1 - FoliaPainter.Ease(enter), true, default);
        WriteShot(ref painter, song, shot, position, scale, 1, false, motion);
    }

    private void WriteShot(ref FoliaPainter painter, FoliaSong song, in FoliaShot shot, double position,
        float scale, float alpha, bool outgoing, in FoliaMotionFrame motion)
    {
        float unit = Math.Min(painter.Width, painter.Height);
        var center = new Vector2(painter.Width * .5f, painter.Height * .48f);
        Vector3 accent = song.Palette[3];
        Vector3 cool = Vector3.Lerp(song.Palette[2], new(.35f, .71f, 1), .58f);
        Vector3 warm = Vector3.Lerp(accent, new(1, .4f, .22f), .58f);
        int first = painter.Count;
        float local = (float)(position - shot.Start);
        float rise = FoliaPainter.Ease(local / Math.Min(.82f, (float)(shot.End - shot.Start) * .47f));
        float after = outgoing ? Math.Clamp((float)(position - shot.End) / .56f, 0, 1) : 0;
        Vector3 shotColor = (shot.Seed & 1) == 0 ? warm : cool;

        // Fixed-budget aerial launches also carry the instrumental shots.
        for (uint n = 0; n < 5; n++)
        {
            float eventTime = .11f + n * .4f;
            float x = painter.Width * (.13f + .74f * FoliaShotTimeline.Random(shot.Seed, n + 8));
            float y = painter.Height * (.14f + .65f * FoliaShotTimeline.Random(shot.Seed, n + 26));
            float age = local - eventTime;
            if (age is > -.26f and < 0)
            {
                float head = y - age * unit * .62f;
                painter.Line(x, head, x, head + unit * .065f, shotColor, alpha * .65f, 1.6f);
            }
            Sparks(ref painter, new(x, y), unit, age, shot.Seed + n * 17, shotColor, alpha, 30);
        }

        if (shot.Line >= 0)
        {
            var text = _layouts[shot.Line];
            float font = text.Fit(painter.Width * .81f, painter.Height * .64f, scale);
            float y = center.Y - (text.Rows.Length * FoliaStageText.RowHeight + .22f) * font * .5f;
            foreach (ref readonly var row in text.Rows.AsSpan())
            {
                float x = center.X - row.Width * font * .5f;
                foreach (ref readonly var glyph in text.Line.Glyphs.AsSpan(row.First, row.End - row.First))
                {
                    float age = (float)(position - glyph.Start);
                    float show = FoliaPainter.Ease((age + .035f) / .17f);
                    if (show <= 0)
                        continue;
                    float gx = x + (glyph.Offset - row.Offset) * font;
                    float gy = y + (1 - show) * font * .22f;
                    float advance = glyph.Glyph.Advance * font;
                    // Space metrics have no visible outline and should not acquire a colored sticker.
                    if (glyph.Glyph.Width > .51f && advance > font * .19f)
                        painter.Shape(3, gx - font * .045f, gy + font * .13f, advance + font * .08f, font * .97f,
                            (glyph.Word & 1) == 0 ? warm : cool, alpha * show * .91f, radius: font * .07f);
                    painter.Glyph(glyph.Glyph, gx, gy, font, new(.11f, .065f, .18f), show * alpha);
                    if (glyph.Index % 3 == 0)
                        Sparks(ref painter, new(gx + advance * .5f, gy + font * .35f), unit * .5f, age, shot.Seed + (uint)glyph.Index, shotColor, alpha, 13);
                }
                y += font * FoliaStageText.RowHeight;
            }
        }
        if (motion.Onset > .08f)
            Sparks(ref painter, new(center.X + motion.Sign * unit * .3f, center.Y), unit,
                (1 - motion.Onset) * .46f, shot.Seed + 501, warm, alpha * motion.Onset, 40);

        float spin = ((shot.Seed & 1) == 0 ? 1 : -1) * (shot.Word == 1 ? 1 : .68f);
        painter.TransformFrom(first, center, new(0, painter.Height * (after * .31f - (1 - rise) * .25f)),
            1 + motion.Low * .055f + after * .07f, spin * ((rise - 1) * .51f + after * .52f));
    }

    private static void Sparks(ref FoliaPainter painter, Vector2 center, float unit, float age, uint seed, Vector3 color, float alpha, uint count)
    {
        if (age < 0 || age >= .92f)
            return;
        float phase = age / .92f;
        float fade = (1 - phase) * (1 - phase) * alpha;
        float radius = unit * (.025f + .27f * (1 - MathF.Exp(-age * 2.7f)));
        painter.Shape(18, center.X - radius * .2f, center.Y - radius * .2f,
            radius * .4f, radius * .4f, color * 1.25f, fade * .58f);
        for (uint i = 0; i < count; i++)
        {
            float angle = (i + FoliaShotTimeline.Random(seed, i + 89) * .5f) * MathF.Tau / count;
            var (sin, cos) = MathF.SinCos(angle);
            var direction = new Vector2(cos, sin);
            float spread = radius * (.55f + .45f * FoliaShotTimeline.Random(seed, i + 13));
            Vector2 tip = center + direction * spread + new Vector2(0, unit * .10f * age * age);
            Vector2 tail = tip - direction * (unit * (.012f + .023f * (1 - phase)));
            painter.Line(tail.X, tail.Y, tip.X, tip.Y, color * 1.42f, fade, Math.Max(1, unit * .003f));
        }
    }
}
