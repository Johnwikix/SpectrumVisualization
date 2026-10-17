using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaLumiere
{
    private sealed record MoteField(float DriftX, float DriftY, float Swirl, float Period, float Twinkle, float Ambient, float Gain, float[][] Items);
    private sealed record Echo(int Line, int First, int End, double Birth, float Across, float Speed, float Scale,
        float Rotation, float Stretch, float Shear, float Spin, Vector4[] Glyphs);
    private MoteField? _motes, _front;
    private Echo[] _echoes = [];
    private float _echoSize, _opening, _starBrightness;
    private float[][] _stars = [], _drops = [];

    private void ReadDecor(JsonElement root)
    {
        _motes = ReadMotes(root.GetProperty("motes"));
        _front = ReadMotes(root.GetProperty("front"));
        _echoSize = F(root, "echoSize");
        var echoes = new List<Echo>();
        foreach (var e in root.GetProperty("echoes").EnumerateArray())
        {
            var values = e.GetProperty("glyphs");
            var glyphs = new Vector4[values.GetArrayLength()];
            for (int i = 0; i < glyphs.Length; i++)
                glyphs[i] = V(values[i]);
            echoes.Add(new(e.GetProperty("line").GetInt32(), e.GetProperty("first").GetInt32(), e.GetProperty("end").GetInt32(), e.GetProperty("birth").GetDouble(),
                F(e, "across"), F(e, "speed"), F(e, "scale"), F(e, "rotation"), F(e, "stretch"), F(e, "shear"), F(e, "spin"), glyphs));
        }
        _echoes = echoes.ToArray();
        var stars = root.GetProperty("starfall");
        if (stars.ValueKind == JsonValueKind.Null)
        {
            _stars = [];
            _drops = [];
            _opening = _starBrightness = 0;
        }
        else
        {
            var spec = stars.GetProperty("spec");
            _opening = Math.Min(F(spec, "opening"), Math.Max(.8f, (_span.Y - _span.X) * .4f));
            _starBrightness = F(spec, "brightness");
            _stars = Rows(stars.GetProperty("stars"));
            _drops = Rows(stars.GetProperty("drops"));
        }
    }
    private static MoteField? ReadMotes(JsonElement field)
    {
        if (field.ValueKind == JsonValueKind.Null)
            return null;
        var spec = field.GetProperty("spec");
        return new(F(spec, "driftX"), F(spec, "driftY"), F(spec, "swirl"), F(spec, "swirlPeriod"), F(spec, "twinkle"), F(spec, "ambient"), F(spec, "gain"), Rows(field.GetProperty("items")));
    }
    private static float[][] Rows(JsonElement value)
    {
        var result = new float[value.GetArrayLength()][];
        for (int i = 0; i < result.Length; i++)
            result[i] = Floats(value[i]);
        return result;
    }
    private void WriteMotes(ref FoliaPainter p, MoteField? field, double time, bool bokeh)
    {
        if (field == null)
            return;
        float t = (float)time, aspect = p.Width / p.Height, omega = MathF.Tau / Math.Max(field.Period, .1f);
        foreach (var mote in field.Items)
        {
            float speed = .45f + mote[2] * .9f, swirl = field.Swirl * (.5f + mote[2]);
            float x = Wrap(mote[0] + field.DriftX * speed * t + swirl * MathF.Sin(t * omega * mote[6] + mote[4]), -.08f, aspect + .08f);
            float y = Wrap(mote[1] + field.DriftY * speed * t + swirl * MathF.Cos(t * omega * mote[7] + mote[5]), -.08f, 1.08f);
            float lit = Illumination(x, y) * field.Gain;
            float twinkle = 1 - field.Twinkle + field.Twinkle * (.5f + .5f * MathF.Sin(t * mote[9] * MathF.Tau + mote[8]));
            float alpha = Math.Min(1, (lit + field.Ambient) * twinkle);
            if (alpha <= .004f)
                continue;
            float size = mote[3] * p.Height;
            p.Shape(bokeh ? 19 : 17, x * p.Height - size * .5f, y * p.Height - size * .5f, size, size, _light, alpha);
        }
    }
    private static float Wrap(float value, float low, float high) => low + ((value - low) % (high - low) + high - low) % (high - low);
    private void WriteStars(ref FoliaPainter p, double time)
    {
        float local = (float)(time - _span.X), t = (float)time;
        foreach (var star in _stars)
        {
            float progress = (local - star[3]) / star[4];
            if (progress <= 0)
                continue;
            float eased = 1 - MathF.Pow(1 - Math.Clamp(progress, 0, 1), 3);
            float y = star[2] + (star[1] - star[2]) * eased;
            float speed = progress < 1 ? (star[1] - star[2]) * 3 * MathF.Pow(1 - progress, 2) / star[4] : 0;
            float landed = star[3] + star[4];
            float flash = local >= landed ? MathF.Exp(-(local - landed) / .25f) : 0;
            float twinkle = .55f + .45f * MathF.Sin(t * star[8] * MathF.Tau + star[7]);
            float basis = progress < 1 ? .9f : (star[6] > .5f ? .7f : .45f) * twinkle;
            float alpha = Math.Clamp((basis + Illumination(star[0], y) * .8f + flash) * _starBrightness, 0, 1);
            Star(ref p, star[0], y, star[5] * (1 + flash * 1.5f), alpha, speed, star[6] > .5f);
        }
        float rainIn = Math.Clamp((local - _opening * .6f) / 1.5f, 0, 1);
        foreach (var drop in _drops)
        {
            float cycle = (t / drop[1] + drop[2]) % 1;
            float y = -.1f + cycle * 1.25f, x = drop[0] + MathF.Sin(t * .7f + drop[2] * 6) * drop[4];
            float edge = Math.Clamp(cycle / .08f, 0, 1) * Math.Clamp((1 - cycle) / .15f, 0, 1);
            float alpha = Math.Clamp((.18f + Illumination(x, y) * 1.1f) * edge * rainIn * _starBrightness, 0, 1);
            Star(ref p, x, y, drop[3], alpha, 1.25f / drop[1], false);
        }
    }
    private void Star(ref FoliaPainter p, float x, float y, float size, float alpha, float speed, bool bright)
    {
        if (alpha <= .004f)
            return;
        x *= p.Height;
        y *= p.Height;
        float px = size * p.Height;
        if (speed > .05f)
        {
            float length = Math.Min(.25f, speed * .09f) * p.Height, width = Math.Max(1.5f, px * .7f);
            p.Shape(22, x - width * .5f, y - length, width, length, _light, alpha * .6f);
        }
        p.Shape(bright ? 18 : 17, x - px * .5f, y - px * .5f, px, px, _light, alpha);
    }
    private void WriteEchoes(ref FoliaPainter p, FoliaTextAtlas atlas, double time)
    {
        Vector4 beamA = _resolved[0], beamB = _resolved[6];
        float font = _echoSize * p.Height;
        foreach (var echo in _echoes)
        {
            float age = (float)(time - echo.Birth);
            float presence = FoliaPainter.Ease(age / .9f) * (1 - FoliaPainter.Ease((age - 4.5f) / 2.5f));
            if (age < 0 || age > 7 || presence <= .003f)
                continue;
            float distance = .08f + .075f * echo.Speed * age;
            float across = echo.Across * (beamB.X + distance * beamB.Y + .04f);
            float x = (beamA.X + beamA.Z * distance - beamA.W * across) * p.Height;
            float y = (beamA.Y + beamA.W * distance + beamA.Z * across) * p.Height;
            float alpha = presence * (.05f + .4f * Illumination(x / p.Height, y / p.Height));
            float split = .05f * MathF.Pow(age, 1.3f);
            var line = atlas.Lines[echo.Line];
            float left = line.Glyphs[echo.First].Offset;
            ref readonly var end = ref line.Glyphs[echo.End - 1];
            float right = end.Offset + end.Glyph.Advance + .04f * (echo.End - echo.First);
            float middle = (left + right) * .5f;
            int first = p.Count;
            for (int gi = echo.First; gi < echo.End; gi++)
            {
                ref readonly var glyph = ref line.Glyphs[gi];
                var meta = echo.Glyphs[gi - echo.First];
                float gx = (glyph.Offset + glyph.Glyph.Advance * .5f + .02f + .04f * (gi - echo.First) - middle + meta.X * split * meta.Z) * font;
                float gy = meta.Y * font * split * meta.Z;
                p.GlyphCentered(glyph.Glyph, gx, gy, font, _light, alpha, meta.W * split * 4,
                    outline: true, outlineWidth: font * .006f, letterSpacing: .04f);
            }
            // Pixi's rotation, skew and non-uniform scale remain one affine transform.
            float rotation = echo.Rotation + echo.Spin * age, skew = echo.Shear * age;
            var affine = new Matrix3x2(MathF.Cos(rotation) * echo.Scale * echo.Stretch, MathF.Sin(rotation) * echo.Scale * echo.Stretch,
                -MathF.Sin(rotation - skew) * echo.Scale, MathF.Cos(rotation - skew) * echo.Scale, x, y);
            p.AffineFrom(first, affine);
        }
    }
}
