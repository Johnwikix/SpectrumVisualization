using System;
using System.Numerics;
using System.Text.Json;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed partial class FoliaTempera
{
    /// <summary>Stores the original absolute-time paragraph boundary.</summary>
    private sealed record Transition(string Kind, double Start, double End);
    private readonly record struct BoundaryFrame(Vector2 Offset, float Scale, float Alpha, float Blur, float Wipe);
    private static Transition? ReadTransition(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null
        : new(value.GetProperty("kind").GetString()!, value.GetProperty("startTime").GetDouble(), value.GetProperty("endTime").GetDouble());

    private static BoundaryFrame ResolveTransition(Shot shot, double time)
    {
        if (!AppSettings.FoliaTemperaTransitions) return new(default, 1, 1, 0, 0);
        if (shot.Enter is { } previous)
        {
            float duration = (float)Math.Max(.001, previous.End - previous.Start);
            if (previous.Kind != "block-wipe" && time < shot.ParagraphStart && time >= previous.Start)
                return Effect(previous.Kind, false, (float)(time - previous.Start) / duration, shot.Flow);
            duration = Math.Clamp(duration, .35f, 1);
            if (previous.Kind == "block-wipe" && time >= shot.ParagraphStart && time <= shot.ParagraphStart + duration)
                return Effect(previous.Kind, false, (float)(time - shot.ParagraphStart) / duration, shot.Flow);
        }
        if (shot.Exit is { } exit && time >= exit.Start)
            return Effect(exit.Kind, true, (float)((time - exit.Start) / Math.Max(.001, exit.End - exit.Start)), shot.Flow);
        return new(default, 1, 1, 0, 0);
    }

    private static BoundaryFrame Effect(ReadOnlySpan<char> kind, bool exit, float progress, float angle)
    {
        float linear = Math.Clamp(progress, 0, 1), eased = FoliaMotion.InOut(linear);
        Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
        if (kind.SequenceEqual("block-wipe")) return new(default, 1, 1, 0, exit ? eased : 1 + eased);
        float away = exit ? eased : 1 - eased;
        if (kind.SequenceEqual("camera-pan"))
            return new(direction * (exit ? eased : eased - 1) * .5f, 1 + away * .03f,
                exit ? 1 - Math.Clamp((linear - .72f) / .28f, 0, 1) : Math.Clamp(linear / .3f, 0, 1), 0, 0);
        return new(direction * (exit ? eased : eased - 1) * .09f, 1 + away * .07f,
            exit ? 1 - Math.Clamp((linear - .55f) / .45f, 0, 1) : Math.Clamp(linear / .45f, 0, 1), away * 6, 0);
    }

    private static void WriteWipe(ref FoliaPainter painter, Shot shot, double time)
    {
        float wipe = ResolveTransition(shot, time).Wipe;
        if (wipe <= 0 || wipe >= 2) return;
        float span = MathF.Sqrt(painter.Width * painter.Width + painter.Height * painter.Height);
        Vector2 direction = new(MathF.Cos(shot.Flow), MathF.Sin(shot.Flow));
        Vector2 center = new Vector2(painter.Width * .5f, painter.Height * .5f) + direction * ((wipe - 1) * span * 1.2f);
        painter.Shape(1, center.X - span * .6f, center.Y - span * .6f, span * 1.2f, span * 1.2f, Ink, 1, rotation: shot.Flow);
    }
}
