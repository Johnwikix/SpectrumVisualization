using System;
using System.Numerics;
using System.Text.Json;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaSonnet
{
    /// <summary>Preserves the original absolute-time paragraph handoff contract.</summary>
    private sealed record Transition(string Kind, double Start, double End);

    private static Transition? ReadTransition(JsonElement item) => item.ValueKind == JsonValueKind.Null ? null
        : new(item.GetProperty("kind").GetString()!, item.GetProperty("startTime").GetDouble(), item.GetProperty("endTime").GetDouble());

    private Vector4 ResolveTransition(int index, double time)
    {
        if (!AppSettings.FoliaSonnetTransitions)
            return new(0, 0, 1, 0);
        var current = _shots[index];
        if (index > 0 && _shots[index - 1].Paragraph == current.Paragraph)
        {
            float duration = Math.Clamp((float)(current.Start - _shots[index - 1].Start) * .18f, .14f, .24f);
            if (time <= current.Start + duration && time >= current.Start)
                return Effect(Boundary(current.TransitionSeed, current.Ordinal - 1), false, (float)(time - current.Start) / duration, current.TransitionSeed + current.Ordinal * 97);
        }
        if (index + 1 < _shots.Length && _shots[index + 1].Paragraph == current.Paragraph)
        {
            float duration = Math.Clamp((float)(_shots[index + 1].Start - current.Start) * .18f, .14f, .24f);
            double start = _shots[index + 1].Start - duration;
            if (time >= start)
                return Effect(Boundary(current.TransitionSeed, current.Ordinal), true, (float)(time - start) / duration, current.TransitionSeed + (current.Ordinal + 1) * 97);
        }
        if (current.Enter is { } enter)
        {
            float duration = Math.Clamp((float)(enter.End - enter.Start), .16f, .3f);
            if (time >= current.ParagraphStart && time <= current.ParagraphStart + duration)
                return Effect(enter.Kind, false, (float)(time - current.ParagraphStart) / duration, current.TransitionSeed);
        }
        if (current.Exit is { } exit && time >= exit.Start)
            return Effect(exit.Kind, true, (float)((time - exit.Start) / Math.Max(.001, exit.End - exit.Start)), current.TransitionSeed);
        return new(0, 0, 1, 0);
    }

    private static string Boundary(uint seed, int index) => ((seed ^ unchecked((uint)(index + 1) * 0x9e3779b1)) % 3) switch
    {
        0 => "fast-blur",
        1 => "mono-glitch",
        _ => "camera-pull"
    };

    private static Vector4 Effect(string kind, bool exit, float progress, double seed)
    {
        float linear = Math.Clamp(progress, 0, 1), eased = FoliaMotion.Bezier(.65f, 0, .35f, 1, linear);
        float amount = exit ? eased : 1 - eased;
        return kind switch
        {
            "fast-blur" => new(amount * 14, 0, exit ? 1 - amount : 1 - amount * .82f, 0),
            "mono-glitch" => new(0, amount, exit && linear > .86f ? 1 - (linear - .86f) / .14f : 1, (float)((seed * .0001 + Math.Floor(linear * 14) * .173) % 997)),
            _ => new(0, 0, exit ? 1 - amount : 1 - amount * .72f, 0)
        };
    }

    private static void WriteGuides(ref FoliaPainter painter, Shot shot, double time, Vector2 focus, Vector2 anchor, float scale, float rotation, Vector3 color)
    {
        if (!AppSettings.FoliaSonnetGuides)
            return;
        var matrix = Matrix3x2.CreateTranslation(-focus) * Matrix3x2.CreateScale(scale) * Matrix3x2.CreateRotation(rotation) * Matrix3x2.CreateTranslation(anchor);
        foreach (var text in shot.Text)
        {
            if (text.Role == "decoration" || text.Glyphs.Length == 0)
                continue;
            var first = text.Glyphs[0];
            var last = text.Glyphs[^1];
            float lead = Math.Clamp(.18f + (float)(last.Start - first.Start) * .1f, .2f, .38f);
            double start = first.Start - lead, end = first.Start + .65;
            if (time < start || time > end)
                continue;
            float progress = Math.Clamp((float)((time - start) / (end - start)), 0, 1);
            float draw = Math.Clamp(progress / .35f, 0, 1), fade = 1 - Math.Clamp((progress - .4f) / .3f, 0, 1);
            bool hero = text.Role is "hero" or "semi-hero";
            Vector2 center = (first.Base + last.Base) * .5f;
            Vector2 entry = first.Enter;
            if (entry.LengthSquared() < .001f)
                entry = new(-text.Font * 1.8f, -text.Font * .9f);
            Vector2 previous = Vector2.Transform(center + entry, matrix);
            for (int i = 1; i <= 20 && fade > 0; i++)
            {
                float t = i / 20f * draw, inverse = 1 - t;
                Vector2 local = entry * (inverse * inverse * inverse)
                    + new Vector2(entry.X * .6f, entry.Y * .4f) * (3 * inverse * inverse * t)
                    + new Vector2(entry.X * .2f, entry.Y * .1f) * (3 * inverse * t * t);
                Vector2 next = Vector2.Transform(center + local, matrix);
                float intensity = i * i / 400f;
                painter.Line(previous.X, previous.Y, next.X, next.Y, color, Math.Min(1, intensity * (hero ? .82f : .55f) * fade * 1.6f),
                    ((hero ? 2.5f : 1.5f) + intensity * (hero ? 5 : 3)) * scale);
                previous = next;
            }
            if (fade > 0)
            {
                float size = (hero ? 14 : 9) * scale;
                painter.Shape(15, previous.X - size, previous.Y - size, size * 2, size * 2, color, fade * .5f);
                painter.Shape(16, previous.X - size * .3f, previous.Y - size * .3f, size * .6f, size * .6f, Vector3.One, fade);
            }
            float burst = Math.Clamp((progress - .3f) / .7f, 0, 1), travel = 1 - MathF.Pow(1 - burst, 3);
            int count = hero ? 6 : 3;
            for (int i = 0; i < count; i++)
            {
                float angle = i * 2.39996323f + first.EntryRotation;
                Vector2 point = Vector2.Transform(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (text.Font * .5f * travel), matrix);
                float size = (hero ? 5 : 3) * scale * (1 - burst * .4f);
                painter.Shape(i % 2 == 0 ? 16 : 1, point.X - size, point.Y - size, size * 2, size * 2, color,
                    (1 - burst) * (hero ? .8f : .6f), burst * angle);
            }
        }
    }

    internal void WriteOverlay(ref FoliaPainter painter, Vector3 color)
    {
        int first = painter.Count;
        if (AppSettings.FoliaSonnetFrame && _shots.Length > 0)
        {
            float w = painter.Width, h = painter.Height;
            float x = Math.Max(30, w * .05f), y = Math.Max(30, h * .05f);
            painter.Shape(1, x, y, 30, 4, color, .8f);
            painter.Line(x, y + 16, x, y + 120, color, .5f);
            painter.Shape(1, w - x - 4, h - y - 16, 4, 16, color, .8f);
            painter.Line(w - x - 160, h - y, w - x - 20, h - y, color, .5f);
            painter.Line(w - x, h - y - 180, w - x, h - y - 30, color, .5f);
            painter.Line(w - x - 6, y + 20, w - x + 6, y + 20, color, .8f);
            painter.Line(w - x, y + 14, w - x, y + 26, color, .8f);
            painter.Shape(8, x - 4, h - y - 4, 8, 8, color, .7f);
        }
        OverlayCount = painter.Count - first;
    }
}
