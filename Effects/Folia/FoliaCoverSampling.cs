using System;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold cover reduction for Canvas-style 50 x 50 representative-color extraction.</summary>
internal static class FoliaCoverSampling
{
    internal static byte[] PalettePixels(ReadOnlySpan<byte> rgba, int width, int height)
    {
        const int side = 50;
        // Canvas downsamples through a mip before the final bilinear draw. Direct2D's direct linear
        // draw differs by just one byte, but that can change median-cut bucket ordering and the scene hue.
        int step = 1;
        while (width / (step * 2) >= side && height / (step * 2) >= side)
            step *= 2;
        int mipWidth = Math.Max(1, width / step), mipHeight = Math.Max(1, height / step);
        var mip = new byte[checked(mipWidth * mipHeight * 4)];
        for (int y = 0; y < mipHeight; y++)
        for (int x = 0; x < mipWidth; x++)
        for (int channel = 0; channel < 4; channel++)
        {
            int sum = 0;
            for (int j = 0; j < step; j++)
            for (int i = 0; i < step; i++)
            {
                int pixel = ((y * step + j) * width + x * step + i) * 4;
                sum += channel == 3 ? rgba[pixel + 3] : (rgba[pixel + channel] * rgba[pixel + 3] + 127) / 255;
            }
            int count = step * step;
            mip[(y * mipWidth + x) * 4 + channel] = (byte)((sum + count / 2) / count);
        }
        var result = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
        for (int x = 0; x < side; x++)
        {
            double sourceX = (x + .5) * mipWidth / side - .5, sourceY = (y + .5) * mipHeight / side - .5;
            int left = (int)Math.Floor(sourceX), top = (int)Math.Floor(sourceY);
            double fx = sourceX - left, fy = sourceY - top;
            int a = (Math.Clamp(top, 0, mipHeight - 1) * mipWidth + Math.Clamp(left, 0, mipWidth - 1)) * 4;
            int b = (Math.Clamp(top, 0, mipHeight - 1) * mipWidth + Math.Clamp(left + 1, 0, mipWidth - 1)) * 4;
            int c = (Math.Clamp(top + 1, 0, mipHeight - 1) * mipWidth + Math.Clamp(left, 0, mipWidth - 1)) * 4;
            int d = (Math.Clamp(top + 1, 0, mipHeight - 1) * mipWidth + Math.Clamp(left + 1, 0, mipWidth - 1)) * 4;
            for (int channel = 0; channel < 4; channel++)
            {
                double upper = mip[a + channel] * (1 - fx) + mip[b + channel] * fx;
                double lower = mip[c + channel] * (1 - fx) + mip[d + channel] * fx;
                result[(y * side + x) * 4 + channel] = (byte)Math.Clamp((int)Math.Floor(upper * (1 - fy) + lower * fy + .5), 0, 255);
            }
        }
        return result;
    }
}
