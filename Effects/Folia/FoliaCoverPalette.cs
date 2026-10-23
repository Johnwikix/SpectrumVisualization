using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold cover extraction and a readable dark stage theme. Published arrays are immutable.</summary>
internal static class FoliaCoverPalette
{
    internal static Vector3[] Extract(byte[] rgba, int width, int height, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length)
            return [];
        byte[] pixels = FoliaCoverSampling.PalettePixels(rgba, width, height);
        using var input = new MemoryStream();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartArray();
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i + 3];
                for (int c = 0; c < 3; c++)
                    writer.WriteNumberValue(alpha == 0 ? 0 : Math.Min(255, (pixels[i + c] * 255 + alpha / 2) / alpha));
                writer.WriteNumberValue(alpha);
            }
            writer.WriteEndArray();
        }
        string json = FoliaSourcePlanner.Compile(Encoding.UTF8.GetString(input.GetBuffer(), 0, (int)input.Length), "compilePalette", token);
        using var document = JsonDocument.Parse(json);
        var colors = new Vector3[document.RootElement.GetArrayLength()];
        for (int i = 0; i < colors.Length; i++)
        {
            string hex = document.RootElement[i].GetString()!;
            int rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            colors[i] = new Vector3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255;
        }
        token.ThrowIfCancellationRequested();
        return colors;
    }

    internal static Vector3[] CreateTheme(ReadOnlySpan<Vector3> colors)
    {
        if (colors.IsEmpty) return FoliaSong.Empty.Palette;
        Vector3 dominant = colors[0], secondary = colors.Length > 1 ? colors[1] : dominant;
        Vector3 accent = dominant;
        float chroma = -1;
        foreach (var color in colors)
        {
            float range = Math.Max(color.X, Math.Max(color.Y, color.Z)) - Math.Min(color.X, Math.Min(color.Y, color.Z));
            if (range > chroma) { chroma = range; accent = color; }
        }
        // Preserve the native stage's dark appearance. Text and accents keep cover hues,
        // but must remain readable even for black, white or monochrome album artwork.
        Vector3 background = dominant * .1f;
        return [background, Readable(Vector3.Lerp(dominant, Vector3.One, .78f), background, 4.5f),
            Readable(Vector3.Lerp(secondary, Vector3.One, .45f), background, 4.5f),
            Readable(Vector3.Lerp(accent, Vector3.One, .2f), background, 3)];
    }

    internal static byte[] Composite(byte[] rgba, Vector3 background, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        int first = 3;
        while (first < rgba.Length && rgba[first] == 255) first += 4;
        if (first >= rgba.Length) return rgba;
        var result = (byte[])rgba.Clone();
        for (int i = first - 3; i < result.Length; i += 4)
        {
            if (((i - first + 3) & 16383) == 0) token.ThrowIfCancellationRequested();
            float alpha = rgba[i + 3] / 255f;
            result[i] = (byte)MathF.Round(rgba[i] * alpha + background.X * 255 * (1 - alpha));
            result[i + 1] = (byte)MathF.Round(rgba[i + 1] * alpha + background.Y * 255 * (1 - alpha));
            result[i + 2] = (byte)MathF.Round(rgba[i + 2] * alpha + background.Z * 255 * (1 - alpha));
            result[i + 3] = 255;
        }
        token.ThrowIfCancellationRequested();
        return result;
    }

    private static Vector3 Readable(Vector3 color, Vector3 background, float contrast)
    {
        float target = (Luminance(background) + .05f) * contrast - .05f;
        if (Luminance(color) >= target) return color;
        float low = 0, high = 1;
        for (int i = 0; i < 16; i++)
        {
            float mix = (low + high) * .5f;
            if (Luminance(Vector3.Lerp(color, Vector3.One, mix)) < target) low = mix;
            else high = mix;
        }
        return Vector3.Lerp(color, Vector3.One, high);
    }

    private static float Luminance(Vector3 color) => Linear(color.X) * .2126f + Linear(color.Y) * .7152f + Linear(color.Z) * .0722f;
    private static float Linear(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
}
