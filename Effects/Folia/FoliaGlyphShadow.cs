using System;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold, separable Gaussian coverage convolution; independent of sharp MTSDF reconstruction.</summary>
internal static class FoliaGlyphShadow
{
    internal static float[] Kernel(float sigma)
    {
        int radius = (int)MathF.Ceiling(sigma * 3);
        var weights = new float[radius * 2 + 1];
        float total = 0;
        for (int i = -radius; i <= radius; i++)
            total += weights[i + radius] = MathF.Exp(-.5f * i * i / (sigma * sigma));
        for (int i = 0; i < weights.Length; i++)
            weights[i] /= total;
        return weights;
    }
    internal static void Write(ReadOnlySpan<float> field, int width, int height, ReadOnlySpan<float> kernel,
        Span<float> coverage, Span<float> horizontal, Span<byte> atlas, int atlasWidth, int atlasX, int atlasY)
    {
        int length = width * height, radius = kernel.Length / 2;
        for (int i = 0; i < length; i++)
            coverage[i] = Math.Clamp((field[i * 4 + 3] - .5f) * FoliaDistanceField.Range + .5f, 0, 1);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int k = Math.Max(-radius, -x); k <= Math.Min(radius, width - 1 - x); k++)
                sum += coverage[y * width + x + k] * kernel[k + radius];
            horizontal[y * width + x] = sum;
        }
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float sum = 0;
            for (int k = Math.Max(-radius, -y); k <= Math.Min(radius, height - 1 - y); k++)
                sum += horizontal[(y + k) * width + x] * kernel[k + radius];
            atlas[(atlasY + y) * atlasWidth + atlasX + x] = (byte)Math.Clamp((int)(sum * 255 + .5f), 0, 255);
        }
    }
}
