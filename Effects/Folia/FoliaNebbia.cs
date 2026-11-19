using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI.Text;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Prepares reusable handwriting paths for the rain-window mask.</summary>
internal sealed class FoliaNebbia
{
    private readonly FoliaStageText[] _layouts;
    private readonly Dictionary<FoliaGlyph, PenLine[]> _paths;
    private readonly record struct PenLine(Vector2 From, Vector2 To, float Start, float End);

    internal FoliaNebbia(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        _layouts = FoliaStageText.Prepare(atlas, request.Width / Math.Max(1, request.Height), token);
        _paths = new(atlas.CharacterGlyphs.Count);
        var wanted = new HashSet<int>();
        foreach (var pair in atlas.CharacterGlyphs)
            if (Rune.TryGetRuneAt(pair.Key, 0, out var rune) && rune.Utf16SequenceLength == pair.Key.Length && rune.Value > 127)
                wanted.Add(rune.Value);
        var medians = FoliaNebbiaStrokes.ReadMedialPaths(wanted, token);
        var device = CanvasDevice.GetSharedDevice();
        using var format = new CanvasTextFormat
        {
            FontFamily = request.Family, FontSize = 96, FontWeight = new FontWeight { Weight = (ushort)request.Weight },
            WordWrapping = CanvasWordWrapping.NoWrap
        };
        foreach (var pair in atlas.CharacterGlyphs)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(pair.Key) || _paths.ContainsKey(pair.Value))
                continue;
            var segments = Rune.TryGetRuneAt(pair.Key, 0, out var rune) && rune.Utf16SequenceLength == pair.Key.Length
                && medians.TryGetValue(rune.Value, out var strokes) ? strokes : FoliaNebbiaStrokes.Estimate(device, format, pair.Key);
            if (segments.Length == 0)
                continue;
            float distance = 0;
            foreach (var segment in segments)
                distance += Vector2.Distance(segment.Start, segment.End);
            var path = new PenLine[segments.Length];
            float start = 0;
            for (int i = 0; i < path.Length; i++)
            {
                var segment = segments[i];
                float end = start + Vector2.Distance(segment.Start, segment.End) / Math.Max(.001f, distance);
                path[i] = new(segment.Start, segment.End, start, end);
                start = end;
            }
            _paths.Add(pair.Value, path);
        }
    }

    internal Vector2 Write(ref FoliaPainter painter, FoliaTextAtlas atlas, double position, float scale, int anchor)
    {
        var focus = new Vector2(.5f, .43f);
        if (anchor < 0)
            return focus;
        var current = _layouts[anchor];
        if (anchor > 0)
        {
            double entrance = position - current.Line.Source.Start;
            float fade = 1 - FoliaPainter.Ease((float)(entrance / .65));
            if (fade > 0 && FoliaShotTimeline.SungEnd(_layouts[anchor - 1].Line.Source) + 3 > position)
                WriteLine(ref painter, _layouts[anchor - 1], position, scale, fade, ref focus);
        }
        double sinceLine = position - FoliaShotTimeline.SungEnd(current.Line.Source);
        float opacity = 1 - FoliaPainter.Ease((float)((sinceLine - .6) / 2.3));
        if (opacity > 0)
            WriteLine(ref painter, current, position, scale, opacity, ref focus);
        if (anchor == _layouts.Length - 1 && sinceLine > 2.05)
        {
            float titleFont = Math.Min(Math.Min(painter.Width, painter.Height) * .12f, painter.Width * .76f / Math.Max(1, atlas.TitleWidth));
            float artistFont = Math.Min(titleFont * .55f, painter.Width * .66f / Math.Max(1, atlas.ArtistWidth));
            float fade = FoliaPainter.Ease((float)((sinceLine - 2.05) / 2.5));
            WriteCredit(ref painter, atlas.Title, atlas.TitleWidth, titleFont, painter.Height * .40f, fade);
            WriteCredit(ref painter, atlas.Artist, atlas.ArtistWidth, artistFont, painter.Height * .40f + titleFont * 1.27f, Math.Clamp(fade * 2 - 1, 0, 1));
        }
        return focus;
    }

    private void WriteCredit(ref FoliaPainter painter, ReadOnlySpan<FoliaPreparedGlyph> glyphs, float width, float font, float y, float progress)
    {
        float x = (painter.Width - width * font) * .5f;
        foreach (ref readonly var glyph in glyphs)
        {
            float reveal = Math.Clamp(progress * (glyphs.Length + .75f) - glyph.Index, 0, 1);
            if (reveal <= 0)
                break;
            DrawGlyph(ref painter, glyph.Glyph, x + glyph.Offset * font, y, font, reveal, .87f);
        }
    }

    private void WriteLine(ref FoliaPainter painter, FoliaStageText text, double position, float scale, float opacity, ref Vector2 focus)
    {
        float font = text.Fit(painter.Width * .77f, painter.Height * .55f, scale);
        float y = painter.Height * .45f - (text.Rows.Length * FoliaStageText.RowHeight + .2f) * font * .5f;
        foreach (ref readonly var row in text.Rows.AsSpan())
        {
            float x = (painter.Width - row.Width * font) * .5f;
            foreach (ref readonly var glyph in text.Line.Glyphs.AsSpan(row.First, row.End - row.First))
            {
                float reveal = FoliaStageText.Reveal(glyph, position);
                if (reveal <= 0)
                    continue;
                float left = x + (glyph.Offset - row.Offset) * font;
                DrawGlyph(ref painter, glyph.Glyph, left, y, font, reveal, opacity);
                focus = new((left + glyph.Glyph.Advance * font * .5f) / painter.Width, (y + font * .56f) / painter.Height);
            }
            y += font * FoliaStageText.RowHeight;
        }
    }

    private void DrawGlyph(ref FoliaPainter painter, in FoliaGlyph glyph, float x, float y, float font, float reveal, float opacity)
    {
        if (!_paths.TryGetValue(glyph, out var path))
        {
            painter.Glyph(glyph, x, y, font, Vector3.One, opacity, reveal: reveal, revealSoftness: .065f);
            return;
        }
        // Atlas padding is a quarter of the font em on each side.
        var origin = new Vector2(x + (glyph.BearingX + .25f) * font, y + (glyph.BearingY + .25f) * font);
        var size = new Vector2(Math.Max(.035f, glyph.Width - .5f), Math.Max(.035f, glyph.Height - .5f)) * font;
        float width = Math.Max(1.45f, font * .086f);
        foreach (ref readonly var line in path.AsSpan())
        {
            if (reveal <= line.Start)
                break;
            Vector2 from = origin + line.From * size;
            Vector2 to = origin + Vector2.Lerp(line.From, line.To, Math.Clamp((reveal - line.Start) / Math.Max(.00001f, line.End - line.Start), 0, 1)) * size;
            Vector2 delta = to - from, center = (from + to) * .5f;
            float length = delta.Length() + width;
            painter.Shape(3, center.X - length * .5f, center.Y - width * .5f, length, width, Vector3.One, opacity,
                MathF.Atan2(delta.Y, delta.X), radius: width * .5f);
        }
    }
}
