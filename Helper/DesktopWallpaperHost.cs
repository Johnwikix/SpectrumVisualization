using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Helper;

/// <summary>
/// Embeds a window below Explorer's icons, on the primary monitor (including the
/// taskbar area). Does not change the user's wallpaper file or Explorer's styles.
/// All calls must run on the window's UI thread.
/// </summary>
internal sealed class DesktopWallpaperHost
{
    private const int GwlStyle = -16, GwlExStyle = -20;
    private const long WsChild = 0x40000000, WsPopup = 0x80000000;
    private const long WindowChrome = 0x00CF0000; // caption, sizing frame, system menu, min/max boxes
    private const long WsExAppWindow = 0x00040000, WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000, WsExTransparent = 0x20;
    private const long WsExTopmost = 0x8, WsExNoRedirectionBitmap = 0x00200000;
    private const uint SwpNoActivate = 0x10, SwpFrameChanged = 0x20, SwpShowWindow = 0x40;
    private readonly IntPtr _window;
    private readonly IntPtr _originalParent;
    private readonly long _style, _exStyle;
    private readonly Rect _bounds;
    private IntPtr _desktop, _icons;

    public DesktopWallpaperHost(IntPtr window)
    {
        _window = window;
        _originalParent = GetParent(window);
        _style = GetWindowLongPtr(window, GwlStyle).ToInt64();
        _exStyle = GetWindowLongPtr(window, GwlExStyle).ToInt64();
        Check(GetWindowRect(window, out _bounds));
    }

    public void Attach()
    {
        (_desktop, _icons) = FindDesktop();
        if (_desktop == IntPtr.Zero)
            throw new InvalidOperationException("Explorer's wallpaper host is unavailable.");

        // SetParent does not change WS_CHILD/WS_POPUP. Explicitly remove topmost
        // before parenting, so the locked-widget policy cannot leak onto the desktop.
        Check(SetWindowPos(_window, new IntPtr(-2), 0, 0, 0, 0, 0x13));
        SetStyle(GwlStyle, (_style & ~(WsPopup | WindowChrome)) | WsChild);
        SetStyle(GwlExStyle, (_exStyle & ~(WsExAppWindow | WsExTopmost)) |
            WsExToolWindow | WsExNoActivate | WsExTransparent);
        WindowHelper.EnsureLayered(_window);
        ChangeParent(_desktop);
        FitPrimaryMonitor(force: true);
    }

    /// <summary>Handle primary-display changes and desktop host recreation.</summary>
    public void Refresh()
    {
        if (!IsWindow(_desktop) || GetParent(_window) != _desktop ||
            (_icons != IntPtr.Zero && !IsWindow(_icons)))
        {
            Attach();
            return;
        }
        FitPrimaryMonitor(force: false);
    }

    public void Restore()
    {
        // Restore parenting before removing WS_CHILD, as required by SetParent.
        ChangeParent(_originalParent);
        SetStyle(GwlStyle, _style);
        SetStyle(GwlExStyle, _exStyle);
        Check(SetWindowPos(_window, (_exStyle & WsExTopmost) != 0 ? new IntPtr(-1) : new IntPtr(-2),
            _bounds.Left, _bounds.Top, _bounds.Right - _bounds.Left, _bounds.Bottom - _bounds.Top,
            SwpNoActivate | SwpFrameChanged | SwpShowWindow));
        _desktop = IntPtr.Zero;
    }

    private void FitPrimaryMonitor(bool force)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        Check(GetMonitorInfo(MonitorFromPoint(default, 1 /* MONITOR_DEFAULTTOPRIMARY */), ref info));
        Rect bounds = info.Monitor;
        // Also compare the actual screen rectangle: moving a secondary monitor
        // changes Progman's origin even when the primary monitor is unchanged.
        Check(GetWindowRect(_window, out Rect current));
        bool correctLayer = _icons == IntPtr.Zero || GetWindow(_window, 3 /* GW_HWNDPREV */) == _icons;
        if (!force && bounds.Equals(current) && correctLayer) return;

        // Explorer's host can start at a negative virtual-screen coordinate.
        // Convert the physical primary-monitor origin to parent-client coordinates.
        var origin = new WindowHelper.POINT { X = bounds.Left, Y = bounds.Top };
        Check(ScreenToClient(_desktop, ref origin));
        Check(SetWindowPos(_window, _icons, origin.X, origin.Y,
            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
            SwpNoActivate | SwpFrameChanged | SwpShowWindow));
    }

    private static (IntPtr Host, IntPtr Icons) FindDesktop()
    {
        IntPtr progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return default;

        // Explorer's private desktop-layer request. Bound the wait so an unresponsive
        // shell cannot hang a tray click. Unsupported shells fall back to the widget.
        if (SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1),
                0x2 /* SMTO_ABORTIFHUNG */, 1000, out _) == IntPtr.Zero)
            return default;

        IntPtr icons = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (icons != IntPtr.Zero &&
            (GetWindowLongPtr(progman, GwlExStyle).ToInt64() & WsExNoRedirectionBitmap) != 0)
        {
            // New Windows 11: icons and wallpaper are siblings inside Progman.
            // Insert our layered child immediately behind the icon view.
            return (progman, icons);
        }

        IntPtr host = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                return true;
            IntPtr candidate = FindWindowEx(IntPtr.Zero, window, "WorkerW", null);
            if (candidate == IntPtr.Zero ||
                FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                return true;
            host = candidate;
            return false;
        }, IntPtr.Zero);
        return (host, IntPtr.Zero);
    }

    private void ChangeParent(IntPtr parent)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = SetParent(_window, parent);
        int error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error);
        // While WS_CHILD is still set, SetParent(NULL) reports the desktop HWND
        // through GetParent. Restore clears WS_CHILD immediately afterwards.
        if (parent != IntPtr.Zero && GetParent(_window) != parent)
            throw new InvalidOperationException("The wallpaper window was not reparented.");
    }

    private void SetStyle(int index, long style)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr previous = SetWindowLongPtr(_window, index, new IntPtr(style));
        int error = Marshal.GetLastPInvokeError();
        if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error);
    }

    private static void Check(bool success)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect : IEquatable<Rect>
    {
        public int Left, Top, Right, Bottom;
        public readonly bool Equals(Rect other) => Left == other.Left && Top == other.Top &&
            Right == other.Right && Bottom == other.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor, Work;
        public uint Flags;
    }

    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam,
        IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ScreenToClient(IntPtr hwnd, ref WindowHelper.POINT point);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(WindowHelper.POINT point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
