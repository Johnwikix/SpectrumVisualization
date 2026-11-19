using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Prepares upstream Metaphor cues, geometry and hard-cut camera tracks on the scene worker.</summary>
internal sealed class FoliaMetaphor
{
    private readonly Cue[] _cues;
    private readonly FoliaShot[] _cuesByTime;
    private readonly Vector3 _accent;
    private readonly InkPalette[] _palettes;
    internal readonly record struct InkPalette(Vector3 Paper, Vector3 Ink, Vector3 Tone, Vector3 Accent);
    private readonly record struct InkGlyph(FoliaGlyph Glyph, Vector2 Position, float Size, float Roll, double Start, float Seed, Vector3 Tint, bool Tinted, bool Note);
    private readonly record struct InkShape(int Kind, Vector2 Position, Vector2 Size, float Roll, float Parameter,
        double Born, Vector3 Mix, Vector2 Drift, Vector2 Frequency, Vector2 Phase, int Parent, float Orbit, float Speed, bool Steady, float Pulse);
    private readonly record struct Fragment(float Direction, float Curl, float Range, float Size, float Life);
    private sealed record Collision(double Time, Vector2 Center, float Angle, float Unit, Fragment[] Fragments);
    private readonly record struct Pose(double Time, Vector2 Position, float Distance, float Roll);
    private sealed record Shot(Pose[] Poses, float Angle, float Seed, bool Invert, int Index);
    private sealed record Cue(FoliaShot[] Timeline, Shot[] Shots, InkGlyph[] Glyphs, InkShape[] Shapes, Collision[] Collisions, double SingEnd,
        double Loop, bool Interlude, bool Flood);

    internal FoliaMetaphor(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        _accent = request.Song.Palette[3];
        using var compiled = FoliaSourcePlanner.CompileNativeStage(atlas, request, "metaphor", token);
        var cues = compiled.RootElement.GetProperty("cues");
        var palettes = compiled.RootElement.GetProperty("palettes");
        _palettes = new InkPalette[palettes.GetArrayLength()];
        for (int i = 0; i < _palettes.Length; i++)
        {
            var p = palettes[i];
            _palettes[i] = new(Rgb(p.GetProperty("paper")), Rgb(p.GetProperty("ink")),
                Rgb(p.GetProperty("tone")), Rgb(p.GetProperty("accent")));
        }
        _cues = new Cue[cues.GetArrayLength()];
        _cuesByTime = new FoliaShot[_cues.Length];
        int shotIndex = 0;
        for (int i = 0; i < _cues.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var cue = cues[i];
            var shots = cue.GetProperty("shots");
            var shapes = cue.GetProperty("shapes");
            double start = D(cue, "start");
            double end = cue.GetProperty("end").ValueKind == JsonValueKind.Number ? D(cue, "end") : double.PositiveInfinity;
            var stageShots = new Shot[shots.GetArrayLength()];
            var stageTimeline = new FoliaShot[stageShots.Length];
            int firstShot = shotIndex;
            for (int j = 0; j < shots.GetArrayLength(); j++)
            {
                var shot = shots[j];
                var source = shot.GetProperty("poses");
                var poses = new Pose[source.GetArrayLength()];
                for (int k = 0; k < poses.Length; k++)
                {
                    var pose = source[k];
                    poses[k] = new(pose[0].GetDouble(), new(pose[1].GetSingle(), pose[2].GetSingle()), pose[3].GetSingle(), pose[4].GetSingle());
                }
                stageShots[j] = new(poses, F(shot, "screenAngle"), F(shot, "seed"), shot.GetProperty("invert").GetBoolean(), shotIndex++);
                stageTimeline[j] = new(D(shot, "start"), D(shot, "end"), 0, 0, 0);
            }

            var stageShapes = new InkShape[shapes.GetArrayLength()];
            for (int j = 0; j < stageShapes.Length; j++)
            {
                var s = shapes[j];
                stageShapes[j] = new(s.GetProperty("shape").GetInt32(), new(F(s, "x"), F(s, "y")), new(F(s, "sx"), F(s, "sy")),
                    F(s, "rot"), F(s, "param"), D(s, "born"), new(F(s, "ink"), F(s, "tone"), F(s, "accent")),
                    new(F(s, "ampX"), F(s, "ampY")), new(F(s, "w1"), F(s, "w2")), new(F(s, "p1"), F(s, "p2")),
                    s.GetProperty("parent").GetInt32(), F(s, "orbit"), F(s, "orbitSpeed"), s.GetProperty("steady").GetBoolean(), F(s, "pulse"));
            }

            var glyphs = new List<InkGlyph>();
            ReadGlyphs(cue.GetProperty("glyphs"), atlas, start, false, glyphs);
            ReadGlyphs(cue.GetProperty("notes"), atlas, start, true, glyphs);
            bool interlude = cue.GetProperty("lineIndex").GetInt32() < 0;
            _cuesByTime[i] = new(start, end, 0, 0, 0);
            _cues[i] = new(stageTimeline, stageShots, glyphs.ToArray(), stageShapes, ReadCollisions(cue.GetProperty("collisions")), D(cue, "singEnd"), D(cue, "loop"),
                interlude, cue.GetProperty("transition").GetString() == "flood");
        }
    }

    internal InkPalette Palette(bool invert, bool forceDark) =>
        _palettes[forceDark ? (Luminance(_palettes[0].Paper) < .5f ? 0 : 1) : (invert ? 1 : 0)];

    private static float Luminance(Vector3 value) => Vector3.Dot(value, new(.2126f, .7152f, .0722f));
    private static Vector3 Rgb(JsonElement array) => new(array[0].GetSingle(), array[1].GetSingle(), array[2].GetSingle());

    private static float F(JsonElement obj, string field) => obj.GetProperty(field).GetSingle();
    private static double D(JsonElement obj, string field) => obj.GetProperty(field).GetDouble();

    private void ReadGlyphs(JsonElement array, FoliaTextAtlas atlas, double cueStart, bool note, List<InkGlyph> output)
    {
        foreach (var g in array.EnumerateArray())
        {
            if (!atlas.CharacterGlyphs.TryGetValue(g.GetProperty("char").GetString()!, out var glyph))
                continue;
            Vector3 color = Vector3.Zero;
            bool tinted = false;
            if (g.TryGetProperty("tint", out var tint) && tint.ValueKind == JsonValueKind.String)
            {
                var hex = tint.GetString()!.AsSpan(1);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
                {
                    color = new((rgb >> 16) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f);
                    tinted = true;
                }
            }
            else if (g.TryGetProperty("accent", out var accent) && accent.GetBoolean())
            {
                color = _accent;
                tinted = true;
            }
            output.Add(new(glyph, new(F(g, "x"), F(g, "y")), F(g, "size"), F(g, "rot"),
                note ? cueStart + D(g, "born") : D(g, "t0"), note ? 0 : F(g, "seed"), color, tinted, note));
        }
    }

    private static Collision[] ReadCollisions(JsonElement input)
    {
        var collisions = new Collision[input.GetArrayLength()];
        for (int i = 0; i < collisions.Length; i++)
        {
            var c = input[i];
            var inner = c.GetProperty("fragments");
            var fragments = new Fragment[inner.GetArrayLength()];
            for (int j = 0; j < fragments.Length; j++)
            {
                var f = inner[j];
                fragments[j] = new(F(f, "dir"), F(f, "curl"), F(f, "range"), F(f, "size"), F(f, "life"));
            }
            collisions[i] = new(D(c, "time"), new(F(c, "x"), F(c, "y")), F(c, "angle"), F(c, "unit"), fragments);
        }
        return collisions;
    }

    internal int ShotAt(double position)
    {
        int index = FoliaShotTimeline.Find(_cuesByTime, position);
        if (index < 0)
            return -1;
        var cue = _cues[index];
        double start = _cuesByTime[index].Start;
        double time = cue.Loop > 0 ? start + (position - start) % cue.Loop : position;
        int shot = FoliaShotTimeline.Find(cue.Timeline, time);
        return shot < 0 ? -1 : cue.Shots[shot].Index;
    }

    internal int Write(ref FoliaPainter painter, double position, float scale, in FoliaMotionFrame motion, out Vector4 development)
    {
        development = default;
        int index = FoliaShotTimeline.Find(_cuesByTime, position);
        if (index < 0)
            return -1;
        var cue = _cues[index];
        var interval = _cuesByTime[index];
        double time = cue.Loop > 0 ? interval.Start + (position - interval.Start) % cue.Loop : position;
        int j = FoliaShotTimeline.Find(cue.Timeline, time);
        if (j < 0)
            return -1;
        var shot = cue.Shots[j];
        var pose = PoseAt(shot.Poses, time);
        float pixelsPerUnit = painter.Height / Math.Max(.001f, 2 * pose.Distance * .2679492f) * Math.Clamp(scale, .25f, 4);
        var center = new Vector2(painter.Width, painter.Height) * .5f;
        var (sin, cos) = MathF.SinCos(pose.Roll);
        Matrix3x2 projection = new(cos * pixelsPerUnit, sin * pixelsPerUnit, sin * pixelsPerUnit, -cos * pixelsPerUnit, center.X, center.Y);

        float flood = 0;
        double since = time - interval.Start;
        if (cue.Flood && since is >= 0 and < .9)
            flood = 1.15f * MathF.Pow(1 - (float)(since / .9), 3);
        if (index + 1 < _cues.Length && _cues[index + 1].Flood)
        {
            double until = _cuesByTime[index + 1].Start - time;
            if (until < .7)
                flood = Math.Max(flood, 1.15f * MathF.Pow(1 - (float)(until / .7), 2));
        }
        development = new(shot.Angle, flood, shot.Seed, shot.Invert ? 1 : 0);
        float melt = 0;
        if (!cue.Interlude && interval.End - cue.SingEnd >= 1.2)
        {
            double from = Math.Max(cue.SingEnd + .3, interval.End - 1.5);
            melt = Math.Clamp((float)((time - from) / Math.Max(.05, interval.End - from)), 0, 1);
        }
        float reveal = Math.Clamp((float)(since / .5) + (cue.Interlude ? 1 : 0), 0, 1);
        foreach (ref readonly var glyph in cue.Glyphs.AsSpan())
        {
            if (glyph.Note && time < glyph.Start)
                continue;
            float age = (float)(time - glyph.Start);
            float glyphScale = (age < 0 ? .94f : 1 + .18f * MathF.Exp(-age * 2.2f)) * (1 + melt * .25f);
            var (awayY, awayX) = MathF.SinCos(glyph.Seed * MathF.Tau);
            float distance = melt * melt * (.4f + glyph.Seed * 7.31f % 1 * .9f) * glyph.Size;
            Vector2 p = Vector2.Transform(glyph.Position + new Vector2(awayX, awayY) * distance - pose.Position, projection);
            painter.InkGlyph(glyph.Glyph, p.X, p.Y, glyph.Size * pixelsPerUnit * glyphScale, glyph.Tint,
                pose.Roll - glyph.Roll, age, reveal, melt, glyph.Tinted, glyph.Note);
            if (age is >= 0 and < 3.6f && !glyph.Note)
            {
                float unit = glyph.Size;
                float travel = unit * (.4f + 1.5f * (1 - MathF.Exp(-age * .7f)));
                float radius = unit * (.07f + glyph.Seed * .05f) * (1 - age * age / (3.6f * 3.6f));
                float curl = .9f + glyph.Seed * 1.4f, theta = glyph.Seed * MathF.Tau;
                var weights = glyph.Tinted ? new Vector3(0, .3f, 1) : new Vector3(1, .3f, 0);
                for (int side = -1; side <= 1; side += 2)
                {
                    var (dy, dx) = MathF.SinCos(theta + (side < 0 ? MathF.PI : 0) + side * curl * travel / unit);
                    WriteShape(ref painter, projection, pose, pixelsPerUnit, 0, glyph.Position + travel * new Vector2(dx, dy),
                        new(radius), 0, 0, weights);
                }
            }
        }

        // Parent indices refer only to earlier shapes in this immutable cue.
        Span<Vector2> centers = stackalloc Vector2[cue.Shapes.Length];
        for (int i = 0; i < cue.Shapes.Length; i++)
        {
            ref readonly var shape = ref cue.Shapes[i];
            Vector2 at = shape.Position + shape.Drift * new Vector2(MathF.Sin((float)time * shape.Frequency.X + shape.Phase.X),
                MathF.Cos((float)time * shape.Frequency.Y + shape.Phase.Y));
            if (shape.Orbit > 0)
            {
                float radius = shape.Steady ? shape.Orbit : shape.Orbit * (.8f + .35f * MathF.Sin((float)time * .3f + shape.Phase.Y));
                var (orbitSin, orbitCos) = MathF.SinCos((float)time * shape.Speed + shape.Phase.X);
                at = (shape.Parent >= 0 ? centers[shape.Parent] : at) + radius * new Vector2(orbitCos, orbitSin);
            }
            centers[i] = at;
            if (since < shape.Born)
                continue;
            float swell = Math.Max(.01f, 1 + shape.Pulse * (.06f * MathF.Sin((float)time * .4f + shape.Phase.Y) + .14f * motion.Low));
            var world = Vector2.Abs(shape.Size) * swell;
            if (world.X <= 0 || world.Y <= 0)
                continue;
            WriteShape(ref painter, projection, pose, pixelsPerUnit, shape.Kind, at, world, shape.Roll, shape.Parameter, shape.Mix);
        }
        WriteCollisions(ref painter, cue.Collisions, projection, pose, pixelsPerUnit, time);
        return shot.Index;
    }

    private static void WriteShape(ref FoliaPainter painter, in Matrix3x2 projection, in Pose pose, float pixelsPerUnit,
        int kind, Vector2 at, Vector2 world, float roll, float parameter, Vector3 mix)
    {
        Vector2 raster = (kind == 3 ? new Vector2(world.X) : world) + new Vector2(.2f);
        Vector2 size = raster * pixelsPerUnit;
        Vector2 point = Vector2.Transform(at - pose.Position, projection);
        float bound = size.Length();
        if (point.X + bound < 0 || point.X - bound > painter.Width || point.Y + bound < 0 || point.Y - bound > painter.Height)
            return;
        painter.InkShape(27 + kind, point, size, mix, pose.Roll - roll, parameter,
            new(world.X, world.Y, raster.X, raster.Y), at, roll);
    }

    private static void WriteCollisions(ref FoliaPainter painter, ReadOnlySpan<Collision> collisions,
        in Matrix3x2 projection, in Pose pose, float pixelsPerUnit, double time)
    {
        foreach (var hit in collisions)
        {
            float since = (float)(time - hit.Time);
            if (since is < -2.6f or >= 6.5f)
                continue;
            if (since < 0)
            {
                var (y, x) = MathF.SinCos(hit.Angle);
                for (int dot = 0; dot <= 3; dot++)
                {
                    float u = 1 + since / 2.6f - dot * .035f;
                    if (u < 0)
                        continue;
                    Vector2 direction = new Vector2(x, y) * (hit.Unit * 5 * (1 - u * u * u));
                    float radius = hit.Unit * (dot == 0 ? .22f : .13f - dot * .025f);
                    WriteShape(ref painter, projection, pose, pixelsPerUnit, 0, hit.Center + direction, new(radius), 0, 0, new(1, .3f, 0));
                    WriteShape(ref painter, projection, pose, pixelsPerUnit, 0, hit.Center - direction, new(radius), 0, 0, new(1, .3f, 0));
                }
                continue;
            }

            float burst = hit.Unit * .95f * MathF.Exp(-since * 1.6f);
            if (burst > hit.Unit * .04f)
                WriteShape(ref painter, projection, pose, pixelsPerUnit, 0, hit.Center, new(burst), 0, 0, new(1, .8f, 0));
            if (since < 4)
            {
                float ring = hit.Unit * (.35f + 2.6f * (1 - MathF.Exp(-since * .55f)));
                WriteShape(ref painter, projection, pose, pixelsPerUnit, 1, hit.Center, new(ring), 0, 0, new(since < 2.2f ? 1 : 0, 1, 0));
            }
            foreach (ref readonly var fragment in hit.Fragments.AsSpan())
            {
                for (int dot = 0; dot <= 5; dot++)
                {
                    float age = since - dot * .3f;
                    if (age < 0 || age >= fragment.Life)
                        continue;
                    float travel = fragment.Range * (1 - MathF.Exp(-age * .6f));
                    var (y, x) = MathF.SinCos(fragment.Direction + fragment.Curl * travel / hit.Unit);
                    Vector2 at = hit.Center + new Vector2(x, y) * travel;
                    float radius = fragment.Size * (1 - age / fragment.Life) * (dot == 0 ? 1 : .6f - dot * .07f);
                    WriteShape(ref painter, projection, pose, pixelsPerUnit, 0, at, new(radius), 0, 0, new(1, .25f, 0));
                }
            }
        }
    }

    private static Pose PoseAt(ReadOnlySpan<Pose> poses, double time)
    {
        int a = 0, b = poses.Length - 1;
        while (b - a > 1)
        {
            int middle = (a + b) / 2;
            if (poses[middle].Time <= time)
                a = middle;
            else
                b = middle;
        }
        ref readonly var first = ref poses[a];
        ref readonly var last = ref poses[b];
        float t = (float)Math.Clamp((time - first.Time) / Math.Max(1e-8, last.Time - first.Time), 0, 1);
        return new(time, Vector2.Lerp(first.Position, last.Position, t), float.Lerp(first.Distance, last.Distance, t), float.Lerp(first.Roll, last.Roll, t));
    }
}
