using System;
using System.Collections.Generic;
using Windows.UI;

namespace WinExSpectrumTest.Effects;

/// <summary>Cold RGBA cover mean and hue palette. Called only when album artwork changes.</summary>
internal static class AuroraCoverPalette
{
    internal static (Color Primary, Color[] Colors) Extract(ReadOnlySpan<byte> pixels)
    {
        Color primary = Color.FromArgb(255, 90, 170, 255);
        Color[] palette = [primary, Color.FromArgb(255, 60, 220, 200)];
        float r = 0f, g = 0f, b = 0f;
        int count = 0;
        // 12 个色相桶，权重 = 饱和度 × 亮度 × 出现次数；剔除近黑/近灰像素
        Span<float> hueWeight = stackalloc float[12];
        Span<float> hueR = stackalloc float[12];
        Span<float> hueG = stackalloc float[12];
        Span<float> hueB = stackalloc float[12];
        hueWeight.Clear();
        hueR.Clear();
        hueG.Clear();
        hueB.Clear();
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (pixels[i + 3] < 128) continue;
            count++;
            float pr = pixels[i] / 255f;
            float pg = pixels[i + 1] / 255f;
            float pb = pixels[i + 2] / 255f;
            r += pr;
            g += pg;
            b += pb;

            float max = MathF.Max(pr, MathF.Max(pg, pb));
            float min = MathF.Min(pr, MathF.Min(pg, pb));
            float value = max;
            float saturation = max <= 0f ? 0f : (max - min) / max;
            if (value < 0.14f || saturation < 0.14f) continue;   // 近黑/近灰：不参与色相统计
            float hue = 0f;
            float delta = max - min;
            if (delta > 0f)
            {
                if (max == pr) hue = ((pg - pb) / delta) % 6f;
                else if (max == pg) hue = (pb - pr) / delta + 2f;
                else hue = (pr - pg) / delta + 4f;
                hue *= 60f;
                if (hue < 0f) hue += 360f;
            }
            int bin = Math.Clamp((int)(hue / 30f), 0, 11);
            float weight = saturation * value;
            hueWeight[bin] += weight;
            hueR[bin] += pr * weight;
            hueG[bin] += pg * weight;
            hueB[bin] += pb * weight;
        }

        if (count == 0) return (primary, palette);
        primary = Color.FromArgb(255,
            (byte)(Math.Clamp(r / count * 1.25f, 0f, 1f) * 255f),
            (byte)(Math.Clamp(g / count * 1.25f, 0f, 1f) * 255f),
            (byte)(Math.Clamp(b / count * 1.25f, 0f, 1f) * 255f));

        palette = [primary];

        // 取权重最高的色相桶，按色相排序构成 4 色调色板（相邻桶去重），
        // 桶均值色向主色回拉 35% 保证与整体观感协调。
        var candidates = new List<(int Bin, float Weight)>(12);
        for (int bin = 0; bin < 12; bin++)
        {
            if (hueWeight[bin] > 0.01f) candidates.Add((bin, hueWeight[bin]));
        }
        candidates.Sort((a, b2) => b2.Weight.CompareTo(a.Weight));
        var picked = new List<int>(4);
        foreach ((int bin, _) in candidates)
        {
            bool adjacent = false;
            foreach (int chosen in picked)
            {
                int distance = Math.Abs(chosen - bin);
                distance = Math.Min(distance, 12 - distance);
                if (distance <= 1) { adjacent = true; break; }
            }
            if (!adjacent) picked.Add(bin);
            if (picked.Count == 4) break;
        }
        if (picked.Count > 0)
        {
            picked.Sort();
            var extracted = new Color[picked.Count];
            for (int k = 0; k < picked.Count; k++)
            {
                int bin = picked[k];
                float w = MathF.Max(hueWeight[bin], 0.001f);
                var mean = Color.FromArgb(255,
                    (byte)(Math.Clamp(hueR[bin] / w, 0f, 1f) * 255f),
                    (byte)(Math.Clamp(hueG[bin] / w, 0f, 1f) * 255f),
                    (byte)(Math.Clamp(hueB[bin] / w, 0f, 1f) * 255f));
                extracted[k] = LerpColor(mean, primary, 0.35f);
            }
            palette = extracted;
        }
        return (primary, palette);
    }

    private static Color LerpColor(Color a, Color b, float t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
