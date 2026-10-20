using System;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Draws original segment-fixed geometry and the four deterministic open-frame variants.</summary>
internal sealed class FoliaSonnetTextDecor
{
    private readonly FoliaSonnetGraphics _shape;
    private readonly int _variant;
    private readonly Vector2 _position, _enter, _dimensions;

    internal FoliaSonnetTextDecor(JsonElement source)
    {
        _shape = new(source.GetProperty("commands"));
        _variant = source.GetProperty("variant").GetInt32();
        _position = new(F(source, "x"), F(source, "y"));
        _enter = new(F(source, "enterX"), F(source, "enterY"));
        _dimensions = new(F(source, "width"), F(source, "height"));
    }
    private static float F(JsonElement value, string name) => value.GetProperty(name).GetSingle();

    internal void Write(ref FoliaPainter painter, double time, double start, double settle, double shotStart, double shotEnd,
        float font, float rotation, in Matrix3x2 camera, Vector3 primary, Vector3 secondary, Vector3 accent)
    {
        float entry = Math.Clamp((float)((time - start) / Math.Max(.08, settle - start)), 0, 1);
        if (entry <= 0) return;
        float reveal = entry >= 1 ? 1 : 1 - MathF.Pow(2, -10 * entry);
        var shapeMatrix = Matrix3x2.CreateScale(.86f + reveal * .14f) * Matrix3x2.CreateRotation(rotation)
            * Matrix3x2.CreateTranslation(_position + _enter * (1 - reveal)) * camera;
        _shape.WriteFixed(ref painter, shapeMatrix, .16f + reveal * .84f, primary, secondary, accent);
        if (_variant < 0) return;
        float shotDuration = (float)Math.Max(.001, shotEnd - shotStart);
        float duration = Math.Min(Math.Clamp(shotDuration * .42f, .65f, 1.8f), shotDuration * .72f) * 1.25f;
        float phase = Math.Clamp((float)(time - start) / duration, 0, 1);
        float eased = phase >= 1 ? 1 : 1 - MathF.Pow(2, -10 * phase);
        if (eased <= 0) return;
        var matrix = Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(_position) * camera;
        float pad = Math.Clamp(font * .22f, 8, 20), stroke = Math.Clamp(font * .03f, 1.2f, 2.2f);
        Vector2 half = _dimensions * .5f + new Vector2(pad);
        float gap = Math.Clamp(Math.Min(half.X, half.Y) * (_variant == 1 ? .42f : .3f), 6, 30);
        Span<Vector2> corners = stackalloc Vector2[4] { new(-half.X, -half.Y), new(half.X, -half.Y), new(half.X, half.Y), new(-half.X, half.Y) };
        Vector3 color = primary * .55f;
        for (int side = 0; side < 4; side++)
        {
            Vector2 a = corners[side], b = corners[(side + 1) % 4];
            Vector2 unit = Vector2.Normalize(b - a);
            a += unit * gap;
            b -= unit * gap;
            float progress = Math.Clamp((eased - side * .2f) / .4f, 0, 1);
            Vector2 tip = Vector2.Lerp(a, b, progress);
            if (_variant == 3)
            {
                float dash = Math.Clamp(font * .14f, 5, 8), length = Vector2.Distance(a, tip);
                for (float d = 0; d + dash <= length + .001f; d += dash * 1.7f)
                    Line(ref painter, matrix, a + unit * d, a + unit * (d + dash), color, .85f, stroke);
            }
            else if (progress > 0)
                Line(ref painter, matrix, a, tip, color, .85f, stroke);
            Vector2 corner = corners[side], direction = new(MathF.Sign(corner.X), MathF.Sign(corner.Y));
            float t = Math.Clamp((eased - .35f - side * .14f) / .3f, 0, 1);
            float ornament = MathF.Pow(2, -10 * t) * MathF.Sin((t - .075f) * MathF.Tau / .3f) + 1;
            if (ornament <= .02f) continue;
            if (_variant == 1)
            {
                float radius = Math.Clamp(pad * .55f, 4, 9);
                for (int petal = 0; petal < 4; petal++)
                {
                    float angle = petal * MathF.PI * .5f;
                    Circle(ref painter, matrix, corner + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius, radius * .44f * ornament, color, .8f);
                }
                Circle(ref painter, matrix, corner, radius * .32f * Math.Min(1, ornament), color, .9f);
            }
            else if (_variant == 3)
            {
                float size = Math.Clamp(pad * .6f, 4, 9);
                Vector2 diagonal = direction / MathF.Sqrt(2), perpendicular = new(direction.Y / MathF.Sqrt(2), -direction.X / MathF.Sqrt(2));
                Vector2 origin = corner + diagonal * 2;
                painter.Triangle(Vector2.Transform(corner + diagonal * (2 + size * ornament), matrix),
                    Vector2.Transform(origin + perpendicular * size * .7f * ornament, matrix), Vector2.Transform(origin - perpendicular * size * .7f * ornament, matrix), color, .85f);
                Circle(ref painter, matrix, corner, stroke, color, .9f);
            }
            else
            {
                float arm = _variant == 0 ? Math.Clamp(pad * .8f, 5, 12) : Math.Clamp(pad, 6, 14);
                Vector2 inner = corner + direction * (_variant == 0 ? 3 : 2.5f);
                Line(ref painter, matrix, inner + new Vector2(direction.X * arm * ornament, 0), inner, color, .9f * Math.Min(1, ornament), stroke * (_variant == 2 ? 1.4f : 1));
                Line(ref painter, matrix, inner, inner + new Vector2(0, direction.Y * arm * ornament), color, .9f * Math.Min(1, ornament), stroke * (_variant == 2 ? 1.4f : 1));
                if (_variant == 2)
                {
                    Vector2 inset = inner + direction * 4;
                    Line(ref painter, matrix, inset + new Vector2(direction.X * arm * ornament, 0), inset, color, .5f, stroke * .8f);
                    Line(ref painter, matrix, inset, inset + new Vector2(0, direction.Y * arm * ornament), color, .5f, stroke * .8f);
                    Vector2 mid = (a + b) * .5f;
                    float diamond = Math.Clamp(pad * .45f, 3.5f, 7) * ornament;
                    Vector2 top = Vector2.Transform(mid + new Vector2(0, -diamond), matrix), right = Vector2.Transform(mid + new Vector2(diamond, 0), matrix);
                    Vector2 bottom = Vector2.Transform(mid + new Vector2(0, diamond), matrix), left = Vector2.Transform(mid + new Vector2(-diamond, 0), matrix);
                    painter.Triangle(top, right, bottom, color, .85f);
                    painter.Triangle(top, bottom, left, color, .85f);
                }
            }
        }
    }
    private static void Line(ref FoliaPainter painter, in Matrix3x2 matrix, Vector2 a, Vector2 b, Vector3 color, float alpha, float stroke)
    {
        a = Vector2.Transform(a, matrix);
        b = Vector2.Transform(b, matrix);
        painter.Line(a.X, a.Y, b.X, b.Y, color, alpha, stroke * MathF.Sqrt(MathF.Abs(matrix.GetDeterminant())));
    }
    private static void Circle(ref FoliaPainter painter, in Matrix3x2 matrix, Vector2 center, float radius, Vector3 color, float alpha)
    {
        center = Vector2.Transform(center, matrix);
        radius *= MathF.Sqrt(MathF.Abs(matrix.GetDeterminant()));
        painter.Shape(16, center.X - radius, center.Y - radius, radius * 2, radius * 2, color, alpha);
    }
}
