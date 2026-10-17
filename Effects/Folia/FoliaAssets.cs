using System;
using System.IO;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.UI;

namespace WinExSpectrumTest.Effects.Folia;

internal static class FoliaAssets
{
    internal sealed record EmojiAtlas(byte[] Pixels, int Width, int Height, Vector4[] Rects);
    private static readonly Lazy<EmojiAtlas> EmojiCache = new(BuildEmojis);
    internal static EmojiAtlas Emojis => EmojiCache.Value;

    private static EmojiAtlas BuildEmojis()
    {
        string[] names = ["happy1.png", "love1.png", "normal1.png", "sleepy1.png", "sleepy2.png", "sleepy3.png", "vibe1.png", "vibe2.png", "vibe3.png"];
        const int tile = 256, size = tile * 3;
        using var device = new CanvasDevice();
        using var target = new CanvasRenderTarget(device, size, size, 96);
        var rects = new Vector4[names.Length];
        using (var draw = target.CreateDrawingSession())
        {
            draw.Clear(Color.FromArgb(0, 0, 0, 0));
            for (int i = 0; i < names.Length; i++)
            {
                using var source = typeof(FoliaAssets).Assembly.GetManifestResourceStream("Spectrum.Folia.Emoji." + names[i])
                    ?? throw new InvalidOperationException("Missing Folia emoji " + names[i]);
                using var memory = new MemoryStream();
                source.CopyTo(memory);
                memory.Position = 0;
                using var bitmap = CanvasBitmap.LoadAsync(device, memory.AsRandomAccessStream()).AsTask().GetAwaiter().GetResult();
                float x = i % 3 * tile, y = i / 3 * tile;
                double fit = Math.Min((tile - 16) / bitmap.Size.Width, (tile - 16) / bitmap.Size.Height);
                double w = bitmap.Size.Width * fit, h = bitmap.Size.Height * fit;
                draw.DrawImage(bitmap, new Rect(x + (tile - w) * .5, y + (tile - h) * .5, w, h));
                rects[i] = new(x / size, y / size, 1f / 3, 1f / 3);
            }
        }
        return new(target.GetPixelBytes(), size, size, rects);
    }
    internal static byte[] Noise()
    {
        using var source = typeof(FoliaAssets).Assembly.GetManifestResourceStream("Spectrum.Folia.noise.png") ?? throw new InvalidOperationException("Missing Folia noise texture.");
        using var memory = new MemoryStream();
        source.CopyTo(memory);
        memory.Position = 0;
        using var device = new CanvasDevice();
        using var bitmap = CanvasBitmap.LoadAsync(device, memory.AsRandomAccessStream()).AsTask().GetAwaiter().GetResult();
        return bitmap.GetPixelBytes();
    }
}
