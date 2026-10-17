using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaFume
{
    private readonly record struct BackgroundShape(string Kind, Vector2 Position, float Width, float Rotation,
        float Speed, float Stroke, float Alpha, bool Accent, float Depth, int AudioBand, float Gap, float GapSize);
    private BackgroundShape[] _shapes = [];
    private Vector2 _world;
    private readonly float[] _audio = new float[5];

    internal void SetAudio(ReadOnlySpan<float> features, bool playing)
    {
        _audio[0] = playing ? features[FeatureIndex.Bass] : 0;
        _audio[1] = playing ? features[FeatureIndex.LowMid] : 0;
        _audio[2] = playing ? features[FeatureIndex.Mid] : 0;
        _audio[3] = playing ? (features[FeatureIndex.Mid] + features[FeatureIndex.HighMid] + features[FeatureIndex.Presence]) / 3 : 0;
        _audio[4] = playing ? (features[FeatureIndex.Brilliance] + features[FeatureIndex.Air]) * .5f : 0;
    }
    private static readonly Vector2[] Cross = [new(-.3f,-.5f),new(.3f,-.5f),new(.3f,-.3f),new(.5f,-.3f),new(.5f,.3f),new(.3f,.3f),new(.3f,.5f),new(-.3f,.5f),new(-.3f,.3f),new(-.5f,.3f),new(-.5f,-.3f),new(-.3f,-.3f)];
    private static readonly Vector2[] Spark = [new(0,-.5f),new(.13f,-.13f),new(.5f,0),new(.13f,.13f),new(0,.5f),new(-.13f,.13f),new(-.5f,0),new(-.13f,-.13f)];
    private static readonly Vector2[] Square = [new(-.5f,-.5f),new(.5f,-.5f),new(.5f,.5f),new(-.5f,.5f)];
    private void ReadBackground(JsonElement root)
    {
        _shapes = [];
        if (!root.TryGetProperty("background", out var scene))
            return;
        var shapes = new List<BackgroundShape>();
        _world = new(F(scene, "width"), F(scene, "height"));
        foreach (var shape in scene.GetProperty("shapes").EnumerateArray())
        {
            float gap = shape.TryGetProperty("ringGapStart", out var gs) ? gs.GetSingle() : -.18f * MathF.PI;
            float size = shape.TryGetProperty("ringGapSize", out var gz) ? gz.GetSingle() : .2f * MathF.PI;
            shapes.Add(new(shape.GetProperty("kind").GetString()!, new(F(shape, "x"), F(shape, "y")), F(shape, "width"),
                F(shape, "rotation"), F(shape, "rotationSpeed"), F(shape, "strokeWidth"), F(shape, "opacity"),
                shape.GetProperty("color").GetString() == "accent", F(shape, "depth"), shape.TryGetProperty("audioBand", out var band) ? band.GetString() switch { "bass" => 0, "lowMid" => 1, "mid" => 2, "vocal" => 3, "treble" => 4, _ => -1 } : -1, gap, size));
        }
        _shapes = shapes.ToArray();
    }
    private void WriteBackground(ref FoliaPainter p, Vector3 camera, double time, Vector3 secondary, Vector3 accent)
    {
        var center = _world * .5f;
        var view = new Vector2(center.X + (camera.X - center.X) * .9f,
            center.Y + (camera.Y - center.Y) * .74f - Math.Clamp(p.Height * .22f / Math.Max(camera.Z, .001f), 48, 180));
        float scale = Math.Clamp(camera.Z * .94f, .22f, 2.24f);
        foreach (ref readonly var shape in _shapes.AsSpan())
        {
            var parallax = (view - center) * ((1 - (.58f + .58f * shape.Depth)) * .72f);
            var position = new Vector2(p.Width * .5f, p.Height * .5f) + (shape.Position + parallax - view) * scale;
            float audio = shape.AudioBand >= 0 ? Math.Clamp((_audio[shape.AudioBand] * 255 - 10) / 190, 0, 1) : 0;
            float size = shape.Width * (shape.AudioBand >= 0 ? .95f + .5f * audio : 1) * scale;
            if (position.X + size < 0 || position.Y + size < 0 || position.X - size > p.Width || position.Y - size > p.Height)
                continue;
            float rotation = shape.Rotation + (float)time * shape.Speed, cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
            float alpha = shape.Alpha * (shape.AudioBand >= 0 ? .85f + .7f * audio : 1);
            var points = shape.Kind == "spark" ? Spark : shape.Kind == "cross" ? Cross : Square;
            int segments = shape.Kind == "ring" ? 96 : points.Length;
            for (int segment = 0; segment < segments; segment++)
            {
                Vector2 a, b;
                if (shape.Kind == "ring")
                {
                    float angle = shape.Gap + shape.GapSize, sweep = MathF.Tau - shape.GapSize;
                    float start = angle + sweep * segment / segments, end = angle + sweep * (segment + 1) / segments;
                    a = new Vector2(MathF.Cos(start), MathF.Sin(start)) * .5f;
                    b = new Vector2(MathF.Cos(end), MathF.Sin(end)) * .5f;
                }
                else
                {
                    a = points[segment];
                    b = points[(segment + 1) % points.Length];
                }
                int pieces = shape.Kind == "spark" ? 1 : Math.Max(1, (int)MathF.Ceiling(Vector2.Distance(a, b) * size / 18));
                for (int piece = 0; piece < pieces; piece++)
                {
                    Vector2 from = Vector2.Lerp(a, b, (float)piece / pieces), to = Vector2.Lerp(a, b, (float)(piece + 1) / pieces);
                    var midpoint = (from + to) * .5f;
                    Vector3 color;
                    float opacity;
                    if (shape.Kind == "spark")
                    {
                        color = shape.Accent ? accent : secondary;
                        opacity = alpha;
                    }
                    else
                    {
                        float t = Math.Clamp(.5f + Vector2.Dot(midpoint, new(.55f, .28f)) / (2 * (.55f * .55f + .28f * .28f)), 0, 1);
                        float amount = t < .28f ? t / .28f * .24f : t < .54f ? .24f + (t - .28f) / .26f * .38f : .62f + (t - .54f) / .46f * .38f;
                        float gain = t < .28f ? .18f + t / .28f * .4f : t < .54f ? .58f + (t - .28f) / .26f * .34f : .92f - (t - .54f) / .46f * .22f;
                        color = Vector3.Lerp(secondary, accent, amount);
                        opacity = alpha * gain;
                    }
                    var x = position + new Vector2(from.X * cos - from.Y * sin, from.X * sin + from.Y * cos) * size;
                    var y = position + new Vector2(to.X * cos - to.Y * sin, to.X * sin + to.Y * cos) * size;
                    if (shape.Kind != "spark")
                        p.Line(x.X, x.Y, y.X, y.Y, color, opacity * .56f, Math.Max(shape.Stroke * .28f, .14f) * scale);
                    p.Line(x.X, x.Y, y.X, y.Y, color, opacity, (shape.Kind == "spark" ? shape.Stroke : Math.Max(shape.Stroke * .92f, .78f)) * scale);
                }
            }
        }
    }
}
