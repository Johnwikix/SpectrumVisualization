using System;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Helper
{
    /// <summary>
    /// 背景亮度采样（移植 music_player DesktopLyricsAdaptiveColor，BetterLyrics 思路）：
    /// GDI 从屏幕 DC 拉取窗口外围一圈（36px 环带）像素，统计 YIQ 亮度直方图取中位数。
    /// 亮度中位数对少数派文字/图标像素稳健，直接贴合"背景整体亮不亮"的语义。
    /// 低频（1s 轮询）在 UI 线程调用。
    /// </summary>
    internal static class ScreenLuminanceSampler
    {
        private const int EdgeThickness = 36;
        private const int SampleSize = 64;
        private const int RasterOpSrcCopy = 0x00CC0020;
        private const int StretchBltColorOnColor = 3;
        private const int DibRgbColors = 0;
        private const int SmXVirtualScreen = 76;
        private const int SmYVirtualScreen = 77;
        private const int SmCxVirtualScreen = 78;
        private const int SmCyVirtualScreen = 79;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
        }

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, int iUsage, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int iStretchMode);

        [DllImport("gdi32.dll")]
        private static extern bool StretchBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, int rop);

        /// <summary>采样窗口外围一圈环境的 YIQ 亮度中位数（0=纯黑，255=纯白）。
        /// 取不到窗口矩形时返回 false，调用方保持当前颜色。</summary>
        public static bool TrySampleWindowLuminance(IntPtr hwnd, out double luminance)
        {
            luminance = 0;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rect)) return false;
            return TrySampleRing(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, out luminance);
        }

        // 复用缓冲（仅 UI 线程 1Hz 调用）：避免每秒稳定分配拖累 Gen0 GC。
        private static readonly int[] PixelsBuffer = new int[SampleSize * SampleSize];
        private static readonly int[] HistogramBuffer = new int[256];

        /// <summary>采样屏幕矩形外围 36px 环带的 YIQ 亮度中位数。环带裁剪进虚拟屏幕；
        /// 整个环带在屏外时返回 false。</summary>
        public static bool TrySampleRing(int x, int y, int width, int height, out double luminance)
        {
            luminance = 0;
            if (width <= 0 || height <= 0) return false;

            int vsLeft = GetSystemMetrics(SmXVirtualScreen);
            int vsTop = GetSystemMetrics(SmYVirtualScreen);
            int vsRight = vsLeft + GetSystemMetrics(SmCxVirtualScreen);
            int vsBottom = vsTop + GetSystemMetrics(SmCyVirtualScreen);

            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero) return false;
            try
            {
                IntPtr hdcMem = CreateCompatibleDC(hdcScreen);
                if (hdcMem == IntPtr.Zero) return false;
                try
                {
                    if (!TryCreateSampleDib(hdcMem, out IntPtr hBitmap, out IntPtr bits)) return false;
                    IntPtr oldBitmap = SelectObject(hdcMem, hBitmap);
                    try
                    {
                        SetStretchBltMode(hdcMem, StretchBltColorOnColor);
                        int[] pixels = PixelsBuffer;
                        int[] histogram = HistogramBuffer;
                        Array.Clear(histogram);
                        long total = 0;

                        bool sampled =
                            AccumulateBand(hdcMem, hdcScreen, bits, pixels, histogram, vsLeft, vsTop, vsRight, vsBottom, x, y - EdgeThickness, width, EdgeThickness, ref total) &&
                            AccumulateBand(hdcMem, hdcScreen, bits, pixels, histogram, vsLeft, vsTop, vsRight, vsBottom, x, y + height, width, EdgeThickness, ref total) &&
                            AccumulateBand(hdcMem, hdcScreen, bits, pixels, histogram, vsLeft, vsTop, vsRight, vsBottom, x - EdgeThickness, y, EdgeThickness, height, ref total) &&
                            AccumulateBand(hdcMem, hdcScreen, bits, pixels, histogram, vsLeft, vsTop, vsRight, vsBottom, x + width, y, EdgeThickness, height, ref total);
                        if (!sampled || total == 0) return false;

                        long half = total / 2;
                        long cumulative = 0;
                        int median = 255;
                        for (int value = 0; value < 256; value++)
                        {
                            cumulative += histogram[value];
                            if (cumulative > half)
                            {
                                median = value;
                                break;
                            }
                        }
                        luminance = median;
                        return true;
                    }
                    finally
                    {
                        SelectObject(hdcMem, oldBitmap);
                        DeleteObject(hBitmap);
                    }
                }
                finally
                {
                    DeleteDC(hdcMem);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcScreen);
            }
        }

        private static bool AccumulateBand(
            IntPtr hdcMem, IntPtr hdcScreen, IntPtr bits, int[] pixels, int[] histogram,
            int vsLeft, int vsTop, int vsRight, int vsBottom,
            int sx, int sy, int sw, int sh, ref long total)
        {
            int cx = Math.Max(sx, vsLeft);
            int cy = Math.Max(sy, vsTop);
            int right = Math.Min(sx + sw, vsRight);
            int bottom = Math.Min(sy + sh, vsBottom);
            if (right <= cx || bottom <= cy) return true;
            if (!StretchBlt(hdcMem, 0, 0, SampleSize, SampleSize, hdcScreen, cx, cy, right - cx, bottom - cy, RasterOpSrcCopy)) return false;
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            for (int i = 0; i < pixels.Length; i++)
            {
                int pixel = pixels[i];
                int r = (pixel >> 16) & 0xFF;
                int g = (pixel >> 8) & 0xFF;
                int b = pixel & 0xFF;
                histogram[(r * 299 + g * 587 + b * 114) / 1000]++;
                total++;
            }
            return true;
        }

        private static bool TryCreateSampleDib(IntPtr hdcMem, out IntPtr hBitmap, out IntPtr bits)
        {
            hBitmap = IntPtr.Zero;
            bits = IntPtr.Zero;
            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = SampleSize;
            bmi.bmiHeader.biHeight = -SampleSize;   // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;          // 0xAARRGGBB
            hBitmap = CreateDIBSection(hdcMem, ref bmi, DibRgbColors, out bits, IntPtr.Zero, 0);
            return hBitmap != IntPtr.Zero && bits != IntPtr.Zero;
        }
    }
}
