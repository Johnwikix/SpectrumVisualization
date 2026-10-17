using System;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaLumiere
{
    private readonly record struct Burst(int Line, int Glyph, double Time, float Size, float Offset, Vector3 Tint);
    private Burst[] _bursts = [];
    private bool _hasOpening;
    private float _sceneIntensity = 1;

    private void ReadBursts(JsonElement root)
    {
        _hasOpening = root.GetProperty("opening").GetBoolean();
        var values = root.GetProperty("bursts");
        _bursts = new Burst[values.GetArrayLength()];
        for (int i = 0; i < _bursts.Length; i++)
        {
            var value = values[i];
            var tint = value.GetProperty("tint");
            _bursts[i] = new(value.GetProperty("lineIndex").GetInt32(), value.GetProperty("glyphIndex").GetInt32(),
                value.GetProperty("time").GetDouble(), F(value, "size"), F(value, "dy"),
                new(tint[0].GetSingle(), tint[1].GetSingle(), tint[2].GetSingle()));
        }
    }

    private float BurstBoost(double time)
    {
        float boost = 0;
        foreach (ref readonly var burst in _bursts.AsSpan())
        {
            double age = time - burst.Time;
            if (age >= 0 && age <= .6)
                boost += .18f * MathF.Exp(-(float)age / .1f);
        }
        return Math.Min(boost, .45f);
    }

    private void WriteBursts(ref FoliaPainter painter, double time)
    {
        int count = 0;
        foreach (ref readonly var burst in _bursts.AsSpan())
        {
            float age = (float)(time - burst.Time);
            if (age < 0 || age > .6f)
                continue;
            if (++count > 14)
                break;
            _window.BeginLine(burst.Line);
            var anchor = _window.Resolve(burst.Glyph);
            float length = anchor.Font * burst.Size;
            float x = anchor.Position.X, y = anchor.Position.Y + anchor.Font * burst.Offset;
            Vector3 color = Vector3.Lerp(_light, burst.Tint, .85f), core = Vector3.Lerp(color, Vector3.One, .75f);
            float envelope = Math.Clamp(age / .03f, 0, 1) * MathF.Exp(-Math.Max(0, age - .03f) / .16f);
            float spread = 1 + 2 * age;
            float vertical = Math.Max(1, length * OutExpo(age / .08f));
            float horizontal = Math.Max(1, length * .62f * OutExpo((age - .02f) / .1f));
            float horizontalAlpha = Math.Clamp((age - .02f) / .03f, 0, 1);
            float ring = 1 - MathF.Pow(1 - Math.Clamp(age / .4f, 0, 1), 3);
            Sprite(ref painter, 19, x, y - length * .42f, length * .9f * ring, length * .9f * ring,
                color, .35f * (1 - ring) * Math.Clamp(age / .03f, 0, 1));
            Sprite(ref painter, 26, x, y - length * .2f, vertical, length * .1f * spread, color, .6f * envelope, MathF.PI / 2);
            Sprite(ref painter, 26, x, y - length * .42f, horizontal, length * .09f * spread, color, .55f * envelope * horizontalAlpha);
            Sprite(ref painter, 26, x, y - length * .2f, vertical, length * .02f * spread, core, envelope, MathF.PI / 2);
            Sprite(ref painter, 26, x, y - length * .42f, horizontal, length * .018f * spread, core, .95f * envelope * horizontalAlpha);
            float flash = length * (.35f + .8f * age);
            Sprite(ref painter, 17, x, y - length * .42f, flash, flash, core, MathF.Exp(-age / .07f));
        }
    }
    private static float OutExpo(float t) => t >= 1 ? 1 : 1 - MathF.Pow(2, -10 * Math.Clamp(t, 0, 1));
    private static void Sprite(ref FoliaPainter painter, int kind, float x, float y, float width, float height,
        Vector3 color, float alpha, float rotation = 0)
        => painter.Shape(kind, x - width * .5f, y - height * .5f, width, height, color, alpha, rotation);
}
