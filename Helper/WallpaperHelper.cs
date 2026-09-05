using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using WinUIEx;

namespace WinExSpectrumTest.Helper
{
    /// <summary>
    /// Wallpaper mode: turns the app into a borderless, click-through window pinned
    /// to the bottom of the z-order (HWND_BOTTOM), so it renders behind every
    /// regular window like a live wallpaper; clicking the desktop or Win+D reveals
    /// it again. WorkerW/SetParent reparenting is deliberately NOT used: WinUIEx's
    /// window-message subclassing crashes with a NullReferenceException once the
    /// HWND is reparented.
    /// </summary>
    public static class WallpaperHelper
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const int SW_SHOWNA = 8;

        private static (int W, int H) _originalSize = (512, 512);

        /// <summary>Whether the window is currently docked to the wallpaper layer.</summary>
        public static bool IsAttached { get; private set; }

        public static void Enter(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);

            if (!IsAttached)
            {
                _originalSize = (window.AppWindow.Size.Width, window.AppWindow.Size.Height);
            }

            // Borderless window covering the whole monitor.
            window.ToggleWindowStyle(true, WindowStyle.Popup | WindowStyle.Visible);
            DisplayArea area = DisplayArea.Primary;
            SetWindowPos(hwnd, IntPtr.Zero, area.WorkArea.X, area.WorkArea.Y,
                area.WorkArea.Width, area.WorkArea.Height, SWP_FRAMECHANGED | SWP_NOACTIVATE);

            // Tool window (no alt-tab), never activated, fully click-through.
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            _ = SetWindowLong(hwnd, GWL_EXSTYLE,
                exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT);

            // Pin to the bottom of the z-order (HWND_BOTTOM = 1).
            SetWindowPos(hwnd, new IntPtr(1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            _ = ShowWindow(hwnd, SW_SHOWNA);

            IsAttached = true;
        }

        public static void Exit(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);

            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            _ = SetWindowLong(hwnd, GWL_EXSTYLE,
                exStyle & ~WS_EX_TOOLWINDOW & ~WS_EX_NOACTIVATE & ~WS_EX_TRANSPARENT);

            window.ToggleWindowStyle(false, WindowStyle.Popup | WindowStyle.Visible);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, _originalSize.W, _originalSize.H,
                SWP_NOMOVE | SWP_FRAMECHANGED | SWP_NOACTIVATE);

            IsAttached = false;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}
