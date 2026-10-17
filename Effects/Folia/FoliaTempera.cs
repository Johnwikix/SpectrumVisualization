using System;
using System.Threading;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Original shot, composition and typography plans compiled at song/font/viewport boundaries.</summary>
internal sealed class FoliaTempera
{
    private readonly record struct Primitive(int Kind, Vector3 Color, float Alpha, float Width, Vector2 A, Vector2 B, Vector2 C);
    private sealed record Node(Vector2 Center, float Rotation, float Alpha, Vector2 Enter, float Delay, float Span, bool Drift, bool Grow, float Phase, Primitive[] Primitives);
    private readonly record struct Glyph(FoliaGlyph Shape, Vector2 Position, float Font, float Rotation, double Start, double End, double Settle, double Release,
        Vector2 Enter, float EnterRotation, float EnterScale, string Style, Vector2 Tracking);
    private sealed record Shot(double Start, double End, double LyricEnd, float Flow, Vector4 Camera, Vector4 CameraEnd, Node[] Nodes, Glyph[] Glyphs, float PostSeed);
    internal static readonly Vector3 Ink = new(184 / 255f, 214 / 255f, 204 / 255f), Paper = new(14 / 255f, 17 / 255f, 24 / 255f);
    private long _atlasIdentity;
    private float _width, _height, _scale;
    private Shot[] _shots = [];
    internal float PostSeed { get; private set; }
    internal int GraphicsCount
    {
        get; private set;
    }
    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float scale, string family, int weight, CancellationToken token = default)
    {
        GraphicsCount = 0;
        if (atlas.Identity == _atlasIdentity && width == _width && height == _height && scale == _scale)
            return;
        string input = FoliaSourcePlanner.CreateInput(atlas, width, height, scale, family, weight);
        string output = FoliaSourcePlanner.Compile(input, "compileTempera", token);
        using var document = JsonDocument.Parse(output);
        var shots = new List<Shot>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var nodes = new List<Node>();
            foreach (var n in item.GetProperty("nodes").EnumerateArray())
            {
                var options = n.GetProperty("options");
                var center = n.GetProperty("center");
                var primitives = new List<Primitive>();
                foreach (var v in n.GetProperty("primitives").EnumerateArray())
                {
                    int color = v[1].GetInt32();
                    primitives.Add(new(v[0].GetInt32(), new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f),
                        v[2].GetSingle(), v[3].GetSingle(), new(v[4].GetSingle(), v[5].GetSingle()), new(v[6].GetSingle(), v[7].GetSingle()), new(v[8].GetSingle(), v[9].GetSingle())));
                }
                nodes.Add(new(new(center[0].GetSingle(), center[1].GetSingle()), F(n, "rotation"), F(options, "alpha", 1), new(F(options, "enterDX"), F(options, "enterDY")),
                    F(options, "delay"), F(options, "span", .45f), B(options, "drift"), B(options, "grow"), F(n, "phase"), primitives.ToArray()));
            }
            var glyphs = new List<Glyph>();
            foreach (var g in item.GetProperty("glyphs").EnumerateArray())
            {
                string character = g.GetProperty("char").GetString()!;
                if (!atlas.CharacterGlyphs.TryGetValue(character, out var shape))
                    continue;
                glyphs.Add(new(shape, new(F(g, "x"), F(g, "y")), F(g, "fontSize"), F(g, "rotation"), F(g, "startTime"), F(g, "endTime"), F(g, "settleTime"), F(g, "releaseTime"),
                    new(F(g, "enterX"), F(g, "enterY")), F(g, "enterRotation"), F(g, "enterScale", 1), g.GetProperty("enterStyle").GetString()!, new(F(g, "trackingX"), F(g, "trackingY"))));
            }
            shots.Add(new(F(item, "startTime"), F(item, "endTime"), F(item, "lyricEndTime"), F(item, "flowAngle"), Camera(item.GetProperty("camera")), Camera(item.GetProperty("cameraEnd")), nodes.ToArray(), glyphs.ToArray(), F(item, "postSeed")));
        }
        _shots = shots.ToArray();
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _scale = scale;
    }
    private static float F(JsonElement value, string name, float fallback = 0) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number ? field.GetSingle() : fallback;
    private static bool B(JsonElement value, string name) => value.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.True;
    private static Vector4 Camera(JsonElement value) => new(F(value, "x"), F(value, "y"), F(value, "zoom", 1), F(value, "rotation"));
    internal void Write(ref FoliaPainter p, FoliaTextAtlas atlas, int index, double time, float scale)
    {
        GraphicsCount = 0;
        if (_shots.Length == 0)
            return;
        int current = 0;
        while (current + 1 < _shots.Length && _shots[current + 1].Start <= time)
            current++;
        PostSeed = _shots[current].PostSeed;
        // Both compositions coexist during a push hand-off; retain their individual absolute-time poses.
        for (int i = Math.Max(0, current - 1); i <= current; i++)
            DrawGraphics(ref p, _shots[i], time);
        GraphicsCount = p.Count;
        for (int i = Math.Max(0, current - 1); i <= current; i++)
            DrawGlyphs(ref p, _shots[i], time);
    }
    private readonly record struct Pose(Matrix3x2 Matrix, float Alpha, float Duration, float Carry, Vector2 Flow);
    private static Pose ResolvePose(Shot shot, double time, float width, float height)
    {
        float duration = Math.Max(.001f, (float)(shot.End - shot.Start)), progress = (float)(time - shot.Start) / duration;
        float t = Math.Clamp(progress, 0, 1), eased = t * .5f + (t < .5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) * .5f) * .5f + Math.Max(0, progress - 1) * .35f;
        Vector4 camera = Vector4.Lerp(shot.Camera, shot.CameraEnd, eased);
        float handoff = Math.Clamp(duration * .3f, .4f, 1.1f), enter = FoliaMotion.Enter((float)(time - shot.Start) / (handoff * .8f)), exit = FoliaMotion.InOut((float)(time - shot.End) / handoff);
        float span = Math.Max(width, height), travel = exit * span * .55f - (1 - enter) * span * .32f;
        Vector2 flow = new(MathF.Cos(shot.Flow), MathF.Sin(shot.Flow));
        Vector2 center = new(width * .5f, height * .5f), translation = center + new Vector2(camera.X * width, camera.Y * height) + flow * travel;
        var matrix = Matrix3x2.CreateTranslation(-center) * Matrix3x2.CreateScale(camera.Z * (1 - exit * .08f)) * Matrix3x2.CreateRotation(camera.W) * Matrix3x2.CreateTranslation(translation);
        return new(matrix, 1 - exit, duration, span * .09f, flow);
    }
    private static void DrawGraphics(ref FoliaPainter p, Shot shot, double time)
    {
        var pose = ResolvePose(shot, time, p.Width, p.Height);
        if (pose.Alpha < .001f)
            return;
        float pace = Math.Max(.2f, (float)(shot.LyricEnd - shot.Start));
        float creep = FoliaMotion.InOut((float)(time - shot.Start) / pose.Duration) * pose.Carry * .35f;
        foreach (var node in shot.Nodes)
        {
            if (p.Remaining < 1800)
                break;
            float delay = Math.Clamp(pace * node.Delay, 0, 1.4f), span = Math.Clamp(pace * node.Span, .7f, 2.6f), compression = Math.Min(1, Math.Max(.5f, pace) / (delay + span));
            float enter = FoliaMotion.Enter(((float)(time - shot.Start) - delay * compression) / (span * compression));
            if (enter <= .001f)
                continue;
            float breathe = node.Drift ? 1 + MathF.Sin((float)time * .5f + node.Phase) * .02f : 1;
            float sx = (node.Grow ? Math.Max(.0001f, enter) : 1) * breathe, sy = breathe;
            float drift = node.Drift ? MathF.Sin((float)time * .33f + node.Phase) * .012f : 0;
            Vector2 translation = node.Enter * (1 - enter) - pose.Flow * ((1 - enter) * pose.Carry - creep);
            var matrix = Matrix3x2.CreateTranslation(-node.Center) * Matrix3x2.CreateRotation(-node.Rotation) * Matrix3x2.CreateScale(sx, sy)
                * Matrix3x2.CreateRotation(node.Rotation + drift) * Matrix3x2.CreateTranslation(node.Center + translation) * pose.Matrix;
            foreach (ref readonly var primitive in node.Primitives.AsSpan())
            {
                if (p.Remaining < 1800)
                    break;
                Vector2 a = Vector2.Transform(primitive.A, matrix), b = Vector2.Transform(primitive.B, matrix);
                float alpha = primitive.Alpha * node.Alpha * enter * pose.Alpha;
                if (primitive.Kind == 1)
                    p.Line(a.X, a.Y, b.X, b.Y, primitive.Color, alpha, Math.Max(.4f, primitive.Width));
                else
                    p.Triangle(a, b, Vector2.Transform(primitive.C, matrix), primitive.Color, alpha);
            }
        }
    }
    private static void DrawGlyphs(ref FoliaPainter p, Shot shot, double time)
    {
        var pose = ResolvePose(shot, time, p.Width, p.Height);
        if (pose.Alpha < .001f)
            return;
        float cameraScale = MathF.Sqrt(pose.Matrix.M11 * pose.Matrix.M11 + pose.Matrix.M12 * pose.Matrix.M12), cameraRotation = MathF.Atan2(pose.Matrix.M12, pose.Matrix.M11);
        foreach (ref readonly var g in shot.Glyphs.AsSpan())
        {
            if (time < g.Start)
                continue;
            float window = Math.Max(.08f, (float)(g.Settle - g.Start)), linear = Math.Clamp((float)(time - g.Start) / window, 0, 1), travel = 1 - FoliaMotion.Enter(linear);
            float reveal = Math.Clamp((float)(time - g.Start) / Math.Min(window, 1.35f), 0, 1), alpha = FoliaMotion.InOut(reveal * 2.4f) * pose.Alpha;
            float inverse = linear - 1, softBack = 1 + 2.42f * inverse * inverse * inverse + 1.42f * inverse * inverse;
            float uniform = g.EnterScale + (1 - g.EnterScale) * softBack, rotation = g.EnterRotation * travel;
            Vector2 offset = g.Enter * travel;
            float echo = travel;
            Vector2 direction = g.Style switch
            {
                "from-left" => new(-1, .12f),
                "from-right" => new(1, -.12f),
                "from-above" => new(.12f, -1),
                "from-below" => new(-.12f, 1),
                _ => default
            };
            if (direction != default)
            {
                offset = Vector2.Normalize(direction) * g.Enter.Length() * travel;
                rotation *= .4f;
            }
            else if (g.Style == "swing")
            {
                offset *= .6f;
                rotation = (g.EnterRotation + MathF.Sign(g.EnterRotation == 0 ? 1 : g.EnterRotation) * .95f) * travel;
                echo *= .8f;
            }
            else if (g.Style == "stamp")
            {
                offset = default;
                rotation *= .6f;
                uniform = 1 + travel * .7f;
                echo = 0;
            }
            float attack = Math.Min(.12f, (float)(g.End - g.Start) * .5f);
            float emphasis = g.End <= g.Start ? 0 : FoliaMotion.InOut((float)(time - g.Start) / Math.Max(attack, .02f)) * (1 - FoliaMotion.InOut((float)(time - g.End) / .26f));
            uniform += emphasis * .05f;
            float release = FoliaMotion.InOut((float)((time - Math.Max(g.End, g.Settle)) / Math.Max(.001, g.Release - Math.Max(g.End, g.Settle))));
            Vector2 point = Vector2.Transform(g.Position + offset + g.Tracking * (release * .055f), pose.Matrix);
            float font = g.Font * uniform * cameraScale, angle = g.Rotation + rotation + cameraRotation;
            // Print misregistration shares the inversion pass, so it cannot contaminate its own backdrop.
            p.Glyph(g.Shape, point.X - g.Shape.Advance * font * .5f + g.Font * .06f, point.Y - font * .6f + g.Font * .08f, font, Ink, alpha * .34f, angle, difference: 2);
            p.Glyph(g.Shape, point.X - g.Shape.Advance * font * .5f, point.Y - font * .6f, font, Ink, alpha, angle, difference: 2);
            float echoAlpha = echo * (1 - FoliaMotion.Enter(reveal)) * .5f;
            if (echoAlpha > .004f)
                for (int layer = 0; layer < 2; layer++)
                {
                    Vector2 echoPoint = Vector2.Transform(g.Position + offset * (1 + layer * .85f), pose.Matrix);
                    p.Glyph(g.Shape, echoPoint.X - g.Shape.Advance * font * .5f, echoPoint.Y - font * .6f, font, Ink, echoAlpha / (layer + 1.4f) * pose.Alpha, angle);
                }
        }
    }
}
