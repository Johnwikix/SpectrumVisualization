using System;
using System.Numerics;

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
        int lanes = Vector<float>.Count;
        for (int y = 0; y < height; y++)
        {
            int x = 0;
            // Interior pixels share the full kernel. Keep the scalar accumulation order
            // in every SIMD lane so the versioned shadow cache remains byte-identical.
            int vectorEnd = Vector.IsHardwareAccelerated ? width - radius - lanes : -1;
            while (x < width)
            {
                if (x >= radius && x <= vectorEnd)
                {
                    var sum = Vector<float>.Zero;
                    for (int k = -radius; k <= radius; k++)
                        sum += new Vector<float>(coverage.Slice(y * width + x + k, lanes)) * new Vector<float>(kernel[k + radius]);
                    sum.CopyTo(horizontal.Slice(y * width + x, lanes));
                    x += lanes;
                }
                else
                {
                    float sum = 0;
                    for (int k = Math.Max(-radius, -x); k <= Math.Min(radius, width - 1 - x); k++)
                        sum += coverage[y * width + x + k] * kernel[k + radius];
                    horizontal[y * width + x++] = sum;
                }
            }
        }
        for (int y = 0; y < height; y++)
        {
            int first = Math.Max(-radius, -y), last = Math.Min(radius, height - 1 - y);
            int x = 0, target = (atlasY + y) * atlasWidth + atlasX;
            if (Vector.IsHardwareAccelerated)
                for (; x <= width - lanes; x += lanes)
                {
                    var sum = Vector<float>.Zero;
                    for (int k = first; k <= last; k++)
                        sum += new Vector<float>(horizontal.Slice((y + k) * width + x, lanes)) * new Vector<float>(kernel[k + radius]);
                    for (int lane = 0; lane < lanes; lane++)
                        atlas[target + x + lane] = (byte)Math.Clamp((int)(sum[lane] * 255 + .5f), 0, 255);
                }
            for (; x < width; x++)
            {
                float sum = 0;
                for (int k = first; k <= last; k++)
                    sum += horizontal[(y + k) * width + x] * kernel[k + radius];
                atlas[target + x] = (byte)Math.Clamp((int)(sum * 255 + .5f), 0, 255);
            }
        }
    }
}
