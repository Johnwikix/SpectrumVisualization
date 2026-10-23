using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold OKLCH hue/chroma generation with gamut reduction and displayed contrast correction.
/// Oklab matrices: https://bottosson.github.io/posts/oklab/ (public-domain reference implementation).</summary>
internal static class FoliaEmphasisColor
{
    internal static Vector3 Create(Vector3[] palette, uint seed, float strength, bool light)
    {
        var lab = ToLab(palette[3]);
        float hue = lab.Y * lab.Y + lab.Z * lab.Z > .001f ? MathF.Atan2(lab.Z, lab.Y) : (seed % 360) * (MathF.PI / 180);
        hue += (55 + seed % 3 * 45) * (MathF.PI / 180);
        float chroma = .13f + .05f * strength, value = light ? .43f : .76f;
        Vector3 color = At(value, chroma, hue);
        if (Contrast(color, palette[0]) >= 4.5f) return color;
        float pole = Contrast(Vector3.One, palette[0]) >= Contrast(Vector3.Zero, palette[0]) ? 1 : 0;
        float low = 0, high = 1;
        for (int i = 0; i < 12; i++)
        {
            float mid = (low + high) * .5f;
            if (Contrast(At(value + (pole - value) * mid, chroma, hue), palette[0]) >= 4.5f) high = mid;
            else low = mid;
        }
        return At(value + (pole - value) * high, chroma, hue);
    }

    internal static float Contrast(Vector3 foreground, Vector3 background)
    {
        float a = Luminance(foreground * .92f + background * .08f), b = Luminance(background);
        return (Math.Max(a, b) + .05f) / (Math.Min(a, b) + .05f);
    }

    private static float Luminance(Vector3 v) => Vector3.Dot(Linear(v), new(.2126f, .7152f, .0722f));
    private static Vector3 Linear(Vector3 v) => new(Linear(v.X), Linear(v.Y), Linear(v.Z));
    private static float Linear(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
    private static float Encode(float v) => v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - .055f;
    private static Vector3 ToLab(Vector3 color)
    {
        var c = Linear(color);
        float l = MathF.Cbrt(Vector3.Dot(c, new(.4122214708f, .5363325363f, .0514459929f)));
        float m = MathF.Cbrt(Vector3.Dot(c, new(.2119034982f, .6806995451f, .1073969566f)));
        float s = MathF.Cbrt(Vector3.Dot(c, new(.0883024619f, .2817188376f, .6299787005f)));
        return new(.2104542553f * l + .7936177850f * m - .0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + .4505937099f * s,
            .0259040371f * l + .7827717662f * m - .8086757660f * s);
    }

    private static Vector3 At(float lightness, float chroma, float hue)
    {
        float low = 0, high = chroma;
        Vector3 color = Convert(lightness, chroma, hue);
        if (!InGamut(color))
        {
            for (int i = 0; i < 12; i++)
            {
                float mid = (low + high) * .5f;
                if (InGamut(Convert(lightness, mid, hue))) low = mid; else high = mid;
            }
            color = Convert(lightness, low, hue);
        }
        color = Vector3.Clamp(color, Vector3.Zero, Vector3.One);
        return new(Encode(color.X), Encode(color.Y), Encode(color.Z));
    }
    private static bool InGamut(Vector3 v) => v.X >= 0 && v.Y >= 0 && v.Z >= 0 && v.X <= 1 && v.Y <= 1 && v.Z <= 1;
    private static Vector3 Convert(float lightness, float chroma, float hue)
    {
        float a = chroma * MathF.Cos(hue), b = chroma * MathF.Sin(hue);
        float l = lightness + .3963377774f * a + .2158037573f * b;
        float m = lightness - .1055613458f * a - .0638541728f * b;
        float s = lightness - .0894841775f * a - 1.2914855480f * b;
        l *= l * l; m *= m * m; s *= s * s;
        return new(4.0767416621f * l - 3.3077115913f * m + .2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - .3413193965f * s,
            -.0041960863f * l - .7034186147f * m + 1.7076147010f * s);
    }
}
