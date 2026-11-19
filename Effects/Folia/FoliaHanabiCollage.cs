using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaHanabiCollage(string Kind, string Style, Vector2 Center, Vector2 Size,
    Vector2 Destination, Vector3 Color, float Rotation, float Travel, float Direction, float Alpha, double Start, double End,
    double Fade, bool Front, FoliaPreparedGlyph[] Text, float TextWidth, float Font)
{
    internal static FoliaHanabiCollage[] Read(JsonElement source, FoliaTextAtlas atlas)
    {
        var items = new FoliaHanabiCollage[source.GetArrayLength()];
        for (int i = 0; i < items.Length; i++)
        {
            var item = source[i];
            string kind = item.GetProperty("kind").GetString()!;
            string style = String(item, "shape") ?? String(item, "style") ?? String(item, "pattern") ?? String(item, "icon") ?? "";
            string? label = String(item, "text") ?? String(item, "label") ?? String(item, "title");
            float size = Number(item, "size");
            float width = Number(item, "length", Number(item, "width", size));
            float height = Number(item, "thickness", Number(item, "height", kind == "tape" ? Number(item, "width") : size));
            double start = Number(item, "at"), duration = Number(item, "duration");
            double end = Number(item, "exitAt", (float)(start + duration * .72));
            double fade = Number(item, "exitDuration", (float)(duration * .28));
            int rgb = (int)Number(item, "color", Number(item, "bar", 0xffbbde));
            bool front = String(item, "layer") == "front" || kind is "label" or "pop" or "whoosh" or "floater" or "tape";
            var text = new List<FoliaPreparedGlyph>();
            float advance = 0;
            if (!string.IsNullOrEmpty(label))
            {
                var elements = StringInfo.GetTextElementEnumerator(label);
                while (elements.MoveNext())
                {
                    if (!atlas.CharacterGlyphs.TryGetValue(elements.GetTextElement(), out var glyph))
                        continue;
                    text.Add(new(glyph, advance, 0, 0, 0, text.Count));
                    advance += glyph.Advance;
                }
            }
            items[i] = new(kind, style, new(Number(item, "x", Number(item, "fromX")), Number(item, "y", Number(item, "fromY"))),
                new(width, height), new(Number(item, "toX"), Number(item, "toY")), new((rgb >> 16) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f),
                Number(item, "rotation"), Number(item, "speed", Number(item, "rise")), Number(item, "from"), Number(item, "alpha", 1), start, end, fade,
                front, text.ToArray(), advance, size);
        }
        return items;
    }

    private static string? String(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static float Number(JsonElement item, string name, float missing = 0) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : missing;

    internal void Write(ref FoliaPainter painter, double position, float layerAlpha)
    {
        if (position < Start || position >= End + Fade)
            return;
        float elapsed = (float)(position - Start);
        float alpha = Alpha * layerAlpha * FoliaPainter.Ease(elapsed / .22f) *
            (1 - FoliaPainter.Ease((float)((position - End) / Math.Max(.001, Fade))));
        if (alpha <= .002f)
            return;
        Vector2 center = Center;
        if (Kind is "floater" or "whoosh" or "pop")
        {
            float phase = (float)((position - Start) / Math.Max(.001, End + Fade - Start));
            center = Kind == "whoosh" ? Vector2.Lerp(Center, Destination, phase) : center - new Vector2(0, phase * Travel);
        }
        Vector2 size = Size * (.68f + .32f * FoliaPainter.Ease(elapsed / .22f));
        int first = painter.Count;
        switch (Kind)
        {
            case "echo":
                if (Text.Length > 0)
                {
                    float stride = Font * (Style == "y" ? 1.12f : TextWidth + 1.1f);
                    float distance = stride > 0 ? elapsed * Travel % stride : 0;
                    for (int i = -3; i <= 3; i++)
                    {
                        float offset = i * stride + distance;
                        DrawText(ref painter, center + (Style == "y" ? new Vector2(0, offset) : new Vector2(offset, 0)), Font, Color, alpha);
                    }
                }
                break;
            case "label":
                if (TextWidth > 0)
                    Box(ref painter, center, new(TextWidth * Font + 12, Font * 1.55f), Color, alpha, 35);
                DrawText(ref painter, center, Font, new(.24f, .13f, .32f), alpha);
                break;
            case "headline":
                DrawText(ref painter, center, Font, Color, alpha);
                break;
            case "window":
                Box(ref painter, center, size, new(.09f, .07f, .14f), alpha * .95f, 3);
                Box(ref painter, center - new Vector2(0, size.Y * .4f), size * new Vector2(1, .2f), Color, alpha, 3);
                DrawText(ref painter, center - new Vector2(0, size.Y * .42f), size.Y * .105f, new(.15f, .09f, .23f), alpha);
                break;
            case "strip":
                DrawStrip(ref painter, position, layerAlpha);
                break;
            case "tape":
                float stick = 1 - MathF.Pow(1 - Math.Clamp(elapsed / .16f, 0, 1), 3);
                float tapeAlpha = stick * (1 - Math.Clamp((float)((position - End) / Math.Max(.001, Fade)), 0, 1)) * layerAlpha;
                size = Size * (1.25f - .25f * stick);
                painter.Shape(40, center.X - size.X * .5f, center.Y - size.Y * .5f, size.X, size.Y,
                    Color, tapeAlpha, phase: Style == "stripe" ? 0 : Style == "dot" ? 1 : 2);
                break;
            case "patch":
                Box(ref painter, center, size, Color, alpha * .96f, Style switch
                {
                    "dots" => 32,
                    "grid" or "gingham" => 34,
                    _ => 35
                });
                break;
            default:
                Box(ref painter, center, size, Color, alpha, Style switch
                {
                    "star" or "sparkle" => 9,
                    "ribbon" => 33,
                    "flower" or "Flower" => 36,
                    _ => 16
                });
                DrawText(ref painter, center, size.Y * .32f, new(.13f, .09f, .18f), alpha);
                break;
        }
        if (Rotation != 0)
            painter.TransformFrom(first, center, center, 1, Rotation);
    }

    private void DrawStrip(ref FoliaPainter painter, double position, float alpha)
    {
        float wipe = Style == "cut" ? 1.25f : Style == "lace" ? .55f : .42f;
        float raw = Math.Clamp((float)(position - Start) / wipe, 0, 1);
        float enter = Style == "cut" ? FoliaPainter.Ease(raw) : 1 - MathF.Pow(1 - raw, 3);
        float exit = MathF.Pow(Math.Clamp((float)((position - End) / Math.Max(.001, Fade)), 0, 1), 3);
        float start = Direction < 0 ? exit : 1 - enter;
        float end = Direction < 0 ? enter : 1 - exit;
        float offset = Size.X * Math.Min(start, end);
        float width = Size.X * Math.Abs(end - start);
        float x = Center.X - Size.X * .5f + offset, y = Center.Y - Size.Y * .5f;
        if (Style == "film")
            painter.Shape(1, x + Size.Y * .14f, y + Size.Y * .182f, width, Size.Y, new(.169f, .122f, .239f), alpha * .3f);
        painter.Shape(Style == "film" ? 39 : Style == "cut" ? 38 : 37,
            x, y, width, Size.Y, Color, alpha, uv: new(offset - (float)position * Travel, 0, 0, 0));
    }

    private void DrawText(ref FoliaPainter painter, Vector2 center, float font, Vector3 color, float alpha) =>
        painter.Text(Text, center.X - TextWidth * font * .5f, center.Y - font * .5f, font, color, alpha);
    private static void Box(ref FoliaPainter painter, Vector2 center, Vector2 size, Vector3 color, float alpha, int shape) =>
        painter.Shape(shape, center.X - size.X * .5f, center.Y - size.Y * .5f, size.X, size.Y, color, alpha, radius: size.Y * .12f);
}
