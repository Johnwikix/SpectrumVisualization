using System;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Immutable launch and particle plans from Hanabi's closed-form fireworks planner.</summary>
internal sealed class FoliaHanabiFireworks
{
    internal readonly record struct Palette(Vector3 Ink, Vector3 Paper, Vector3 Accent, Vector3 Glow, Vector3[] Blocks, Vector3[] Sparks)
    {
        internal static Palette Read(JsonElement value)
        {
            return new(Rgb(value.GetProperty("ink").GetInt32()), Rgb(value.GetProperty("paper").GetInt32()),
                Rgb(value.GetProperty("accent").GetInt32()), Rgb(value.GetProperty("glow").GetInt32()),
                ReadColors(value.GetProperty("blocks")), ReadColors(value.GetProperty("sparks")));
        }

        private static Vector3[] ReadColors(JsonElement colors)
        {
            var values = new Vector3[colors.GetArrayLength()];
            for (int i = 0; i < values.Length; i++)
                values[i] = Rgb(colors[i].GetInt32());
            return values;
        }
    }

    private readonly record struct Star(Vector2 Origin, Vector2 Velocity, float Drag, float Fall, double Start,
        float Life, float Size, Vector3 Color, Vector3 EndColor, float Shift, Vector3 TrailColor, float Trail,
        int Segments, float FadeTail, float Strobe, float StrobeFrom, float Twinkle, float Glitter, float Spiral,
        float Spin, float Phase, float Depth, float Rank, int Texture);

    private readonly record struct Flash(Vector2 Position, double Start, float Life, float Size, Vector3 Color);
    private readonly record struct Launch(Vector2 Origin, Vector2 Target, double Start, float Duration, Vector3 Color, float Phase, float Frequency);
    private sealed record Burst(double Start, double End, Star[] Stars, Flash[] Flashes);

    private readonly Burst[] _bursts;
    private readonly Launch[] _launches;

    internal FoliaHanabiFireworks(JsonElement bursts, JsonElement launches)
    {
        _bursts = new Burst[bursts.GetArrayLength()];
        for (int i = 0; i < _bursts.Length; i++)
        {
            var burst = bursts[i];
            var stars = burst.GetProperty("stars");
            var flashes = burst.GetProperty("flashes");
            var points = new Star[stars.GetArrayLength()];
            var lights = new Flash[flashes.GetArrayLength()];
            for (int j = 0; j < points.Length; j++)
            {
                var s = stars[j];
                points[j] = new(new(Number(s, 0), Number(s, 1)), new(Number(s, 2), Number(s, 3)), Number(s, 4), Number(s, 5),
                    s[6].GetDouble(), Number(s, 7), Number(s, 8), Rgb(s[9].GetInt32()), Rgb(s[10].GetInt32()), Number(s, 11),
                    Rgb(s[12].GetInt32()), Number(s, 13), s[14].GetInt32(), Number(s, 15), Number(s, 16), Number(s, 17),
                    Number(s, 18), Number(s, 19), Number(s, 20), Number(s, 21), Number(s, 22), Number(s, 23), Number(s, 24), s[25].GetInt32());
            }
            for (int j = 0; j < lights.Length; j++)
            {
                var f = flashes[j];
                lights[j] = new(new(Number(f, "x"), Number(f, "y")), f.GetProperty("at").GetDouble(), Number(f, "life"),
                    Number(f, "size"), Rgb(f.GetProperty("color").GetInt32()));
            }
            _bursts[i] = new(burst.GetProperty("at").GetDouble(), burst.GetProperty("end").GetDouble(), points, lights);
        }

        _launches = new Launch[launches.GetArrayLength()];
        for (int i = 0; i < _launches.Length; i++)
        {
            var launch = launches[i];
            int salt = (int)(FoliaShotTimeline.Seed(launch.GetProperty("seed").GetString()!) % 100000);
            _launches[i] = new(new(Number(launch, "fromX"), Number(launch, "fromY")), new(Number(launch, "toX"), Number(launch, "toY")),
                launch.GetProperty("at").GetDouble(), Number(launch, "duration"), Rgb(launch.GetProperty("color").GetInt32()),
                Noise(2, salt) * MathF.Tau, 2.2f + Noise(1, salt) * 1.6f);
        }
    }

    private static float Number(JsonElement value, int index) => value[index].GetSingle();
    private static float Number(JsonElement value, string key) => value.GetProperty(key).GetSingle();
    internal static Vector3 Rgb(int rgb) => new((rgb >> 16) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
    private static float Noise(float n, float salt)
    {
        double value = Math.Sin(n * 127.1 + salt * 311.7) * 43758.5453123;
        return (float)(value - Math.Floor(value));
    }

    private static Vector2 Point(in Star star, float time)
    {
        float drag = (1 - MathF.Exp(-star.Drag * time)) / star.Drag;
        Vector2 point = star.Origin + star.Velocity * drag + new Vector2(0, star.Fall * (time - drag));
        if (star.Spiral > 0)
        {
            var (sin, cos) = MathF.SinCos(star.Phase + star.Spin * time);
            point += new Vector2(cos, sin) * star.Spiral * (1 - MathF.Exp(-3 * time));
        }
        return point;
    }

    internal void Write(ref FoliaPainter painter, double time, float layerAlpha)
    {
        float unit = Math.Min(painter.Width, painter.Height);
        int budget = Math.Min(5400, painter.Remaining / 2);
        int first = painter.Count;
        int demand = 0;
        foreach (var burst in _bursts)
            if (time >= burst.Start && time <= burst.End)
                foreach (ref readonly var star in burst.Stars.AsSpan())
                    if (time >= star.Start && time <= star.Start + star.Life)
                        demand += 1 + star.Segments;
        float keep = demand <= budget ? 2 : budget * .92f / Math.Max(1, demand);

        foreach (ref readonly var launch in _launches.AsSpan())
        {
            float age = (float)(time - launch.Start);
            if (age < 0 || age > launch.Duration + .6f)
                continue;
            float p = Math.Clamp(age / launch.Duration, 0, 1);
            Vector2 head = Head(launch, p);
            if (p < 1)
                Sprite(ref painter, 18, head, unit * .029f, launch.Color, layerAlpha, 0);
            for (int j = 1; j <= 5; j++)
            {
                float t = Math.Max(0, p - j * .084f);
                Vector2 tail = Head(launch, t);
                float opacity = (1 - j / 6f) * (1 - Math.Clamp((age - launch.Duration) / .6f, 0, 1));
                painter.Line(tail.X, tail.Y, head.X, head.Y, launch.Color * 1.45f, opacity * layerAlpha, Math.Max(1, unit * .0023f));
                head = tail;
            }
        }

        foreach (var burst in _bursts)
        {
            if (time < burst.Start || time > burst.End)
                continue;
            foreach (ref readonly var flash in burst.Flashes.AsSpan())
            {
                float age = (float)(time - flash.Start);
                if (age < 0 || age >= flash.Life)
                    continue;
                float p = age / flash.Life;
                float opacity = Math.Clamp(age / .02f, 0, 1) * (1 - p) * (1 - p) * layerAlpha;
                Sprite(ref painter, 18, flash.Position, flash.Size * (.55f + .6f * MathF.Sqrt(p)), flash.Color, opacity * .55f, 0);
                Sprite(ref painter, 18, flash.Position, flash.Size * .35f * (1 - p * .6f), Vector3.One, opacity, 0);
            }

            foreach (ref readonly var star in burst.Stars.AsSpan())
            {
                if (painter.Count - first >= budget)
                    return;
                float age = (float)(time - star.Start);
                if (age <= 0 || age > star.Life || star.Rank >= keep)
                    continue;
                float u = age / star.Life;
                float opacity = star.Depth * Math.Min(1, age / .035f) * layerAlpha;
                if (u > 1 - star.FadeTail)
                    opacity *= FoliaPainter.Ease((1 - u) / star.FadeTail);
                if (opacity <= .002f)
                    continue;
                Vector2 head = Point(star, age), previous = head;
                bool glitter = star.Glitter > 0 && Noise(MathF.Floor(age * 30), star.Phase * 53) < star.Glitter * .35f;
                float size = star.Size * (1 - .45f * u) * (glitter ? 1.6f : 1);
                for (int j = 1; j <= star.Segments; j++)
                {
                    float back = Math.Max(0, age - star.Trail * j / star.Segments);
                    Vector2 old = Point(star, back);
                    float fade = MathF.Pow(1 - (j - 1f) / star.Segments, 1.3f);
                    float thickness = Math.Max(1.2f, size * .75f * (1 - .45f * j / star.Segments));
                    if (Vector2.DistanceSquared(old, previous) > .36f)
                        painter.Line(old.X, old.Y, previous.X, previous.Y, star.TrailColor * 1.3f, opacity * fade * .95f, thickness);
                    previous = old;
                    if (back <= 0)
                        break;
                }
                if (star.Strobe > 0 && age > star.StrobeFrom && (age * star.Strobe + star.Phase) % 1 >= .42f)
                    opacity *= .05f;
                if (star.Twinkle > 0 && u > .5f && Noise(MathF.Floor(age * 24), star.Phase * 97) < .45f)
                    opacity *= 1 - star.Twinkle * Math.Clamp((u - .5f) / .2f, 0, 1) * .85f;
                var color = Vector3.Lerp(star.Color, star.EndColor, Math.Clamp((u - star.Shift) / .1f, 0, 1));
                if (glitter)
                    color = Vector3.Lerp(color, Vector3.One, .75f);
                Sprite(ref painter, star.Texture == 1 ? 9 : 18, head, size * 3.4f, color, opacity, star.Phase);
            }
        }
    }

    private static Vector2 Head(in Launch launch, float p)
    {
        float t = 1 - (1 - p) * (1 - p);
        float swing = MathF.Sin(p * launch.Frequency * MathF.Tau + launch.Phase) * Vector2.Distance(launch.Origin, launch.Target) * .018f * (1 - p * .7f);
        return Vector2.Lerp(launch.Origin, launch.Target, t) + new Vector2(swing, 0);
    }

    private static void Sprite(ref FoliaPainter painter, int shape, Vector2 at, float size, Vector3 color, float opacity, float angle) =>
        painter.Shape(shape, at.X - size * .5f, at.Y - size * .5f, size, size, color * 1.37f, opacity, angle);
}
