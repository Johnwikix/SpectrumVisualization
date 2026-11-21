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
internal sealed partial class FoliaNebbia
{
    private readonly FoliaPreparedLine[] _lines;
    private readonly double[] _goneThrough;
    private readonly record struct GlyphPlacement(Vector2 Center, float Size, float Rotation);
    private readonly Dictionary<FoliaGlyph, PenPath> _paths;
    private readonly LinePlan[] _linePlans;
    private readonly record struct PenPath(PenLine[] Lines, PenStroke[] Strokes, float Length);
    private readonly record struct Writing(double Start, double Duration, bool Fade);
    private readonly record struct LinePlan(Writing[] Glyphs, GlyphPlacement[] Placement, GlyphArt[] Art, Vector4 Bounds, double Start, double Gone, double FogAt,
        double YieldAt, double BreathAt, double BreathDuration, double BreathUntil, Vector2 Center, double Done, Doodle Tail)
    {
        internal Vector4[]? BreathLobes { get; init; }
    }
    private readonly record struct Doodle(GlyphArt Art, GlyphPlacement Placement, double Start, double Duration);
    private readonly record struct PenLine(Vector2 From, Vector2 To, float Start, float End);
    private readonly record struct PenStroke(int First, int Count, float Start, float End);

    internal FoliaNebbia(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        _lines = atlas.Lines;
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
        var strokeCounts = new Dictionary<string, int>();
        foreach (var pair in atlas.CharacterGlyphs)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(pair.Key) || _paths.ContainsKey(pair.Value))
                continue;
            if (Rune.TryGetRuneAt(pair.Key, 0, out var rune) && rune.Utf16SequenceLength == pair.Key.Length
                && medians.TryGetValue(rune.Value, out var strokes))
            {
                var path = PreparePath(strokes);
                _paths.Add(pair.Value, path);
                strokeCounts.Add(pair.Key, path.Strokes.Length);
                continue;
            }
            var segments = FoliaNebbiaStrokes.Estimate(device, format, pair.Key);
            if (segments.Length == 0)
                continue;
            _paths.Add(pair.Value, PreparePath(segments));
        }
        using var layout = FoliaSourcePlanner.CompileNativeStage(atlas, request, "nebbia", token, strokeCounts);
        _linePlans = PrepareWriting(layout.RootElement, request.Width, request.Height, token);
        _goneThrough = new double[_linePlans.Length];
        for (int i = 0; i < _goneThrough.Length; i++)
            _goneThrough[i] = Math.Max(i == 0 ? 0 : _goneThrough[i - 1], _linePlans[i].Gone);
    }

    private static PenPath PreparePath(ReadOnlySpan<FoliaNebbiaStrokes.Segment> segments)
    {
        float distance = 0;
        int strokeCount = 1;
        for (int i = 0; i < segments.Length; i++)
        {
            distance += Vector2.Distance(segments[i].Start, segments[i].End);
            if (i > 0 && segments[i].StartsStroke) strokeCount++;
        }
        var path = new PenLine[segments.Length];
        var strokes = new PenStroke[strokeCount];
        float start = 0;
        int strokeIndex = 0;
        int first = 0;
        for (int i = 0; i < path.Length; i++)
        {
            var segment = segments[i];
            float end = start + Vector2.Distance(segment.Start, segment.End) / Math.Max(.001f, distance);
            if (i > 0 && segment.StartsStroke)
            {
                strokes[strokeIndex++] = new(first, i - first, path[first].Start, start);
                first = i;
            }
            path[i] = new(segment.Start, segment.End, start, end);
            start = end;
        }
        strokes[strokeIndex] = new(first, path.Length - first, path[first].Start, start);
        return new(path, strokes, distance);
    }

    private LinePlan[] PrepareWriting(System.Text.Json.JsonElement placement, float width, float height, CancellationToken token)
    {
        var plans = new LinePlan[_lines.Length];
        var finished = new double[plans.Length];
        var viewport = new Vector2(Math.Max(1, width), Math.Max(1, height));
        for (int i = 0; i < plans.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var line = _lines[i];
            var glyphs = line.Glyphs;
            var laid = placement[i];
            Doodle doodle = ReadDoodle(laid, viewport, i);
            double start = line.Source.Start;
            double end = FoliaShotTimeline.SungEnd(line.Source);
            double next = i + 1 < _lines.Length ? _lines[i + 1].Source.Start : end + 6;
            double until = Math.Max(next, end);
            var natural = new double[glyphs.Length];
            var art = new GlyphArt[glyphs.Length];
            for (int j = 0; j < glyphs.Length && j < laid.GetArrayLength(); j++)
                if (_paths.TryGetValue(glyphs[j].Glyph, out var path))
                {
                    art[j] = PrepareArt(path, laid[j].GetProperty("s").GetSingle(), i * 131 + j * 17 + 5);
                    natural[j] = art[j].Path.Length * .28 + (art[j].Path.Strokes.Length - 1) * .06;
                }
            double lo = .3, hi = 1;
            if (WriteEnd(glyphs, natural, start, hi, doodle) <= until)
                lo = hi;
            else
            {
                for (int step = 0; step < 14; step++)
                {
                    double mid = (lo + hi) * .5;
                    if (WriteEnd(glyphs, natural, start, mid, doodle) > until)
                        hi = mid;
                    else
                        lo = mid;
                }
            }
            bool fade = WriteEnd(glyphs, natural, start, lo, doodle) > until + .001;
            var writing = new Writing[glyphs.Length];
            double written = start;
            finished[i] = start;
            for (int j = 0; j < glyphs.Length; j++)
            {
                if (natural[j] <= 0)
                    continue;
                ref readonly var glyph = ref glyphs[j];
                double duration = natural[j] * lo;
                written = Math.Max(glyph.Start, written);
                writing[j] = fade
                    ? new(glyph.Start, Math.Clamp((glyph.End - glyph.Start) * 3, .35, 1.2), true)
                    : new(written, duration, false);
                written += duration;
                finished[i] = Math.Max(finished[i], writing[j].Start +
                    (fade ? Math.Min(.5, writing[j].Duration) : writing[j].Duration));
            }
            double fogAt = Math.Min(next, end + 6) + 1.2;
            double afterThree = i + 3 < _lines.Length ? _lines[i + 3].Source.Start + 1.2 : double.PositiveInfinity;
            double linger = Math.Max(fogAt, Math.Min(fogAt + 16, afterThree));
            var locations = new GlyphPlacement[glyphs.Length];
            var lowest = new Vector2(float.MaxValue);
            var highest = new Vector2(float.MinValue);
            for (int j = 0; j < locations.Length && j < laid.GetArrayLength(); j++)
            {
                if (writing[j].Duration <= 0)
                    continue;
                var item = laid[j];
                float size = item.GetProperty("s").GetSingle();
                var at = new Vector2(item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle());
                locations[j] = new(at / viewport, size / viewport.Y, item.GetProperty("rot").GetSingle());
                lowest = Vector2.Min(lowest, (at - new Vector2(size * .5f)) / viewport);
                highest = Vector2.Max(highest, (at + new Vector2(size * .5f)) / viewport);
            }
            if (doodle.Duration > 0)
            {
                doodle = doodle with { Start = Math.Max(doodle.Start, written), Duration = doodle.Duration * lo };
                finished[i] = Math.Max(finished[i], doodle.Start + doodle.Duration);
                Vector2 at = doodle.Placement.Center;
                var radius = new Vector2(doodle.Placement.Size * height / width, doodle.Placement.Size) * .5f;
                lowest = Vector2.Min(lowest, at - radius);
                highest = Vector2.Max(highest, at + radius);
            }
            bool visible = lowest.X <= highest.X;
            var center = visible ? (lowest + highest) * .5f : new Vector2(.5f, .43f);
            var bounds = visible ? new Vector4(lowest.X, lowest.Y, highest.X, highest.Y) : default;
            plans[i] = new(writing, locations, art, bounds, start, linger + 4, linger,
                double.PositiveInfinity, double.PositiveInfinity, 1.2, 0, center, Math.Max(end, finished[i]), doodle);
        }

        for (int i = 0; i < plans.Length; i++)
        {
            double end = FoliaShotTimeline.SungEnd(_lines[i].Source);
            double next = i + 1 < plans.Length ? plans[i + 1].Start : end + 6;
            double fogAt = Math.Min(next, end + 6) + 1.2;
            for (int j = i + 1; j < plans.Length && plans[j].Start < plans[i].Gone; j++)
            {
                var a = plans[i].Bounds;
                var b = plans[j].Bounds;
                if (Math.Min(a.Z, b.Z) <= Math.Max(a.X, b.X) || Math.Min(a.W, b.W) <= Math.Max(a.Y, b.Y))
                    continue;
                double yieldAt = Math.Max(plans[j].Start, finished[i] + .9);
                fogAt = Math.Max(fogAt, Math.Min(plans[i].FogAt, yieldAt - 4));
                double breathAt = yieldAt < fogAt + 4 ? Math.Max(yieldAt - 1.5, finished[i] + .3) : double.PositiveInfinity;
                double breathDuration = double.IsFinite(breathAt) ? Math.Max(.3, yieldAt - .3 - breathAt) : 1.2;
                plans[i] = plans[i] with
                {
                    FogAt = fogAt,
                    YieldAt = yieldAt,
                    BreathAt = breathAt,
                    BreathDuration = breathDuration,
                    BreathUntil = FoliaShotTimeline.SungEnd(_lines[j].Source) + 1,
                    BreathLobes = double.IsFinite(breathAt) ? BuildBreath(plans[i], viewport, i) : null,
                    Gone = Math.Max(fogAt + 4, double.IsFinite(breathAt) ? FoliaShotTimeline.SungEnd(_lines[j].Source) + 9 : yieldAt + .4)
                };
                break;
            }
            if (i == plans.Length - 1)
                plans[i] = plans[i] with { FogAt = Math.Min(plans[i].FogAt, end + 1.3) };
        }
        return plans;
    }

    private static Doodle ReadDoodle(System.Text.Json.JsonElement line, Vector2 viewport, int lineIndex)
    {
        if (line.GetArrayLength() == 0) return default;
        var item = line[line.GetArrayLength() - 1];
        if (!item.TryGetProperty("icon", out var property) || property.GetString() is not string icon)
            return default;
        var segments = DoodleSegments(icon);
        if (segments.Length == 0) return default;
        var path = PreparePath(segments);
        var location = new GlyphPlacement(new Vector2(item.GetProperty("x").GetSingle(), item.GetProperty("y").GetSingle()) / viewport,
            item.GetProperty("s").GetSingle() / viewport.Y, item.GetProperty("rot").GetSingle());
        var art = PrepareArt(path, location.Size * viewport.Y, lineIndex * 131 + (line.GetArrayLength() - 1) * 17 + 5);
        return new(art, location, item.GetProperty("t").GetDouble(), art.Path.Length * .28 + (art.Path.Strokes.Length - 1) * .06);
    }

    private static double WriteEnd(ReadOnlySpan<FoliaPreparedGlyph> glyphs, ReadOnlySpan<double> natural, double start, double factor, in Doodle doodle)
    {
        double end = start;
        for (int i = 0; i < glyphs.Length; i++)
            if (natural[i] > 0)
                end = Math.Max(end, glyphs[i].Start) + natural[i] * factor;
        return doodle.Duration > 0 ? Math.Max(end, doodle.Start) + doodle.Duration * factor : end;
    }

    internal Vector2 Write(ref FoliaPainter painter, FoliaTextAtlas atlas, double position, int anchor, bool snow,
        out float spotRadius, out float spotStrength)
    {
        var focus = new Vector2(.5f, .43f);
        spotRadius = .02f;
        spotStrength = 0;
        if (anchor < 0)
            return focus;
        // Artwork can outlive several very short lines. Track the cutoffs in the plan so seeks
        // evaluate the same traces as forward playback and never consult mutable pen state.
        int lo = 0, hi = Math.Min(anchor + 1, _goneThrough.Length);
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_goneThrough[mid] <= position)
                lo = mid + 1;
            else
                hi = mid;
        }
        for (int i = lo; i <= anchor && i < _linePlans.Length; i++)
        {
            ref readonly var plan = ref _linePlans[i];
            if (position < plan.Start || position >= plan.Gone)
                continue;
            float fog = 1 - FoliaPainter.Ease((float)((position - plan.FogAt) / 4));
            float yield = double.IsFinite(plan.BreathAt)
                ? 1 - FoliaPainter.Ease((float)(((position - plan.BreathAt) / plan.BreathDuration - .15) / .7))
                : 1 - FoliaPainter.Ease((float)(position - plan.YieldAt + .6));
            if (double.IsFinite(plan.BreathAt))
            {
                PaintBreath(ref painter, plan, position);
            }
            float clarity = fog * (double.IsFinite(plan.BreathAt) ? Math.Max(.08f, yield) : yield);
            float beads = (1 - FoliaPainter.Ease((float)((position - plan.FogAt - 4) / 12))) * yield;
            if (clarity > .002f || beads > .002f)
                WriteLine(ref painter, plan, position, clarity, beads, snow);
        }
        LocateSpot(position, anchor, ref focus, out spotRadius, out spotStrength);
        double sinceLine = position - FoliaShotTimeline.SungEnd(_lines[anchor].Source);
        if (anchor == _lines.Length - 1 && sinceLine > 2.05)
        {
            float titleFont = Math.Min(Math.Min(painter.Width, painter.Height) * .12f, painter.Width * .76f / Math.Max(1, atlas.TitleWidth));
            float artistFont = Math.Min(titleFont * .55f, painter.Width * .66f / Math.Max(1, atlas.ArtistWidth));
            float fade = FoliaPainter.Ease((float)((sinceLine - 2.05) / 2.5));
            WriteCredit(ref painter, atlas.Title, atlas.TitleWidth, titleFont, painter.Height * .40f, fade);
            WriteCredit(ref painter, atlas.Artist, atlas.ArtistWidth, artistFont, painter.Height * .40f + titleFont * 1.27f, Math.Clamp(fade * 2 - 1, 0, 1));
        }
        return focus;
    }

    private static Vector4[] BuildBreath(in LinePlan plan, Vector2 viewport, int index)
    {
        var rng = new InkRandom((uint)index * 6151 + 29);
        float size = plan.Tail.Placement.Size;
        foreach (var glyph in plan.Placement)
            size = Math.Max(size, glyph.Size);
        var radii = new Vector2(plan.Bounds.Z - plan.Bounds.X, plan.Bounds.W - plan.Bounds.Y) * viewport * .5f
            + new Vector2(size * viewport.Y * .3f);
        var center = plan.Center * viewport;
        var lobes = new Vector4[6 + (int)(rng.Next() * 2)];
        lobes[0] = new(center.X, center.Y, radii.X * .95f, radii.Y * .95f);
        for (int i = 1; i < lobes.Length; i++)
        {
            float angle = (i - 1 + (float)rng.Next() * .6f) * MathF.Tau / (lobes.Length - 1);
            float radius = Math.Min(radii.X, radii.Y) * (.45f + (float)rng.Next() * .25f);
            lobes[i] = new(center.X + MathF.Cos(angle) * radii.X * .6f,
                center.Y + MathF.Sin(angle) * radii.Y * .6f, radius, radius);
        }
        return lobes;
    }

    private static void PaintBreath(ref FoliaPainter painter, in LinePlan plan, double position)
    {
        if (position < plan.BreathAt || plan.BreathLobes is not { } lobes)
            return;
        float decay = 1 - FoliaPainter.Ease((float)((position - Math.Max(plan.BreathUntil, plan.BreathAt + plan.BreathDuration)) / 8));
        if (decay <= .002f)
            return;
        float u = Math.Clamp((float)((position - plan.BreathAt) / plan.BreathDuration), 0, 1);
        float e = 1 - (1 - u) * (1 - u) * (1 - u);
        float kx = .7f + .3f * e, ky = .08f + .92f * e;
        float strength = FoliaPainter.Ease(u * 1.4f);
        foreach (var lobe in lobes)
        {
            float x = lobes[0].X + (lobe.X - lobes[0].X) * kx;
            float y = lobes[0].Y + (lobe.Y - lobes[0].Y) * ky;
            float rx = lobe.Z * kx, ry = Math.Max(4, lobe.W * ky);
            painter.Shape(29, x - rx, y - ry, rx * 2, ry * 2, Vector3.One, decay, stroke: strength);
        }
    }

    private void LocateSpot(double position, int anchor, ref Vector2 focus, out float radius, out float strength)
    {
        radius = .02f;
        strength = 0;
        ref readonly var line = ref _linePlans[anchor];
        if (position < line.Start || position > line.Done + .8)
            return;
        if (line.Tail.Duration > 0 && position >= line.Tail.Start)
        {
            focus = line.Tail.Placement.Center;
            radius = Math.Max(.02f, line.Tail.Placement.Size * .75f);
            strength = .5f;
            return;
        }
        int current = -1, next = -1;
        for (int i = 0; i < line.Glyphs.Length; i++)
        {
            if (line.Placement[i].Size <= 0 || line.Glyphs[i].Duration <= 0)
                continue;
            if (line.Glyphs[i].Start <= position)
                current = i;
            else
            {
                next = i;
                break;
            }
        }
        if (current < 0)
            return;
        var glyph = line.Placement[current];
        focus = glyph.Center;
        radius = glyph.Size;
        if (next >= 0)
        {
            float p = FoliaPainter.Ease((float)((position - line.Glyphs[current].Start) /
                Math.Max(.05, line.Glyphs[next].Start - line.Glyphs[current].Start)));
            focus = Vector2.Lerp(focus, line.Placement[next].Center, p);
            radius += (line.Placement[next].Size - radius) * p;
        }
        radius = Math.Max(.02f, radius * .75f);
        strength = .5f;
    }

    private void WriteCredit(ref FoliaPainter painter, ReadOnlySpan<FoliaPreparedGlyph> glyphs, float width, float font, float y, float progress)
    {
        float x = (painter.Width - width * font) * .5f;
        foreach (ref readonly var glyph in glyphs)
        {
            float reveal = Math.Clamp(progress * (glyphs.Length + .75f) - glyph.Index, 0, 1);
            if (reveal <= 0)
                break;
            var center = new Vector2(x + glyph.Offset * font + (glyph.Glyph.BearingX + glyph.Glyph.Width * .5f) * font, y + font * .5f);
            DrawGlyph(ref painter, glyph.Glyph, center, font, reveal);
        }
    }

    private static void WriteLine(ref FoliaPainter painter, in LinePlan plan, double position, float opacity, float beads, bool snow)
    {
        for (int i = 0; i < plan.Glyphs.Length; i++)
        {
            ref readonly var write = ref plan.Glyphs[i];
            var placement = plan.Placement[i];
            if (write.Duration <= 0 || placement.Size <= 0 || position <= write.Start)
                continue;
            float reveal = Math.Clamp((float)((position - write.Start) / write.Duration), 0, 1);
            float font = painter.Height * placement.Size;
            var center = placement.Center * new Vector2(painter.Width, painter.Height);
            ref readonly GlyphArt art = ref plan.Art[i];
            PaintPath(ref painter, art.Path, center, font, placement.Rotation, write.Fade ? 1 : reveal, (float)write.Duration,
                opacity * (write.Fade ? FoliaPainter.Ease(reveal) : 1), beads, (float)(position - write.Start), write.Fade ? [] : art.Wet, snow);
        }
        var doodle = plan.Tail;
        if (doodle.Duration > 0 && position > doodle.Start)
        {
            var center = doodle.Placement.Center * new Vector2(painter.Width, painter.Height);
            float age = (float)(position - doodle.Start);
            PaintPath(ref painter, doodle.Art.Path, center, doodle.Placement.Size * painter.Height, doodle.Placement.Rotation,
                Math.Clamp(age / (float)doodle.Duration, 0, 1), (float)doodle.Duration, opacity, beads, age, doodle.Art.Wet, snow);
        }
    }

    private void DrawGlyph(ref FoliaPainter painter, in FoliaGlyph glyph, Vector2 center, float font, float reveal)
    {
        if (!_paths.TryGetValue(glyph, out var path))
        {
            painter.GlyphCentered(glyph, center.X, center.Y, font, Vector3.One, .87f * reveal);
            return;
        }
        PaintPath(ref painter, path, center, font, 0, reveal, 1, .87f, 0, reveal, [], false);
    }

    private static void PaintPath(ref FoliaPainter painter, in PenPath path, Vector2 center, float font, float rotation,
        float reveal, float duration, float opacity, float beads, float age, scoped ReadOnlySpan<WetStroke> wet, bool snow)
    {
        var (sin, cos) = MathF.SinCos(rotation);
        float width = Math.Max(1.2f, font * .09f * Math.Clamp(MathF.Sqrt(9f / path.Strokes.Length), .55f, 1));
        float gap = Math.Min(.06f, duration * .08f);
        float inkTime = Math.Max(.001f, duration - gap * (path.Strokes.Length - 1));
        for (int i = 0; i < path.Strokes.Length; i++)
        {
            ref readonly var stroke = ref path.Strokes[i];
            float phase = (reveal * duration - gap * i) / inkTime;
            if (phase <= stroke.Start) break;
            float progress = EasePen((phase - stroke.Start) / Math.Max(.00001f, stroke.End - stroke.Start));
            float end = stroke.Start + (stroke.End - stroke.Start) * progress;
            Vector2 lowest = new(center.X, float.NegativeInfinity), tip = center;
            foreach (ref readonly var line in path.Lines.AsSpan(stroke.First, stroke.Count))
            {
                if (end <= line.Start) break;
                Vector2 from = Place(line.From), to = Place(Vector2.Lerp(line.From, line.To,
                    Math.Clamp((end - line.Start) / Math.Max(.00001f, line.End - line.Start), 0, 1)));
                PaintStroke(ref painter, from, to, width, opacity);
                if (from.Y > lowest.Y) lowest = from;
                if (to.Y > lowest.Y) lowest = to;
                tip = to;
            }
            if (wet.IsEmpty) continue;
            ref readonly var details = ref wet[i];
            float strokeAge = age - (stroke.End * inkTime + gap * i);
            if (beads > .002f)
            {
                foreach (ref readonly var bead in details.Beads.AsSpan())
                {
                    if (bead.Along > end) break;
                    float offset = width * .5f + bead.Radius * .7f + bead.Jitter * 1.5f;
                    PaintBead(ref painter, Place(bead.Point + bead.Normal * (offset / font)), bead.Radius * (.55f + .45f * beads),
                        beads, strokeAge, bead.Point.X * 131.7f + bead.Point.Y * 71.3f + i, snow);
                }
                if (font >= 48)
                    PaintBead(ref painter, tip, font * details.EndRadius * (.5f + .5f * progress), beads, strokeAge, tip.X * 3.1f + tip.Y * 5.7f, snow);
            }
            float dripAge = strokeAge - details.Drip.Delay;
            if (details.Drip.Duration <= 0 || dripAge <= 0 || !float.IsFinite(lowest.Y))
                continue;
            float d = Math.Clamp(dripAge / details.Drip.Duration, 0, 1);
            float reach = details.Drip.Length * (d - MathF.Sin(d * 6 * MathF.PI) / (6 * MathF.PI));
            float radius = details.Drip.Radius * (1 - .35f * d);
            float strokePhase = details.Drip.Phase;
            lowest.Y += width * .35f;
            for (float offset = 0; reach >= 2 && opacity > .002f && offset < reach; offset += 4)
            {
                float last = Math.Min(reach, offset + 5);
                float taper = (.72f + .28f * offset / reach) * (1 + .15f * MathF.Sin((lowest.Y + offset) * .19f + strokePhase * 3));
                PaintStroke(ref painter, Trail(offset), Trail(last), radius * 1.5f * taper, opacity * .95f);
            }
            PaintBead(ref painter, Trail(reach), radius, beads, dripAge - details.Drip.Duration, strokePhase + .5f, snow);

            Vector2 Trail(float offset) => lowest + new Vector2(
                MathF.Sin(offset * .07f + strokePhase) * 1.6f + MathF.Sin(offset * .023f + strokePhase * 2) * 2.5f, offset);
        }
        return;

        Vector2 Place(Vector2 at)
        {
            Vector2 p = (at - new Vector2(.5f)) * font;
            return center + new Vector2(cos * p.X - sin * p.Y, sin * p.X + cos * p.Y);
        }
    }

    private static void PaintStroke(ref FoliaPainter painter, Vector2 from, Vector2 to, float width, float opacity)
    {
        Vector2 delta = to - from, center = (from + to) * .5f;
        float margin = width * 1.3f + 4;
        float length = delta.Length() + margin;
        painter.Shape(27, center.X - length * .5f, center.Y - margin * .5f, length, margin, Vector3.One, opacity,
            MathF.Atan2(delta.Y, delta.X), stroke: width);
    }

    private static void PaintBead(ref FoliaPainter painter, Vector2 center, float radius, float opacity, float age, float seed, bool snow)
    {
        if (snow && age > 0 && radius >= painter.Height * .0025f)
        {
            float h = MathF.Sin(seed * 12.9898f + 78.233f) * 43758.5453f;
            float minimum = painter.Height * .0025f;
            float scale = 1 + MathF.Log(radius / minimum);
            float frozen = Math.Clamp((age - 1.5f * scale * (.7f + .6f * (h - MathF.Floor(h)))) / (3 * scale), 0, 1);
            radius *= MathF.Pow(.75f, frozen);
            center.Y += Math.Min(painter.Height * .002f, painter.Height * .0005f * Math.Max(0, radius / minimum - 1)) * FoliaPainter.Ease(frozen);
        }
        painter.Shape(28, center.X - radius, center.Y - radius, radius * 2, radius * 2, Vector3.One, opacity);
    }
    private static float EasePen(float phase) => .4f * Math.Clamp(phase, 0, 1) + .6f * FoliaPainter.Ease(phase);
}
