using System;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Bounded frame writer. Atlas / layout data is immutable, and this writer owns no heap storage.</summary>
internal ref struct FoliaPainter(Span<FoliaInstance> instances, float width, float height)
{
    private readonly Span<FoliaInstance> _instances = instances;
    internal readonly float Width = width;
    internal readonly float Height = height;
    internal int Count;
    internal int Remaining => _instances.Length - Count;

    internal void Append(ReadOnlySpan<FoliaInstance> source)
    {
        int length = Math.Min(source.Length, Remaining);
        source[..length].CopyTo(_instances[Count..]);
        Count += length;
    }

    internal void FadeFrom(int first, float alpha, float blur = 0)
    {
        for (int i = first; i < Count; i++)
        {
            ref var instance = ref _instances[i];
            instance.Color.W = Math.Clamp(instance.Color.W * alpha, 0, 1);
            if (instance.Shape.X == 0)
                instance.Transform.Z += blur;
        }
    }

    internal void TransformFrom(int first, Vector2 pivot, Vector2 position, float scale, float rotation)
    {
        float cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
        for (int i = first; i < Count; i++)
        {
            ref var instance = ref _instances[i];
            Vector2 center = (new Vector2(instance.Rect.X, instance.Rect.Y) + new Vector2(instance.Rect.Z, instance.Rect.W) * .5f - pivot) * scale;
            center = position + new Vector2(center.X * cos - center.Y * sin, center.X * sin + center.Y * cos);
            instance.Rect.Z *= scale;
            instance.Rect.W *= scale;
            instance.Rect.X = center.X - instance.Rect.Z * .5f;
            instance.Rect.Y = center.Y - instance.Rect.W * .5f;
            instance.Transform.X += rotation;
            instance.Transform.Z *= scale;
            instance.Shape.Y *= scale;
            instance.Shape.Z *= scale;
        }
    }

    internal void AffineFrom(int first, in Matrix3x2 transform)
    {
        for (int i = first; i < Count; i++)
        {
            ref var instance = ref _instances[i];
            var center = Vector2.Transform(new Vector2(instance.Rect.X, instance.Rect.Y) + new Vector2(instance.Rect.Z, instance.Rect.W) * .5f, transform);
            float cos = MathF.Cos(instance.Transform.X), sin = MathF.Sin(instance.Transform.X);
            var u = Vector2.TransformNormal(new(cos, sin), transform);
            var v = Vector2.TransformNormal(new(instance.Transform.Y * cos - sin, instance.Transform.Y * sin + cos), transform);
            float xScale = u.Length();
            u /= Math.Max(xScale, .000001f);
            float yScale = u.X * v.Y - u.Y * v.X;
            float shear = Vector2.Dot(u, v) / Math.Max(yScale, .000001f);
            instance.Rect.Z *= xScale;
            instance.Rect.W *= yScale;
            instance.Rect.X = center.X - instance.Rect.Z * .5f;
            instance.Rect.Y = center.Y - instance.Rect.W * .5f;
            instance.Transform.X = MathF.Atan2(u.Y, u.X);
            instance.Transform.Y = shear;
            float average = MathF.Sqrt(Math.Max(0, xScale * yScale));
            instance.Transform.Z *= average;
            instance.Effect.Z *= average;
        }
    }

    internal void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector3 color, float alpha)
    {
        if (alpha <= .001f || Count >= _instances.Length)
            return;
        Vector2 minimum = Vector2.Min(a, Vector2.Min(b, c)), maximum = Vector2.Max(a, Vector2.Max(b, c));
        Vector2 size = maximum - minimum;
        if (size.X < .001f || size.Y < .001f)
            return;
        Vector2 x = (a - minimum) / size, y = (b - minimum) / size, z = (c - minimum) / size;
        _instances[Count++] = new()
        {
            Rect = new(minimum, size.X, size.Y),
            Uv = new(x.X, x.Y, y.X, y.Y),
            Color = new(color, alpha),
            Shape = new(14, 0, 0, 0),
            Effect = new(z.X, z.Y, 0, 0)
        };
    }
    internal static float Clamp(float x, float low, float high) => Math.Clamp(x, low, high);
    internal static float Ease(float x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }
    internal static float Random(double seed, double offset = 0)
    {
        double value = Math.Sin(seed + offset) * 10000;
        return (float)(value - Math.Floor(value));
    }

    internal void GlyphCentered(in FoliaGlyph glyph, float x, float y, float font, Vector3 color, float alpha,
        float rotation = 0, float glow = 0, bool outline = false, float outlineWidth = 1.05f, float letterSpacing = 0)
    {
        float localX = (glyph.BearingX + glyph.Width * .5f - (glyph.Advance + letterSpacing) * .5f) * font;
        float localY = glyph.CanvasCenterY * font;
        float cos = MathF.Cos(rotation), sin = MathF.Sin(rotation);
        float cx = x + localX * cos - localY * sin, cy = y + localX * sin + localY * cos;
        Glyph(glyph, cx - (glyph.BearingX + glyph.Width * .5f) * font, cy - (glyph.BearingY + glyph.Height * .5f) * font,
            font, color, alpha, rotation, glow: glow, outline: outline, outlineWidth: outlineWidth);
    }

    internal void Glyph(in FoliaGlyph glyph, float x, float y, float font, Vector3 color, float alpha,
        float rotation = 0, float blur = 0, float glow = 0, float shear = 0, float reveal = 1, float difference = 0, bool outline = false, float outlineWidth = 1.05f, float revealSoftness = 0, Vector3? glowColor = null)
    {
        if (alpha <= .001f || font < .1f || reveal <= 0 || Count >= _instances.Length)
            return;
        float w = glyph.Width * font, h = glyph.Height * font;
        float px = x + glyph.BearingX * font, py = y + glyph.BearingY * font;
        if (px + w < -Width || py + h < -Height || px > Width * 2 || py > Height * 2)
            return;
        _instances[Count++] = new()
        {
            Rect = new(px, py, w, h),
            Uv = new(glyph.U, glyph.V, glyph.Uw, glyph.Vh),
            Color = new(color, Math.Clamp(alpha, 0, 1)),
            Transform = new(rotation, shear, blur, glow),
            Shape = new(0, 0, 0, 0),
            GlyphData = new(glyph.Page, 0, glowColor is { } tint ? PackColor(tint) : 0, 0),
            Effect = new(reveal >= 1 ? 1 : Math.Clamp((glyph.Advance * reveal - glyph.BearingX) / glyph.Width, 0, 1), reveal >= 1 ? 0 : revealSoftness, outline ? outlineWidth : 0, difference)
        };
    }

    // A float represents every RGB24 integer exactly. Zero means body-colored glow;
    // adding one preserves an explicit black override without enlarging each instance.
    private static float PackColor(Vector3 color)
    {
        var rgb = Vector3.Clamp(color, Vector3.Zero, Vector3.One) * 255;
        return 1 + ((int)MathF.Round(rgb.X) << 16 | (int)MathF.Round(rgb.Y) << 8 | (int)MathF.Round(rgb.Z));
    }

    internal void GlyphQuad(in FoliaGlyph glyph, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft,
        Vector4 depths, Vector3 color, float alpha, float glow = 0, bool additive = false)
    {
        if (alpha <= .001f || Count >= _instances.Length)
            return;
        // Preserve each corner's clip W so atlas sampling stays perspective-correct across the plane.
        _instances[Count++] = new()
        {
            Rect = new(topLeft.X, topLeft.Y, topRight.X, topRight.Y),
            Transform = new(bottomRight.X, bottomRight.Y, bottomLeft.X, bottomLeft.Y),
            Uv = new(glyph.U, glyph.V, glyph.Uw, glyph.Vh),
            Color = new(color, Math.Clamp(alpha, 0, 1)),
            Shape = new(25, depths.X, depths.Y, depths.Z),
            GlyphData = new(glyph.Page, additive ? 1 : 0, 0, 0),
            Effect = new(depths.W, 0, 0, glow)
        };
    }

    internal void Shape(int kind, float x, float y, float w, float h, Vector3 color, float alpha,
        float rotation = 0, float stroke = 1, float radius = 0, float phase = 0, Vector4 uv = default)
    {
        if (alpha <= .001f || Count >= _instances.Length || w <= 0 || h <= 0)
            return;
        _instances[Count++] = new()
        {
            Rect = new(x, y, w, h),
            Uv = uv,
            Color = new(color, Math.Clamp(alpha, 0, 1)),
            Transform = new(rotation, 0, 0, 0),
            Shape = new(kind, stroke, radius, phase),
            Effect = new(1, 0, 0, 0)
        };
    }

    internal void Line(float x1, float y1, float x2, float y2, Vector3 color, float alpha, float stroke = 1)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        Shape(1, (x1 + x2 - length) * .5f, (y1 + y2 - stroke) * .5f, length, stroke, color, alpha, MathF.Atan2(dy, dx));
    }

    internal void RailMaskFrom(int first, float top, float bottom)
    {
        for (int i = first; i < Count; i++)
        {
            ref var instance = ref _instances[i];
            instance.Shape.Y = top;
            instance.Shape.Z = bottom;
            instance.Shape.W = 1;
        }
    }

    internal void Text(ReadOnlySpan<FoliaPreparedGlyph> glyphs, float x, float y, float font, Vector3 color, float alpha,
        float rotation = 0, float blur = 0, float glow = 0, float shear = 0, float difference = 0)
    {
        float c = MathF.Cos(rotation), s = MathF.Sin(rotation);
        foreach (ref readonly var glyph in glyphs)
        {
            float offset = glyph.Offset * font;
            Glyph(glyph.Glyph, x + offset * c, y + offset * s, font, color, alpha, rotation, blur, glow, shear, difference: difference);
        }
    }
}
