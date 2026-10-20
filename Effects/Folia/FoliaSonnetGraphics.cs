using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Replays immutable upstream MG paths, with original stagger windows and independent layer transforms.</summary>
internal sealed class FoliaSonnetGraphics
{
    private readonly record struct Segment(Vector2 A, Vector2 B, float Start, float End);
    private readonly record struct Triangle(Vector2 A, Vector2 B, Vector2 C);
    private sealed record Command(int Layer, int Color, float Alpha, float Width, float Delay, float Span,
        Vector2 Center, Vector3 Icon, bool Rectangle, Vector2[] Corners, Segment[] Segments, Triangle[] Triangles);
    private readonly Command[] _commands;

    internal FoliaSonnetGraphics(JsonElement source)
    {
        var commands = new List<Command>();
        foreach (var item in source.EnumerateArray())
        {
            var segments = new List<Segment>();
            var triangles = new List<Triangle>();
            Vector2[] corners = [];
            bool rectangle = false;
            if (item.TryGetProperty("lines", out var lines))
                foreach (var line in lines.EnumerateArray())
                    segments.Add(new(new(line[0].GetSingle(), line[1].GetSingle()), new(line[2].GetSingle(), line[3].GetSingle()), line[4].GetSingle(), line[5].GetSingle()));
            if (item.TryGetProperty("points", out var points))
            {
                corners = new Vector2[points.GetArrayLength() / 2];
                for (int i = 0; i < corners.Length; i++)
                    corners[i] = new(points[i * 2].GetSingle(), points[i * 2 + 1].GetSingle());
                var indices = item.GetProperty("indices");
                for (int i = 0; i < indices.GetArrayLength(); i += 3)
                    triangles.Add(new(corners[indices[i].GetInt32()], corners[indices[i + 1].GetInt32()], corners[indices[i + 2].GetInt32()]));
                rectangle = item.GetProperty("rectangle").GetBoolean();
            }
            var center = item.GetProperty("center");
            var icon = item.GetProperty("icon");
            commands.Add(new(item.GetProperty("layer").GetInt32(), item.GetProperty("color").GetInt32(), F(item, "alpha"), F(item, "width"), F(item, "delay"), F(item, "span"),
                new(center[0].GetSingle(), center[1].GetSingle()), icon.ValueKind == JsonValueKind.Null ? new(-1, 0, 0) : new(icon[0].GetSingle(), icon[1].GetSingle(), icon[2].GetSingle()),
                rectangle, corners, segments.ToArray(), triangles.ToArray()));
        }
        _commands = commands.ToArray();
    }

    private static float F(JsonElement value, string name) => value.GetProperty(name).GetSingle();

    internal void WriteFixed(ref FoliaPainter painter, in Matrix3x2 matrix, float alpha, Vector3 primary, Vector3 secondary, Vector3 accent)
    {
        float scale = MathF.Sqrt(MathF.Abs(matrix.GetDeterminant()));
        foreach (var command in _commands)
        {
            Vector3 color = command.Color == 1 ? primary : command.Color == 2 ? secondary : command.Color == 3 ? accent : primary;
            foreach (ref readonly var segment in command.Segments.AsSpan())
            {
                Vector2 a = Vector2.Transform(segment.A, matrix), b = Vector2.Transform(segment.B, matrix);
                painter.Line(a.X, a.Y, b.X, b.Y, color, command.Alpha * alpha, Math.Max(.35f, command.Width * scale));
            }
            foreach (ref readonly var triangle in command.Triangles.AsSpan())
                painter.Triangle(Vector2.Transform(triangle.A, matrix), Vector2.Transform(triangle.B, matrix), Vector2.Transform(triangle.C, matrix), color, command.Alpha * alpha);
        }
    }

    internal void Write(ref FoliaPainter painter, double time, double start, double end, double lastCue,
        Vector2 focus, Vector2 anchor, Vector2 cameraOffset, float cameraScale, float cameraRotation, float frameScale,
        Vector3 primary, Vector3 secondary, in FoliaMotionFrame motion, float iconResponse)
    {
        float duration = Math.Max(.001f, (float)(end - start));
        float drawDuration = Math.Max(1, Math.Min(duration, Math.Max((float)(lastCue - start), duration * .95f)));
        float raw = Math.Clamp((float)(time - start) / drawDuration, 0, 1);
        float lineAlpha = motion.Active ? .78f + motion.Treble * .22f : 1;
        foreach (var command in _commands)
        {
            if (painter.Remaining < 4096)
                break;
            if (command.Layer <= 1 && !AppSettings.FoliaSonnetBackgroundMg
                || command.Layer == 2 && !AppSettings.FoliaSonnetFixedGeometry
                || command.Layer == 3 && !AppSettings.FoliaSonnetDecorations)
                continue;
            float local = command.Span <= 0 ? 1 : Math.Clamp((raw - command.Delay) / command.Span, 0, 1);
            if (local <= 0)
                continue;
            float progress = 1 - MathF.Pow(1 - local, 3);
            float alpha = command.Alpha * (command.Triangles.Length > 0 ? 1 - MathF.Pow(1 - Math.Min(1, local * 2), 3) : 1);
            float rotation = cameraRotation, scale = cameraScale;
            Vector2 position = anchor;
            Matrix3x2 decor = Matrix3x2.Identity;
            Vector2 center = command.Center;
            float iconScale = 1;
            if (command.Layer == 2)
            {
                // Fixed geometry stays upright; its origin still follows the shot camera.
                rotation = 0;
                position = anchor - Rotate(focus * cameraScale, cameraRotation);
            }
            else if (command.Layer == 3)
            {
                float swim = (float)(time - start) * .05f;
                float depthScale = 1 + (frameScale - 1) * .3f;
                scale *= depthScale;
                decor = Matrix3x2.CreateScale(depthScale) * Matrix3x2.CreateRotation(swim)
                    * Matrix3x2.CreateTranslation(cameraOffset * .4f);
                if (command.Icon.X >= 0)
                {
                    float entryPhase = command.Icon.Y <= 1 ? .12f : .04f + command.Icon.X / (command.Icon.Y - 1) * .82f;
                    float entryDuration = Math.Min(command.Icon.Z, Math.Min(duration, Math.Max(.08f, duration * .18f)));
                    float delay = entryPhase * Math.Max(0, duration - entryDuration);
                    float entry = 1 - MathF.Pow(1 - Math.Clamp(((float)(time - start) - delay) / Math.Max(.001f, entryDuration), 0, 1), 3);
                    float pulse = (MathF.Sin((float)(time - start) * MathF.PI * .7f + command.Icon.X * .2f) + 1) * .5f;
                    iconScale = (.72f + entry * .28f) * (1 + iconResponse * (motion.Active ? .4f : .42f)) * (1 + pulse * .025f);
                    float lift = motion.Active ? .86f + iconResponse * .24f : .72f + iconResponse * .38f;
                    alpha *= .85f * entry * (lift + pulse * .03f);
                }
            }
            else
                alpha *= lineAlpha;
            var matrix = Matrix3x2.CreateScale(command.Layer == 3 ? cameraScale : scale) * Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(position);
            if (command.Layer != 2)
                matrix = Matrix3x2.CreateTranslation(-focus) * matrix;
            if (command.Layer == 3)
                matrix = decor * matrix;
            if (iconScale != 1)
                matrix = Matrix3x2.CreateTranslation(-center) * Matrix3x2.CreateScale(iconScale) * Matrix3x2.CreateTranslation(center) * matrix;
            Vector3 color = command.Color switch { 1 => primary, 2 => secondary,
                _ => new((command.Color >> 16 & 255) / 255f, (command.Color >> 8 & 255) / 255f, (command.Color & 255) / 255f) };
            float stroke = Math.Max(.35f, command.Width * scale * iconScale);
            foreach (ref readonly var segment in command.Segments.AsSpan())
            {
                if (progress <= segment.Start)
                    continue;
                float reveal = Math.Clamp((progress - segment.Start) / Math.Max(.000001f, segment.End - segment.Start), 0, 1);
                Vector2 a = Vector2.Transform(segment.A, matrix), b = Vector2.Transform(Vector2.Lerp(segment.A, segment.B, reveal), matrix);
                painter.Line(a.X, a.Y, b.X, b.Y, color, alpha, stroke);
            }
            if (command.Rectangle && command.Corners.Length >= 4)
            {
                Vector2 a = Vector2.Transform(command.Corners[0], matrix), d = Vector2.Transform(command.Corners[3], matrix);
                Vector2 b = Vector2.Transform(Vector2.Lerp(command.Corners[0], command.Corners[1], progress), matrix);
                Vector2 c = Vector2.Transform(Vector2.Lerp(command.Corners[3], command.Corners[2], progress), matrix);
                painter.Triangle(a, b, c, color, alpha);
                painter.Triangle(a, c, d, color, alpha);
            }
            else
                foreach (ref readonly var triangle in command.Triangles.AsSpan())
                    painter.Triangle(Vector2.Transform(triangle.A, matrix), Vector2.Transform(triangle.B, matrix), Vector2.Transform(triangle.C, matrix), color, alpha);
        }
    }

    private static Vector2 Rotate(Vector2 point, float angle)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new(point.X * c - point.Y * s, point.X * s + point.Y * c);
    }
}
