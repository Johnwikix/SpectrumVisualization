using System;
using System.Numerics;
using System.Text.Json;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Render-owned waiting / active / passed transitions shared by Classic and Partita.</summary>
internal sealed class FoliaGlowWords
{
    internal readonly record struct Pose(Vector2 Offset, float Scale, float Rotation, float Alpha, float Blur, float ColorMix, float Glow);
    private struct Motion
    {
        internal bool Initialized;
        internal int Status;
        internal double Since;
        internal Pose From, Current;
    }
    private long _atlasIdentity;
    private Motion[] _words = [];
    private int _line = -1, _mode = -1;
    private double _elapsed, _position = double.NaN;
    internal double HandoffTime { get; private set; }
    internal FoliaLineHandoff? Handoff { get; private set; }
    private FoliaLineTiming _timing;
    private FoliaPreparedLine[] _classic = [];
    internal float FrameDelta { private get; set; }
    internal float Energy { get; set; }
    internal FoliaChatMotion? Chat { get; private set; }

    internal void Prepare(FoliaTextAtlas atlas, bool handoff = false, bool classic = false, CancellationToken token = default)
    {
        if (_atlasIdentity == atlas.Identity)
            return;
        int count = 0;
        foreach (var line in atlas.Lines)
            count = Math.Max(count, line.Source.Words.Length);
        _words = new Motion[count];
        Handoff = handoff ? new FoliaLineHandoff() : null;
        _atlasIdentity = atlas.Identity;
        _line = -1;
        Chat = new FoliaChatMotion(atlas.Lines.Length);
        if (classic)
        {
            using var document = JsonDocument.Parse(FoliaSourcePlanner.Compile(FoliaSourcePlanner.CreateInput(atlas, 1, 1, 1, "", 300), "compileClassic", token));
            _classic = new FoliaPreparedLine[atlas.Lines.Length];
            int li = 0;
            foreach (var row in document.RootElement.EnumerateArray())
            {
                var original = atlas.Lines[li];
                var words = new FoliaWord[row.GetArrayLength()];
                var glyphs = (FoliaPreparedGlyph[])original.Glyphs.Clone();
                var mapping = new int[original.Source.Words.Length];
                int wi = 0, originalWord = 0;
                foreach (var item in row.EnumerateArray())
                {
                    string text = item.GetProperty("text").GetString()!;
                    words[wi] = new(text, item.GetProperty("startTime").GetDouble(), item.GetProperty("endTime").GetDouble());
                    int length = 0;
                    while (originalWord < mapping.Length && length < text.Length)
                    {
                        length += original.Source.Words[originalWord].Text.Length;
                        mapping[originalWord++] = wi;
                    }
                    wi++;
                }
                // Align parser-word spans, rather than re-segmenting joined Unicode fragments.
                // A contraction can join a combining mark or emoji sequence across parser boundaries.
                for (int gi = 0; gi < glyphs.Length; gi++)
                    glyphs[gi] = glyphs[gi] with { Word = mapping[glyphs[gi].Word] };
                _classic[li++] = original with { Source = original.Source with { Words = words }, Glyphs = glyphs };
            }
        }
    }
    internal FoliaPreparedLine ClassicLine(int index, FoliaPreparedLine fallback) => (uint)index < (uint)_classic.Length ? _classic[index] : fallback;

    internal static float CharGlow(in FoliaPreparedGlyph glyph, double time, double wordEnd)
    {
        float progress = (float)((time - glyph.Start) / Math.Max(.001, (glyph.End - glyph.Start) * 6));
        if (progress <= 0 || progress >= 1) return 0;
        float value = progress < .3f ? 1 - MathF.Pow(1 - progress / .3f, 3) : 1 - (progress - .3f) / .7f;
        return value * MathF.Pow(1 - Math.Clamp((float)(time - wordEnd) / .9f, 0, 1), 2);
    }

    internal static void Ripple(ref FoliaPainter painter, bool chorus, double start, double end, double time, Vector2 center, float font, Vector3 color, in FoliaLineTiming timing)
    {
        float age = (float)(time - start + timing.Lookahead);
        if (!chorus || age < 0 || age >= .5f || time > timing.WordEnd(start, end)) return;
        float progress = 1 - MathF.Pow(1 - age / .5f, 3);
        float targetScale = 1.5f + FoliaPainter.Random(start, 71.3) * 2;
        float diameter = font * 1.5f * (.2f + (targetScale - .2f) * progress);
        painter.Shape(2, center.X - diameter * .5f, center.Y - diameter * .5f, diameter, diameter, color, .8f * (1 - progress));
    }
    internal void Begin(int line, int mode, double time)
    {
        if (_line != line || _mode != mode || double.IsNaN(_position) || Math.Abs(time - _position) > .5)
        {
            _words.AsSpan().Clear();
            _elapsed = 0;
        }
        _line = line;
        _mode = mode;
        _position = time;
        _elapsed += Math.Clamp(FrameDelta, 0, .1f);
        HandoffTime += Math.Clamp(FrameDelta, 0, .1f);
    }
    internal void SetTiming(FoliaLineTiming timing) => _timing = timing;
    internal Pose Resolve(int index, double start, double end, double time, Vector2 offset, float rotation, float scale, float passedRotation)
    {
        if (_timing.Mode == 2)
            return new(offset, scale, rotation, time >= start - _timing.Lookahead ? 1 : 0, 0, 1, 0);
        ref var motion = ref _words[index];
        if (!motion.Initialized)
        {
            motion.Initialized = true;
            motion.Current = new(offset + new Vector2(MathF.Sin(offset.Y) * 100, MathF.Cos(offset.X) * 50), .5f, rotation + 20 * MathF.PI / 180, 0, 10, 0, 0);
        }
        end = _timing.WordEnd(start, end);
        int status = time < start - _timing.Lookahead ? 0 : time <= end ? 1 : 2;
        if (motion.Status != status)
        {
            motion.Status = status;
            motion.From = motion.Current;
            motion.Since = _elapsed;
        }
        if (status == 0)
            return motion.Current;
        float age = (float)(_elapsed - motion.Since) * (_timing.Mode == 1 ? 2.5f : 1);
        float transform = status == 1 ? 1 - MathF.Exp(-10 * age) * (MathF.Cos(10 * age) + MathF.Sin(10 * age))
            : FoliaMotion.Bezier(0, 0, .58f, 1, age / .5f);
        float opacity = FoliaMotion.Bezier(0, 0, .58f, 1, age / (status == 1 ? .1f : .5f));
        float turn = status == 1 ? transform : Math.Clamp(age / 5, 0, 1);
        float targetScale = scale * (status == 1 ? 1.4f : 1);
        float targetRotation = rotation + (status == 2 ? passedRotation : 0);
        float color = status == 1 ? Math.Clamp(age / Math.Max(.1f, (float)(end - start)), 0, 1) : FoliaMotion.Bezier(.42f, 0, .58f, 1, age / .8f);
        float blur = Math.Clamp(age / (status == 1 ? .2f : .5f), 0, 1);
        float glow = status == 1 ? FoliaMotion.Bezier(0, 0, .58f, 1, age / .15f) : Math.Clamp(age / .8f, 0, 1);
        motion.Current = new(Vector2.Lerp(motion.From.Offset, offset, transform), Lerp(motion.From.Scale, targetScale, transform),
            Lerp(motion.From.Rotation, targetRotation, turn), Lerp(motion.From.Alpha, status == 1 ? 1 : .82f, opacity),
            Lerp(motion.From.Blur, 0, blur), Lerp(motion.From.ColorMix, status == 1 ? 1 : 0, color), Lerp(motion.From.Glow, status == 1 ? .8f : 0, glow));
        return motion.Current;
    }
    internal static void Breathe(ref FoliaPainter painter, int first, float animation)
    {
        float phase = animation % 7 / 7 * 4;
        int segment = (int)phase;
        float mix = FoliaMotion.Bezier(.42f, 0, .58f, 1, phase - segment);
        ReadOnlySpan<float> y = [0, -14, 0, 6.3f, 0], scale = [1, 1.01f, 1, .995f, 1];
        var pivot = new Vector2(painter.Width * .5f, painter.Height * .5f);
        painter.TransformFrom(first, pivot, pivot + new Vector2(0, Lerp(y[segment], y[segment + 1], mix)), Lerp(scale[segment], scale[segment + 1], mix), 0);
    }
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
