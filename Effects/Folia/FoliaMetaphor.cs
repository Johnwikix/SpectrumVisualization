using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Hard-cut ink and halftone compositions adapted from Folia's Metaphor stage.</summary>
internal sealed class FoliaMetaphor
{
    private readonly FoliaStageText[] _layouts;
    private readonly FoliaShot[] _shots;

    internal FoliaMetaphor(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        _layouts = FoliaStageText.Prepare(atlas, request.Width / Math.Max(1, request.Height), token);
        var shots = new List<FoliaShot>();
        uint seed = FoliaShotTimeline.Seed(atlas.Seed);
        double cursor = 0;
        for (int i = 0; i < atlas.Lines.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var line = atlas.Lines[i].Source;
            if (!HasLetters(line.Text))
                continue;
            double start = Math.Max(cursor, Math.Max(0, line.Start));
            double next = i + 1 < atlas.Lines.Length ? Math.Max(start + .001, atlas.Lines[i + 1].Source.Start) : FoliaShotTimeline.SongEnd(request.Song);
            double end = Math.Min(Math.Max(start + .001, next), Math.Max(start + .07, FoliaShotTimeline.SungEnd(line) + .45));
            FoliaShotTimeline.FillGap(shots, cursor, start, 8, seed, token);
            int word = -1;
            for (int w = 1; w < line.Words.Length; w++)
            {
                double cut = line.Words[w].Start;
                if (cut - start < .37 || end - cut < .24)
                    continue;
                shots.Add(new(start, cut, i, word, unchecked(seed + (uint)shots.Count)));
                start = cut;
                word = w;
            }
            shots.Add(new(start, end, i, word, unchecked(seed + (uint)shots.Count)));
            cursor = end;
        }
        FoliaShotTimeline.FillGap(shots, cursor, FoliaShotTimeline.SongEnd(request.Song), 8, seed, token);
        _shots = shots.ToArray();
    }

    private static bool HasLetters(ReadOnlySpan<char> text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (System.Text.Rune.IsLetterOrDigit(rune) || rune.Value > 0xffff)
                return true;
        return false;
    }

    internal int ShotAt(double position) => FoliaShotTimeline.Find(_shots, position);

    internal void Write(ref FoliaPainter painter, FoliaTextAtlas atlas, double position, float scale, in FoliaMotionFrame motion)
    {
        int index = ShotAt(position);
        if (index < 0)
            return;
        var shot = _shots[index];
        float width = painter.Width, height = painter.Height, size = Math.Min(width, height);
        var center = new Vector2(width * .5f, height * .47f);
        float phase = shot.Progress(position);
        float time = (float)(position - shot.Start);
        float n = FoliaShotTimeline.Random(shot.Seed, 7);
        Vector3 ink = new(1, .25f, 0), fine = new(.64f, .86f, 0), tinted = new(.65f, .85f, .78f);
        int first = painter.Count;
        float radius = size * .27f;
        float dir = (shot.Seed & 1) == 0 ? 1 : -1;
        Figure(ref painter, center + new Vector2(dir * width * .24f, -height * .21f), radius, (int)(shot.Seed % 3), fine);
        Figure(ref painter, center + new Vector2(-dir * width * .25f, height * .20f), radius * .65f, (int)((shot.Seed + 1) % 3), fine);
        float stroke = Math.Clamp(size * .003f, .8f, 3);
        for (int side = -1; side <= 1; side += 2)
        {
            float x = center.X + side * width * .41f;
            float y = center.Y + side * height * .32f;
            painter.Line(x - radius * .19f, y, x + radius * .19f, y, fine, .72f, stroke);
            painter.Line(x, y - radius * .10f, x, y + radius * .10f, fine, .72f, stroke);
        }
        float collision = Math.Clamp(time / .52f, 0, 1);
        float blobSize = size * (.13f + n * .12f);
        float approach = (1 - collision) * (1 - collision) * size * .23f;
        Blob(ref painter, center + new Vector2(-approach - radius * .07f, radius * .27f), blobSize, ink, .87f);
        Blob(ref painter, center + new Vector2(approach + radius * .09f, radius * .23f), blobSize * .86f, tinted, .85f);
        for (uint i = 0; i < 12; i++)
        {
            float angle = i * MathF.Tau / 12 + phase * 1.2f;
            var (sin, cos) = MathF.SinCos(angle);
            float r = size * (.08f + .18f * collision) * (.45f + .55f * FoliaShotTimeline.Random(shot.Seed, i));
            Blob(ref painter, center + new Vector2(cos, sin) * r, blobSize * .19f, (i & 1) == 0 ? tinted : fine, .49f * (1 - phase * .65f));
        }
        if (motion.Onset > .08f)
            painter.Shape(2, center.X - radius, center.Y - radius, radius * 2, radius * 2, tinted, .62f * motion.Onset, stroke: stroke * 1.8f);

        Vector2 focus = center;
        float zoom = .96f + n * .14f;
        if (shot.Line >= 0)
        {
            var text = _layouts[shot.Line];
            float font = text.Fit(width * .76f, height * .52f, scale);
            float y = center.Y - (text.Rows.Length * FoliaStageText.RowHeight + .18f) * font * .5f;
            bool focused = false;
            foreach (ref readonly var row in text.Rows.AsSpan())
            {
                float x = center.X - row.Width * font * .5f;
                foreach (ref readonly var glyph in text.Line.Glyphs.AsSpan(row.First, row.End - row.First))
                {
                    float left = x + (glyph.Offset - row.Offset) * font;
                    if (!focused && glyph.Word == shot.Word)
                    {
                        focus = new(left + glyph.Glyph.Advance * font * .5f, y + font * .56f);
                        focused = true;
                    }
                    painter.Glyph(glyph.Glyph, left, y, font, glyph.ActiveColor.HasValue ? new(1, .42f, .8f) : ink, .97f);
                }
                y += font * FoliaStageText.RowHeight;
            }
            if (focused)
                zoom += .10f + n * .36f;
        }
        else if (index == 0 && atlas.Title.Length > 0)
        {
            float font = Math.Min(size * .12f, width * .68f / Math.Max(1, atlas.TitleWidth));
            painter.Text(atlas.Title, center.X - atlas.TitleWidth * font * .5f, center.Y - font * .62f, font, ink, .92f);
        }
        float rotation = (n - .5f) * .20f + phase * .022f * dir;
        painter.TransformFrom(first, focus, Vector2.Lerp(focus, center, .48f) - focus, zoom, rotation);
    }

    private static void Blob(ref FoliaPainter painter, Vector2 pos, float size, Vector3 color, float alpha) =>
        painter.Shape(4, pos.X - size * .5f, pos.Y - size * .5f, size, size, color, alpha);

    private static void Figure(ref FoliaPainter painter, Vector2 center, float radius, int kind, Vector3 color)
    {
        if (kind == 0)
        {
            for (int i = 0; i < 3; i++)
            {
                float angle = i * MathF.PI / 3;
                Vector2 previous = center;
                for (int k = 0; k <= 40; k++)
                {
                    var (sin, cos) = MathF.SinCos(k * MathF.Tau / 40);
                    Vector2 point = center + Vector2.Transform(new Vector2(radius * cos, radius * .34f * sin), Matrix3x2.CreateRotation(angle));
                    if (k > 0)
                        painter.Line(previous.X, previous.Y, point.X, point.Y, color, .65f, 1.25f);
                    previous = point;
                }
            }
            return;
        }
        if (kind == 1)
        {
            float step = radius / 3;
            for (int row = -2; row <= 2; row++)
                for (int col = -2; col <= 2; col++)
                {
                    var node = center + new Vector2(col + row * .5f, row * .72f) * step;
                    painter.Shape(16, node.X - 2, node.Y - 2, 4, 4, color, .83f);
                    if (col < 2)
                        painter.Line(node.X, node.Y, node.X + step, node.Y, color, .47f, 1.25f);
                    if (row < 2)
                        painter.Line(node.X, node.Y, node.X + step * .5f, node.Y + step * .72f, color, .52f, 1.25f);
                }
            return;
        }
        for (int i = 1; i <= 4; i++)
        {
            float r = radius * i * .23f;
            painter.Shape(2, center.X - r, center.Y - r, r * 2, r * 2, color, .61f, stroke: 1.35f);
        }
        painter.Line(center.X - radius, center.Y, center.X + radius, center.Y, color, .60f);
        painter.Line(center.X, center.Y - radius, center.X, center.Y + radius, color, .60f);
    }
}
