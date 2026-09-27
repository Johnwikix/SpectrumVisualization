using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinExSpectrumTest.Helper;

/// <summary>Conservative full-screen coverage check in physical screen coordinates.
/// Used only in wallpaper mode, on the UI thread; it does not capture screen pixels.</summary>
internal sealed class WallpaperOcclusionDetector
{
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x80000, WsExTransparent = 0x20;
    private readonly IntPtr _wallpaperWindow;
    private readonly EnumWindowProc _visitWindow;
    private readonly StringBuilder _className = new(256);
    private WindowRect _wallpaperBounds;
    private bool _covered;

    public WallpaperOcclusionDetector(IntPtr wallpaperWindow)
    {
        _wallpaperWindow = wallpaperWindow;
        _visitWindow = VisitWindow;
    }

    public bool IsFullyCovered()
    {
        if (!GetWindowRect(_wallpaperWindow, out _wallpaperBounds) ||
            _wallpaperBounds.Right <= _wallpaperBounds.Left || _wallpaperBounds.Bottom <= _wallpaperBounds.Top)
            return false;

        // Win+D can bring the desktop above windows that still report WS_VISIBLE.
        if (IsDesktopClass(GetForegroundWindow())) return false;
        _covered = false;
        EnumWindows(_visitWindow, IntPtr.Zero);
        return _covered;
    }

    private bool VisitWindow(IntPtr window, IntPtr parameter)
    {
        if (window == _wallpaperWindow || !IsWindowVisible(window) || IsIconic(window)) return true;
        if ((GetWindowLongPtr(window, GwlExStyle).ToInt64() & (WsExLayered | WsExTransparent)) != 0) return true;
        // Includes windows on another virtual desktop and hidden UWP hosts.
        if (DwmGetWindowAttribute(window, 14 /* DWMWA_CLOAKED */, out int cloaked, sizeof(int)) != 0 || cloaked != 0)
            return true;
        // DWM excludes invisible resize borders, unlike GetWindowRect. If the
        // visible bounds cannot be read, keep rendering rather than guess.
        if (DwmGetWindowAttribute(window, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out WindowRect bounds,
                Marshal.SizeOf<WindowRect>()) != 0 || !Covers(bounds, _wallpaperBounds)) return true;
        if (IsDesktopClass(window)) return true;
        GetClassName(window, _className, _className.Capacity);
        string name = _className.ToString();
        if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
        // A shaped window may have holes even when its bounding box is full-screen.
        int regionType = GetWindowRgnBox(window, out WindowRect region);
        if (regionType is 1 or 3 /* NULLREGION / COMPLEXREGION */) return true;
        if (regionType == 2 /* SIMPLEREGION */)
        {
            if (!GetWindowRect(window, out WindowRect windowBounds)) return true;
            region.Left += windowBounds.Left;
            region.Right += windowBounds.Left;
            region.Top += windowBounds.Top;
            region.Bottom += windowBounds.Top;
            if (!Covers(region, _wallpaperBounds)) return true;
        }
        _covered = true;
        return false;
    }

    private bool IsDesktopClass(IntPtr window)
    {
        if (window == IntPtr.Zero || GetClassName(window, _className, _className.Capacity) == 0) return false;
        return _className.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView";
    }

    internal static bool Covers(WindowRect window, WindowRect wallpaper) =>
        wallpaper.Right > wallpaper.Left && wallpaper.Bottom > wallpaper.Top &&
        window.Left <= wallpaper.Left && window.Top <= wallpaper.Top &&
        window.Right >= wallpaper.Right && window.Bottom >= wallpaper.Bottom;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);
    [DllImport("user32.dll")]
    private static extern int GetWindowRgnBox(IntPtr window, out WindowRect bounds);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out WindowRect value, int size);
}
