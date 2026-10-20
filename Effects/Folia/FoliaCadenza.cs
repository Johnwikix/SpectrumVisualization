using System;
using System.Threading;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original Mindscape placement plans with a preallocated native animation state per fragment.</summary>
internal sealed class FoliaCadenza
{
    private sealed record Placement(int First, int Count, int Word, Vector2 Position, Vector2 Size,
        float Scale, float Rotation, Vector2 Drift);
    private struct Motion
    {
        internal float X, Y, Rotation, Scale, Alpha, Mix, Glow, Blur;
        internal bool Initialized;
    }
    private sealed record LineLayout(float Font, Placement[] Placements, Motion[] Motion);
    private long _atlasIdentity;
    private float _width, _height, _scale;
    private LineLayout[] _layouts = [];
    private int _lastLine = -1;
    private double _lastTime = double.NaN;
    internal float FrameDelta;
    internal float Energy;

    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float scale, string family, int weight, CancellationToken token = default)
    {
        if (_atlasIdentity == atlas.Identity && _width == width && _height == height && _scale == scale)
            return;
        string json = FoliaSourcePlanner.Compile(FoliaSourcePlanner.CreateInput(atlas, width, height, scale, family, weight), "compileCadenza", token);
        using var document = JsonDocument.Parse(json);
        var layouts = new LineLayout[atlas.Lines.Length];
        int lineIndex = 0;
        foreach (var line in document.RootElement.EnumerateArray())
        {
            var glyphs = atlas.Lines[lineIndex].Glyphs;
            var placements = new List<Placement>();
            foreach (var p in line.GetProperty("placements").EnumerateArray())
            {
                int word = p.GetProperty("wordIndex").GetInt32(), first = 0;
                while (first < glyphs.Length && glyphs[first].Word != word)
                    first++;
                first += p.GetProperty("fragmentStartInWord").GetInt32();
                int count = p.GetProperty("fragmentEndInWord").GetInt32() - p.GetProperty("fragmentStartInWord").GetInt32();
                count = Math.Min(count, glyphs.Length - first);
                if (count <= 0)
                    continue;
                placements.Add(new(first, count, word, new(F(p, "x"), F(p, "y")), new(F(p, "width"), F(p, "height")),
                    F(p, "scale"), F(p, "passedRotate") * MathF.PI / 180, new(F(p, "passedDriftX"), F(p, "passedDriftY"))));
            }
            layouts[lineIndex++] = new(F(line, "font"), placements.ToArray(), new Motion[placements.Count]);
        }
        _layouts = layouts;
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _scale = scale;
        _lastLine = -1;
        _lastTime = double.NaN;
    }
    private static float F(JsonElement value, string field) => value.GetProperty(field).GetSingle();

    internal void Write(ref FoliaPainter p, FoliaPreparedLine line, int index, double time, Vector3 primary, Vector3 accent)
    {
        var layout = _layouts[index];
        bool reset = index != _lastLine || Math.Abs(time - _lastTime) > .5;
        _lastLine = index;
        _lastTime = time;
        float dt = Math.Clamp(FrameDelta, .001f, .1f);
        float transformBlend = 1 - MathF.Exp(-11 * dt), visualBlend = 1 - MathF.Exp(-14 * dt), glowBlend = 1 - MathF.Exp(-16 * dt);
        float focus = p.Height * .42f + MathF.Sin((float)time * 2.3f) * (3 + Energy * 8);
        var timing = line.Source.Timing;
        int firstInstance = p.Count;
        for (int pi = 0; pi < layout.Placements.Length; pi++)
        {
            var plan = layout.Placements[pi];
            var word = line.Source.Words[plan.Word];
            double wordEnd = timing.Mode == 2 ? timing.RenderEnd : word.End;
            bool waiting = time < word.Start - (timing.Mode == 2 ? 0 : timing.Mode == 1 ? .045 : .18), passed = time > wordEnd;
            float progress = timing.Mode == 2 ? time < word.Start ? 0 : 1 : Math.Clamp((float)((time - word.Start) / Math.Max(timing.Mode == 1 ? .045 : .01, word.End - word.Start)), 0, 1);
            float after = Math.Clamp((float)(time - word.End) / 5, 0, 1);
            after = after < .5f ? 2 * after * after : 1 - MathF.Pow(-2 * after + 2, 2) * .5f;
            float pulse = 1 + MathF.Sin((float)time * 10 + (float)word.Start * 5) * .04f;
            float scale = waiting ? Math.Max(plan.Scale * .5f, .5f) : passed ? plan.Scale : plan.Scale * 1.3f * pulse;
            float rotation = waiting ? 20 * MathF.PI / 180 : passed ? plan.Rotation * after : 0;
            float x = p.Width * .5f + plan.Position.X + (passed ? plan.Drift.X * after : 0) + MathF.Sin((float)time * 1.2f + pi * .6f) * Energy * 4;
            float y = focus + plan.Position.Y + (passed ? plan.Drift.Y * after : 0) + MathF.Cos((float)time * 1.5f + pi * .4f) * Energy * 2.5f;
            float mix = time < word.Start ? 0 : !passed ? progress : 1 - Math.Clamp((float)(time - word.End) / .8f, 0, 1);
            float glow = time < word.Start ? 0 : !passed ? progress < .18f ? 1 - MathF.Pow(1 - progress / .18f, 3) : progress < .9f ? 1 : 1 - (progress - .9f) : .9f * MathF.Pow(1 - Math.Clamp((float)(time - word.End) / .9f, 0, 1), 2);
            ref var state = ref layout.Motion[pi];
            if (reset || !state.Initialized)
                state = new() { X = x, Y = y, Rotation = rotation + 16 * MathF.PI / 180, Scale = Math.Max(plan.Scale * .5f, .5f), Blur = 10, Initialized = true };
            state.X = Lerp(state.X, x, transformBlend);
            state.Y = Lerp(state.Y, y, transformBlend);
            state.Rotation = Lerp(state.Rotation, rotation, transformBlend);
            state.Scale = Lerp(state.Scale, scale, transformBlend);
            state.Alpha = Lerp(state.Alpha, waiting ? 0 : passed ? .82f : 1, visualBlend);
            state.Blur = Lerp(state.Blur, waiting ? 10 : 0, visualBlend);
            state.Mix = Lerp(state.Mix, mix, visualBlend);
            state.Glow = Lerp(state.Glow, glow, glowBlend);
            if (timing.Mode == 2)
                state = new() { X = x, Y = y, Rotation = 0, Scale = plan.Scale, Alpha = waiting || passed ? 0 : 1, Mix = progress, Glow = glow, Initialized = true };
            if (state.Alpha < .015f && state.Glow < .015f)
                continue;
            float font = layout.Font * state.Scale;
            float cos = MathF.Cos(state.Rotation), sin = MathF.Sin(state.Rotation);
            float anchorX = state.X + plan.Size.X * .5f, anchorY = state.Y - plan.Size.Y * .42f;
            float offset = line.Glyphs[plan.First].Offset;
            for (int gi = plan.First; gi < plan.First + plan.Count; gi++)
            {
                ref readonly var glyph = ref line.Glyphs[gi];
                float localX = ((glyph.Offset - offset + glyph.Glyph.BearingX + glyph.Glyph.Width * .5f) * layout.Font - plan.Size.X * .5f) * state.Scale;
                float localY = (glyph.Glyph.BearingY + glyph.Glyph.Height * .5f) * font - plan.Size.Y * .58f * state.Scale;
                float cx = anchorX + localX * cos - localY * sin, cy = anchorY + localX * sin + localY * cos;
                p.Glyph(glyph.Glyph, cx - (glyph.Glyph.BearingX + glyph.Glyph.Width * .5f) * font,
                    cy - (glyph.Glyph.BearingY + glyph.Glyph.Height * .5f) * font, font, Vector3.Lerp(primary, accent, state.Mix), state.Alpha,
                    state.Rotation, state.Blur, state.Glow * glow);
            }
        }
        ApplyEnvelope(ref p, firstInstance, line.Source, time, timing);
    }

    private static void ApplyEnvelope(ref FoliaPainter painter, int first, FoliaLine line, double time, in FoliaLineTiming timing)
    {
        if (timing.Mode == 2) return;
        double lastWordEnd = line.Words.Length > 0 ? line.Words[^1].End : line.End;
        double passStart = Math.Max(lastWordEnd, line.Start) + timing.Hold;
        double exitStart = Math.Max(passStart, timing.RenderEnd - timing.Exit);
        float enter;
        if (timing.Mode == 1)
        {
            enter = Out((float)(time - line.Start) / timing.Enter);
            exitStart = Math.Max(line.Start + timing.Enter + .01, exitStart);
        }
        else
        {
            float lead = Math.Min(.1f, timing.Enter * .35f);
            enter = Out((float)(time - line.Start + lead) / (timing.Enter + lead));
        }
        float exit = Out((float)(time - exitStart) / timing.Exit);
        float alpha = timing.Mode == 1 ? .65f + .35f * enter : enter;
        float scale = timing.Mode == 1 ? .97f + .03f * enter : .9f + .1f * enter;
        float blur = (timing.Mode == 1 ? 4 : 10) * (1 - enter);
        if (exit > 0)
        {
            alpha *= 1 - exit;
            scale += ((timing.Mode == 1 ? 1.03f : 1.1f) - scale) * exit;
            blur = Math.Max(blur, (timing.Mode == 1 ? 6 : 20) * exit);
        }
        var pivot = new Vector2(painter.Width * .5f, painter.Height * .5f);
        painter.TransformFrom(first, pivot, pivot, scale, 0);
        painter.FadeFrom(first, alpha, blur);
    }
    private static float Out(float value) => 1 - MathF.Pow(1 - Math.Clamp(value, 0, 1), 3);
    private static float Lerp(float a, float b, float value) => a + (b - a) * value;
}
