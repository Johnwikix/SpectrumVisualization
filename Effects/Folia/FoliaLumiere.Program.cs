using System;
using System.Threading;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaLumiere
{
    private sealed record Shot(Profile Profile, double Start, double End);
    private long _atlasIdentity;
    private float _width, _height, _fontScale;
    private Profile _lead = null!;
    private FoliaLumiereGeometry _window = null!;
    private Vector2[] _sections = [];
    private Vector2 _span;
    private Vector4 _camera;

    internal void Prepare(FoliaTextAtlas atlas, float width, float height, float scale, string family, int weight, CancellationToken token = default)
    {
        if (_atlasIdentity == atlas.Identity && width == _width && height == _height && scale == _fontScale)
            return;
        string output = FoliaSourcePlanner.Compile(FoliaSourcePlanner.CreateInput(atlas, width, height, scale, family, weight), "compileLumiere", token);
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var shots = new List<Shot>();
        foreach (var shot in root.GetProperty("shots").EnumerateArray())
            shots.Add(new(ReadProfile(shot.GetProperty("profile")), shot.GetProperty("startTime").GetDouble(), shot.GetProperty("endTime").GetDouble()));
        var window = new FoliaLumiereGeometry(root.GetProperty("window"), atlas.Lines, width, height);
        var camera = root.GetProperty("camera");
        var sections = new List<Vector2>();
        foreach (var section in camera.GetProperty("sections").EnumerateArray())
            sections.Add(new(F(section, "startTime"), F(section, "endTime")));
        _shots = shots.ToArray();
        _lead = _shots[root.GetProperty("lead").GetInt32()].Profile;
        _window = window;
        _sections = sections.ToArray();
        _span = new(F(camera, "start"), F(camera, "end"));
        _atlasIdentity = atlas.Identity;
        _width = width;
        _height = height;
        _fontScale = scale;
        ReadDecor(root);
        ReadBursts(root);
        GraphicsCount = TextCount = 0;
    }

    private static Profile ReadProfile(JsonElement p)
    {
        var beams = new List<Vector4[]>();
        foreach (var beam in p.GetProperty("beams").EnumerateArray())
        {
            var values = new Vector4[7];
            for (int i = 0; i < values.Length; i++)
                values[i] = V(beam[i]);
            beams.Add(values);
        }
        var paths = new List<Path>();
        foreach (var path in p.GetProperty("paths").EnumerateArray())
        {
            var points = new List<Vector2>();
            foreach (var point in path.GetProperty("points").EnumerateArray())
                points.Add(new(point[0].GetSingle(), point[1].GetSingle()));
            Vector2 dash = default;
            if (path.TryGetProperty("dash", out var d))
                dash = new(d[0].GetSingle(), d[1].GetSingle());
            paths.Add(new(new(F(path, "width"), F(path, "alpha"), F(path, "delay"), F(path, "span")), dash, points.ToArray()));
        }
        var nodes = new List<float[]>();
        foreach (var node in p.GetProperty("nodes").EnumerateArray())
        {
            var at = node.GetProperty("at");
            nodes.Add([at[0].GetSingle(), at[1].GetSingle(), F(node, "size"), F(node, "delay"), F(node, "twinklePhase")]);
        }
        return new(p.GetProperty("kind").GetString()!, p.GetProperty("family").GetString()!, p.GetProperty("mood").GetString()!, p.GetProperty("typography").GetString()!,
            Floats(p.GetProperty("layout")), beams.ToArray(), V(p.GetProperty("fogA")), V(p.GetProperty("fogB")), V(p.GetProperty("glare")), F(p, "glareStreak"),
            V(p.GetProperty("caustic")), V(p.GetProperty("floor")), F(p, "floorStrength"), V(p.GetProperty("wave")), V(p.GetProperty("waveArea")), F(p, "shield"), Floats(p.GetProperty("motes")), paths.ToArray(), nodes.ToArray());
    }
    private static float F(JsonElement value, string key) => value.GetProperty(key).GetSingle();
    private static Vector4 V(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle(), value[3].GetSingle());
    private static float[] Floats(JsonElement value)
    {
        var result = new float[value.GetArrayLength()];
        for (int i = 0; i < result.Length; i++)
            result[i] = value[i].GetSingle();
        return result;
    }

    private Vector4 ResolveCamera(double time)
    {
        float start = _span.X, end = _span.Y;
        int section = 0;
        for (int i = 0; i < _sections.Length; i++)
            if (_sections[i].X <= time)
                section = i;
        if (_sections.Length > 0)
        {
            start = _sections[section].X;
            end = _sections[section].Y;
        }
        float progress = (1 - MathF.Cos(Math.Clamp((float)(time - start) / Math.Max(.001f, end - start), 0, 1) * MathF.PI)) * .5f;
        if (_sections.Length > 0 && (section & 1) != 0)
            progress = 1 - progress;
        float t = (float)time;
        float x = (MathF.Sin(t * .21f + .4f) * .6f + MathF.Sin(t * .53f + 1.9f) * .4f) * .006f;
        float y = (MathF.Cos(t * .17f + 1.1f) * .6f + MathF.Sin(t * .47f + .3f) * .4f) * .006f;
        return new(.5f + _lead.Layout[6] * progress + x, .5f + _lead.Layout[7] * progress + y,
            (1 + _lead.Layout[5] * progress) * (1 + .008f * MathF.Sin(t * .31f + .7f)), .004f * MathF.Sin(t * .13f + 2.1f));
    }
}
