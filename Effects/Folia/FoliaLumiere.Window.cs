using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaLumiere
{
    private void WriteWindow(ref FoliaPainter p, FoliaTextAtlas atlas, double time)
    {
        _window.BeginFrame(time);
        WriteBursts(ref p, time);
        // Pixi orders complete layers: tracks / spots / halos, then glyphs, then stars.
        for (int line = _window.Low; line <= _window.High; line++)
        {
            _window.BeginLine(line);
            _window.WriteTrails(ref p, _lit);
            _window.WriteSpot(ref p, _lit);
        }
        for (int layer = 0; layer < 3; layer++)
        for (int line = _window.Low; line <= _window.High; line++)
        {
            _window.BeginLine(line);
            var glyphs = atlas.Lines[line].Glyphs;
            for (int i = 0; i < glyphs.Length; i++)
            {
                var pose = _window.Resolve(i);
                float alpha = pose.Alpha * FoliaPainter.Ease((float)time / .6f);
                if (alpha <= .003f)
                    continue;
                float x = pose.Position.X, y = pose.Position.Y, font = pose.Font, rotation = pose.Rotation, flying = pose.Flying;
                ref readonly var glyph = ref glyphs[i];
                float lit = FoliaPainter.Ease((float)((time - glyph.Start) / Math.Max(.3, glyph.End - glyph.Start)));
                float age = (float)(time - glyph.Start);
                float flash = age <= 0 ? 0 : age < .15f ? FoliaPainter.Ease(age / .15f) : MathF.Exp(-(age - .15f) / .45f);
                float illumination = Illumination(x / p.Height, y / p.Height);
                float heat = Math.Clamp(illumination * 1.3f + flash * .2f, 0, 1);
                float revealed = Math.Min(1, .22f + illumination * .45f + flying * .4f);
                float opacity = alpha * (revealed * (1 - lit) + lit * Math.Min(1, .55f + .4f * heat + .3f * flying));
                Vector3 hot = Vector3.Lerp(_lit, Vector3.One, Math.Clamp(.08f * illumination + .1f * flash + .25f * flying, 0, 1));
                Vector3 cold = Vector3.Lerp(_unlit, _lit, illumination * .35f);
                Vector3 color = Vector3.Lerp(cold, hot, lit);
                if (layer == 1)
                    p.GlyphCentered(glyph.Glyph, x, y, font, color, opacity, rotation, letterSpacing: .04f);
                if (layer == 2)
                {
                    var star = _window.Star(i);
                    float starAlpha = _window.StarStrength(pose) * flash * .55f;
                    float size = font * star.W * (.8f + .5f * flash);
                    p.Shape(18, x + font * star.X - size * .5f, y + font * star.Y - size * .5f, size, size,
                        Vector3.Lerp(_lit, Vector3.One, .5f), starAlpha, rotation: star.Z * (1 + (1 - flash) * .6f));
                }
                float halo = alpha * lit * (.03f + .14f * illumination + .08f * flash);
                if (layer == 0 && halo > .003f)
                {
                    float size = font * (2.2f + .8f * illumination);
                    p.Shape(17, x - size * .5f, y - size * .45f, size, size * .9f, _lit, halo);
                }
            }
        }
    }
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
