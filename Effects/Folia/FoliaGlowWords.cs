using System;
using System.Numerics;

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
    internal float FrameDelta { private get; set; }

    internal void Prepare(FoliaTextAtlas atlas)
    {
        if (_atlasIdentity == atlas.Identity)
            return;
        int count = 0;
        foreach (var line in atlas.Lines)
            count = Math.Max(count, line.Source.Words.Length);
        _words = new Motion[count];
        _atlasIdentity = atlas.Identity;
        _line = -1;
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
    }
    internal Pose Resolve(int index, double start, double end, double time, Vector2 offset, float rotation, float scale, float passedRotation)
    {
        ref var motion = ref _words[index];
        if (!motion.Initialized)
        {
            motion.Initialized = true;
            motion.Current = new(offset + new Vector2(MathF.Sin(offset.Y) * 100, MathF.Cos(offset.X) * 50), .5f, rotation + 20 * MathF.PI / 180, 0, 10, 0, 0);
        }
        int status = time < start - .15 ? 0 : time <= end ? 1 : 2;
        if (motion.Status != status)
        {
            motion.Status = status;
            motion.From = motion.Current;
            motion.Since = _elapsed;
        }
        if (status == 0)
            return motion.Current;
        float age = (float)(_elapsed - motion.Since);
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
