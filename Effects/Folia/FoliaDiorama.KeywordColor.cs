using System;
using System.Collections.Generic;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaDiorama
{
    private Vector3?[][] _keywordColors = [];
    private Vector3[]? _keywordPalette;

    private void PrepareKeywordColors(FoliaTextAtlas atlas, Vector3[]? palette)
    {
        _keywordPalette = palette;
        if (palette == null || !atlas.HasKeywords) { _keywordColors = []; return; }
        var colors = new Dictionary<Vector3, Vector3>();
        _keywordColors = new Vector3?[atlas.Lines.Length][];
        for (int li = 0; li < atlas.Lines.Length; li++)
        {
            var glyphs = atlas.Lines[li].Glyphs;
            Vector3?[] row = [];
            for (int gi = 0; gi < glyphs.Length; gi++)
                if (glyphs[gi].ActiveColor is { } keyword)
                {
                    if (row.Length == 0) row = new Vector3?[glyphs.Length];
                    if (!colors.TryGetValue(keyword, out var target))
                        colors.Add(keyword, target = ResolveKeywordColor(keyword, palette[1], palette[3], palette[0]));
                    row[gi] = target;
                }
            _keywordColors[li] = row;
        }
    }

    /// <summary>Original dioramaKeywordColor.ts: minimal separation, then displayed contrast at 0.92 opacity.
    /// THREE.Color works in linear RGB; the native stage stores encoded RGB. Conversion stays on the CPU worker.</summary>
    internal static Vector3 ResolveKeywordColor(Vector3 keyword, Vector3 primary, Vector3 accent, Vector3 background)
    {
        var color = Linear(keyword);
        primary = Linear(primary);
        accent = Linear(accent);
        if (Separation(color, primary) < .4f)
        {
            float low = 0, high = 1;
            for (int i = 0; i < 10; i++)
            {
                float mid = (low + high) * .5f;
                if (Separation(Vector3.Lerp(color, accent, mid), primary) >= .4f) high = mid;
                else low = mid;
            }
            color = Vector3.Lerp(color, accent, high);
        }
        Vector3 dark = Linear(new Vector3(5 / 255f));
        Vector3 pole = DisplayedContrast(Vector3.One, background) >= DisplayedContrast(dark, background) ? Vector3.One : dark;
        if (DisplayedContrast(color, background) < 4.5f)
        {
            float low = 0, high = 1;
            for (int i = 0; i < 10; i++)
            {
                float mid = (low + high) * .5f;
                if (DisplayedContrast(Vector3.Lerp(color, pole, mid), background) >= 4.5f) high = mid;
                else low = mid;
            }
            color = Vector3.Lerp(color, pole, high);
        }
        return Encoded(color);
    }

    private static float Separation(Vector3 a, Vector3 b) { var d = Vector3.Abs(a - b); return d.X + d.Y + d.Z; }
    private static Vector3 Linear(Vector3 v) => new(Linear(v.X), Linear(v.Y), Linear(v.Z));
    private static float Linear(float v) => v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f);
    private static Vector3 Encoded(Vector3 v) => new(Encoded(v.X), Encoded(v.Y), Encoded(v.Z));
    private static float Encoded(float v) => v <= .0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - .055f;
    private static float DisplayedContrast(Vector3 linear, Vector3 background)
    {
        float displayed = Vector3.Dot(Linear(Encoded(linear) * .92f + background * .08f), new(.2126f, .7152f, .0722f));
        float backdrop = Vector3.Dot(Linear(background), new(.2126f, .7152f, .0722f));
        return (Math.Max(displayed, backdrop) + .05f) / (Math.Min(displayed, backdrop) + .05f);
    }
}
