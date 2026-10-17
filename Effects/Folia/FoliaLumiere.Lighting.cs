using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaLumiere
{
    private readonly record struct ResolvedLight(Vector4 A, Vector4 B, Vector4 C, Vector4 D, Vector4 E, Vector4 F);
    private static Vector3 Glow(Vector3 value, Vector3 fallback)
    {
        float peak = Math.Max(value.X, Math.Max(value.Y, value.Z));
        return peak < .04f ? fallback : value / peak;
    }
    private int Resolve(Profile profile, double time, float local, float aspect, float bass, float weight, Span<ResolvedLight> output)
    {
        int count = Math.Min(profile.Beams.Length, 6);
        for (int i = 0; i < count; i++)
        {
            var b = profile.Beams[i];
            float angle = b[0].Z + Oscillate(b[5], (float)time);
            float pulse = 1 + Oscillate(b[6], (float)time);
            float reveal = b[3].W > 0 ? .06f + .94f * FoliaPainter.Ease(local / b[3].W) : 1;
            output[i] = new(new(b[0].X * aspect, b[0].Y, MathF.Cos(angle), MathF.Sin(angle)),
                new(b[0].W * .5f, MathF.Tan(b[1].X), b[1].Z, b[1].Y * reveal),
                new(b[1].W * pulse * (1 + .15f * MathF.Pow(Math.Max(0, bass), 1.5f)) * weight * _sceneIntensity, b[2].X, b[2].Y, (float)time * b[2].Z),
                new(new Vector3(b[3].X, b[3].Y, b[3].Z) * _light, b[2].W),
                new(b[4].X, b[4].Y, b[4].Z, b[4].W * (float)time), new(b[5].W, b[6].W, 0, 0));
        }
        return count;
    }
    private float Illumination(float x, float y)
    {
        float sum = 0;
        for (int i = 0; i < 6; i++)
        {
            var a = _resolved[i];
            var b = _resolved[6 + i];
            var c = _resolved[12 + i];
            var d = _resolved[18 + i];
            var e = _resolved[24 + i];
            var f = _resolved[30 + i];
            if (c.X <= 0)
                continue;
            float dx = x - a.X, dy = y - a.Y;
            float along = dx * a.Z + dy * a.W;
            if (along <= 0)
                continue;
            float signed = -dx * a.W + dy * a.Z;
            float half = Math.Abs(b.X + along * b.Y) + .0001f;
            float cross = Math.Abs(signed) / half;
            float edge = Smooth(1, 1 - Math.Max(b.Z, .02f), cross);
            if (edge <= 0)
                continue;
            float u = cross * MathF.Sign(signed);
            float streak = 1 - c.Y + c.Y * Noise1(u * c.Z + c.W);
            float core = 1 - d.W + d.W * (1 - cross * cross);
            float reach = f.Y > 0 ? 1 - Smooth(f.Y - Math.Max(.04f, half * 2.5f), f.Y, along) : 1;
            float pattern = 1;
            int kind = (int)e.X;
            if (kind == 1)
                pattern = Band(u * e.Y * .5f + e.W, e.Z);
            else if (kind == 2)
                pattern = Smooth(e.Z * .5f, e.Z * .5f + .08f, Math.Abs(u));
            else if (kind == 3)
                pattern = Band(u * e.Y * .5f + along * e.Y * .8f + e.W, e.Z) * Band(u * e.Y * .5f - along * e.Y * .8f - e.W, e.Z);
            else if (kind == 4)
                pattern = Smooth(e.Z - .12f, e.Z + .12f, Noise2(u * e.Y, along * e.Y * .6f + e.W));
            float gobo = kind > 0 ? .12f + .88f * pattern : 1;
            sum += edge * streak * core * MathF.Exp(-along / Math.Max(b.W, .001f)) * gobo * reach * Smooth(0, Math.Abs(b.X) * 2 + .01f, along) * c.X;
        }
        return 1 - MathF.Exp(-sum);
    }
    private static float Smooth(float a, float b, float value) => FoliaPainter.Ease((value - a) / (b - a));
    private static float Fract(float value) => value - MathF.Floor(value);
    private static float Hash1(float input)
    {
        float p = Fract(input * .1031f);
        p *= p + 33.33f;
        p *= p + p;
        return Fract(p);
    }
    private static float Hash2(float x, float y)
    {
        float a = Fract(x * .1031f), b = Fract(y * .1031f), c = a;
        float d = a * (b + 33.33f) + b * (c + 33.33f) + c * (a + 33.33f);
        return Fract((a + b + 2 * d) * (c + d));
    }
    private static float Noise1(float value)
    {
        float i = MathF.Floor(value), f = FoliaPainter.Ease(value - i);
        return Lerp(Hash1(i), Hash1(i + 1), f);
    }
    private static float Noise2(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y), u = FoliaPainter.Ease(x - ix), v = FoliaPainter.Ease(y - iy);
        return Lerp(Lerp(Hash2(ix, iy), Hash2(ix + 1, iy), u), Lerp(Hash2(ix, iy + 1), Hash2(ix + 1, iy + 1), u), v);
    }
    private static float Band(float x, float duty)
    {
        float f = Fract(x);
        return Math.Min(1, 1 - Smooth(duty - .06f, duty + .06f, f) + Smooth(.94f, 1, f));
    }
}
