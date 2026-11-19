using System;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Native vector drawing for Hanabi's independent MG ornaments.</summary>
internal readonly record struct FoliaHanabiOrnament(string Kind, string Placement, Vector3 Color, Vector3 Accent,
    float Density, float Scale, double Start, double Exit, double Duration, bool Front, uint Seed)
{
    internal static FoliaHanabiOrnament[] Read(JsonElement shot, in FoliaHanabiFireworks.Palette palette)
    {
        var input = shot.GetProperty("ornaments");
        var ornaments = new FoliaHanabiOrnament[input.GetArrayLength()];
        double enter = shot.GetProperty("enterAt").GetDouble();
        double exit = shot.GetProperty("exitAt").GetDouble(), duration = shot.GetProperty("exitDuration").GetDouble();
        for (int i = 0; i < ornaments.Length; i++)
        {
            var value = input[i];
            string kind = value.GetProperty("kind").GetString()!;
            string place = value.GetProperty("placement").GetString()!;
            string role = value.GetProperty("color").GetString()!;
            Vector3 color = role switch
            {
                "spark" => palette.Sparks[0],
                "paper" => palette.Paper,
                "block" => palette.Blocks[0],
                "glow" => palette.Glow,
                _ => palette.Accent
            };
            ornaments[i] = new(kind, place, color, palette.Accent,
                value.GetProperty("density").GetSingle(), value.GetProperty("scale").GetSingle(),
                enter + value.GetProperty("delay").GetDouble(), exit, duration, value.GetProperty("layer").GetString() == "front",
                FoliaShotTimeline.Seed(shot.GetProperty("id").GetString()! + ":" + kind + ":" + i));
        }
        return ornaments;
    }

    internal void Write(ref FoliaPainter painter, Vector2 origin, Vector4 bounds, double time, float layerAlpha)
    {
        float age = (float)(time - Start);
        if (age < 0 || time >= Exit + Duration || Density <= 0)
            return;
        float exit = Math.Clamp((float)((time - Exit) / Math.Max(.05, Duration)), 0, 1);
        float opacity = layerAlpha * (1 - exit) * Math.Clamp(age / .1f, 0, 1);
        float unit = Math.Min(painter.Width, painter.Height);
        Vector2 half = new(Math.Max(1, bounds.Z * .5f), Math.Max(1, bounds.W * .5f));
        Vector2 center = origin + new Vector2(bounds.X, bounds.Y) + half;
        float baseSize = Math.Max(unit * .06f, Math.Min(half.X, half.Y) * .4f) * Scale;
        float factor = .35f + .9f * Density;
        float pad = baseSize * .35f;
        bool vertical = half.Y > half.X * 1.3f;
        if (Placement is "line-end" or "line-start")
        {
            float sign = Placement == "line-end" ? 1 : -1;
            center += vertical ? new Vector2(0, sign * (half.Y + pad)) : new Vector2(sign * (half.X + pad), 0);
            half = new(baseSize * .55f);
        }
        else if (Placement == "screen")
        {
            center = origin;
            half = new(painter.Width * .55f, painter.Height * .55f);
        }
        else if (Placement == "around")
            half += new Vector2(pad);

        switch (Kind)
        {
            case "rays":
            {
                float radius = Placement == "screen" ? new Vector2(painter.Width, painter.Height).Length() * .6f : half.Length() * 1.7f;
                float sweep = MathF.Tau / Math.Max(6, MathF.Round(12 * factor));
                float kick = 1 + .03f * MathF.Sin((float)time * MathF.Tau * .8f);
                radius *= Math.Max(.001f, OutBack(age / .6f, 1.6f)) * kick * (1 + .25f * exit);
                float rotation = (float)time * .12f - .8f * MathF.Pow(1 - Math.Clamp(age / .8f, 0, 1), 3) + exit * exit * 1.5f;
                float alpha = (Placement is "behind" or "screen" ? .26f : .45f) * opacity;
                for (float angle = 0; angle < MathF.Tau - .001f; angle += sweep)
                    painter.Triangle(center, center + Polar(angle + rotation) * radius,
                        center + Polar(angle + rotation + sweep * .5f) * radius, Color, alpha);
                break;
            }
            case "ring":
            case "dash-circle":
            {
                float radius = half.Length() * (Placement == "behind" ? .8f : .95f);
                int layers = Math.Clamp((int)MathF.Round(2.4f * factor), 1, 3);
                float line = Math.Max(1.5f, baseSize * .055f);
                for (int i = 0; i < layers; i++)
                {
                    float local = Math.Max(0, age - i * .09f);
                    float r = radius * (1 + .14f * i) * (.2f + .8f * OutBack(local / .55f, 2.4f)) *
                        (1 + .025f * MathF.Sin(MathF.Tau * ((float)time * .5f + i / 3f)));
                    float alpha = opacity * Math.Clamp(local / .08f, 0, 1);
                    painter.Shape(Kind == "ring" ? 2 : 21, center.X - r, center.Y - r, r * 2, r * 2,
                        (i & 1) == 0 ? Color : Accent, alpha, rotation: (float)time * .13f,
                        stroke: line * (i == 0 ? 1.6f : .8f), phase: 22);
                }
                if (Kind == "ring" && age > .6f)
                {
                    float q = ((age - .6f) / 1.8f) % 1, keep = 1 - q;
                    float r = radius * (1 + .55f * (1 - keep * keep * keep));
                    painter.Shape(2, center.X - r, center.Y - r, r * 2, r * 2, Accent,
                        opacity * .55f * keep * keep, stroke: line * .6f);
                }
                break;
            }
            case "spokes":
            case "streaks":
            {
                int count = Math.Clamp((int)(factor * 24), 6, 32);
                for (uint i = 0; i < count; i++)
                {
                    float drift = FoliaShotTimeline.Random(Seed, i + 7);
                    float angle = Kind == "streaks" ? -MathF.PI * .5f : (i + drift * .6f) * MathF.Tau / count;
                    Vector2 direction = Polar(angle);
                    float radius = half.Length() * (.8f + drift * .7f);
                    Vector2 at = center + direction * radius;
                    Vector2 far = at + direction * baseSize * (.6f + drift * .9f);
                    painter.Line(at.X, at.Y, far.X, far.Y, Color,
                        opacity * (.25f + drift * .42f), Math.Max(.85f, baseSize * .024f));
                }
                break;
            }
            case "zigzag":
            case "wave":
            case "underline":
            case "dots-line":
            {
                float rise = Math.Clamp(age / .55f, 0, 1);
                float y = center.Y + (Kind == "underline" ? half.Y * .82f : 0);
                float start = center.X - half.X, length = 2 * half.X;
                int steps = Math.Clamp((int)(length / Math.Max(5, baseSize * .24f)), 5, 42);
                Vector2 previous = new(start, y);
                for (int i = 0; i <= (int)(steps * rise); i++)
                {
                    float fraction = (float)i / steps;
                    float dy = Kind == "zigzag" ? ((i & 1) == 0 ? -.22f : .22f) * baseSize :
                        Kind == "wave" ? MathF.Sin(fraction * 16 - (float)time * 4) * baseSize * .18f : 0;
                    Vector2 at = new(start + length * fraction, y + dy);
                    if (Kind == "dots-line")
                        Dot(ref painter, at, Math.Max(1.5f, baseSize * .11f), Color, opacity * .75f);
                    else if (i > 0)
                        painter.Line(previous.X, previous.Y, at.X, at.Y, Color, opacity * .69f, Math.Max(1.2f, baseSize * .07f));
                    previous = at;
                }
                break;
            }
            case "brackets":
            case "squares":
            {
                Vector2 size = half * 2 + new Vector2(baseSize * .5f);
                Vector2 top = center - size * .5f;
                painter.Shape(7, top.X, top.Y, size.X, size.Y, Color, opacity * .65f, stroke: Math.Max(1.3f, baseSize * .04f));
                if (Kind == "squares")
                    painter.Shape(7, top.X - baseSize * .22f, top.Y - baseSize * .22f, size.X + baseSize * .44f, size.Y + baseSize * .44f,
                        Accent, opacity * .45f, (float)time * .025f, Math.Max(1.3f, baseSize * .035f));
                break;
            }
            case "halftone":
            {
                if (Placement == "around")
                    half = half * new Vector2(1.2f, 1.35f) + baseSize * new Vector2(.4f, .6f);
                else if (Placement == "behind")
                    half = half * new Vector2(1.15f, 1.6f) + baseSize * new Vector2(.3f, .4f);
                else if (Placement == "glyphs")
                    half *= 1.4f;
                float perSite = Placement == "glyphs" ? 25 : Placement == "corners" ? 36 : Math.Clamp(170 * factor, 40, 260);
                float spacing = Math.Max(baseSize * .18f, MathF.Sqrt(4 * half.X * half.Y / perSite));
                int columns = Math.Max(2, (int)MathF.Round(2 * half.X / spacing));
                int rows = Math.Max(2, (int)MathF.Round(2 * half.Y / spacing));
                var diagonal = Polar(MathF.Floor(FoliaShotTimeline.Random(Seed, 17) * 4) * MathF.PI / 2 + MathF.PI / 4);
                float projectionScale = .7f / half.Length();
                for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    Vector2 offset = new((x - (columns - 1) * .5f + (y & 1) * .5f) * spacing, (y - (rows - 1) * .5f) * spacing);
                    float projection = Math.Clamp(.5f + Vector2.Dot(offset, diagonal) * projectionScale, 0, 1);
                    float reveal = age - projection * .55f;
                    if (reveal <= 0)
                        continue;
                    float leave = exit * 1.6f - (1 - projection) * .6f;
                    float pop = OutBack(reveal / .22f, 2.5f) * (1 - Math.Clamp(leave / .4f, 0, 1));
                    float wave = .22f * MathF.Sin(MathF.Tau * (projection * 1.5f - (float)time * .4f));
                    float size = spacing * .92f * Math.Clamp(1 - projection * .9f + wave, 0, 1) * pop;
                    if (size > .4f)
                        Dot(ref painter, center + offset, size, Color, opacity * .75f);
                }
                break;
            }
            case "stamp":
                painter.Shape(2, center.X - baseSize * .42f, center.Y - baseSize * .42f, baseSize * .84f, baseSize * .84f,
                    Color, opacity * .75f, stroke: Math.Max(1.2f, baseSize * .055f));
                break;
            default:
            {
                int count = Math.Clamp((int)MathF.Round(12 * factor), 4, 20);
                for (uint i = 0; i < count; i++)
                {
                    float seed = FoliaShotTimeline.Random(Seed, i + 11);
                    float phase = (age * (.35f + seed * .2f) + seed) % 1;
                    float angle = (i + seed) * MathF.Tau / count;
                    if (Kind == "orbit")
                        angle += (float)time * .31f;
                    Vector2 at = center + Polar(angle) * half * (Kind == "orbit" ? 1.1f : .75f + phase * .6f);
                    if (Kind is "confetti" or "petals") at.Y += phase * unit * .2f;
                    float size = baseSize * (.08f + seed * .2f);
                    float a = opacity * (.25f + .5f * MathF.Sin(phase * MathF.PI));
                    int shape = Kind is "sparkle" or "glints" or "cross" or "chevrons" ? 9 : Kind == "shards" ? 8 : 16;
                    painter.Shape(shape, at.X - size * .5f, at.Y - size * .5f, size, size, (i & 1) == 0 ? Color : Accent, a, angle);
                }
                break;
            }
        }
    }

    private static float OutBack(float value, float k)
    {
        float x = Math.Clamp(value, 0, 1) - 1;
        return 1 + (k + 1) * x * x * x + k * x * x;
    }

    private static Vector2 Polar(float angle)
    {
        var (y, x) = MathF.SinCos(angle);
        return new(x, y);
    }

    private static void Dot(ref FoliaPainter painter, Vector2 at, float size, Vector3 color, float alpha) =>
        painter.Shape(16, at.X - size * .5f, at.Y - size * .5f, size, size, color, alpha);
}
