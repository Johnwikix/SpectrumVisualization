using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Vanara.PInvoke;
using WinExSpectrumTest.View;
using WinRT.Interop;
using WinUIEx;
using Window = Microsoft.UI.Xaml.Window;

namespace WinExSpectrumTest.Helper
{
    /// <summary>
    /// 窗口辅助。锁定态实现与 music_player DesktopLyrics 同源：
    /// WS_EX_LAYERED 进锁定态一次性设置常驻（运行期反复增删会与 DWM 分层合成竞态），
    /// 穿透开关只切 WS_EX_TRANSPARENT；锁定用 GWL_STYLE 移除 Caption|ThickFrame 位 +
    /// OR-in WS_POPUP（WinUIEx ToggleWindowStyle 含 SWP_FRAMECHANGED）移除边框与标题栏，
    /// 并同步 OverlappedPresenter 簿记防止其重放 WS_THICKFRAME。
    /// </summary>
    public class WindowHelper
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int SW_SHOWNOACTIVATE = 4;
        private static readonly IntPtr HWND_TOPMOST = new(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint LWA_ALPHA = 0x00000002;

        private static readonly Dictionary<IntPtr, WindowStyle> _originalWindowStyles = [];
        private static readonly Dictionary<IntPtr, bool> _originalTopmostStates = [];
        private static List<object> _activeWindows = [];
        private static readonly Dictionary<IntPtr, (double X, double Y, double Width, double Height)> _originalWindowBounds = [];

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        /// <summary>一次性设置 WS_EX_LAYERED + 不透明属性（幂等）。LAYERED 必须常驻，
        /// 穿透开关 <see cref="SetClickThrough"/> 只切 WS_EX_TRANSPARENT。</summary>
        public static void EnsureLayered(IntPtr hWnd)
        {
            int exStyle = (int)GetWindowLongPtr(hWnd, GWL_EXSTYLE);
            if ((exStyle & WS_EX_LAYERED) != 0) return;
            _ = SetWindowLongPtr(hWnd, GWL_EXSTYLE, (IntPtr)(exStyle | WS_EX_LAYERED));
            _ = SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);
        }

        /// <summary>整窗点击穿透开关（只切 WS_EX_TRANSPARENT，需先 EnsureLayered）。</summary>
        public static void SetClickThrough(IntPtr hWnd, bool enable)
        {
            int exStyle = (int)GetWindowLongPtr(hWnd, GWL_EXSTYLE);
            exStyle = enable ? exStyle | WS_EX_TRANSPARENT : exStyle & ~WS_EX_TRANSPARENT;
            _ = SetWindowLongPtr(hWnd, GWL_EXSTYLE, (IntPtr)exStyle);
        }

        /// <summary>锁定窗自愈：不抢焦点地恢复显示并重申置顶（锁定窗不进任务栏/Alt-Tab，
        /// 被前后台切换偶发置为不可见/最小化时无恢复入口）。</summary>
        public static void RestoreOverlay(IntPtr hWnd)
        {
            _ = ShowWindow(hWnd, SW_SHOWNOACTIVATE);
            _ = SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        /// <summary>窗口不在置顶层时幂等重申置顶（不动位置/尺寸/焦点）。</summary>
        public static void EnsureTopmost(IntPtr hWnd)
        {
            if (((int)GetWindowLongPtr(hWnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0) return;
            _ = SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public static bool IsWindowVisibleWindow(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            return IsWindowVisible(hwnd);
        }

        public static bool IsWindowMinimized(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            return IsIconic(hwnd);
        }

        public static void Enable(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);

            // 记忆原TopMost状态
            if (!_originalTopmostStates.ContainsKey(hwnd))
                _originalTopmostStates[hwnd] = window.GetIsAlwaysOnTop();

            // 设置窗口置顶
            window.SetIsAlwaysOnTop(true);

            window.SetIsShownInSwitchers(false);
        }

        public static void Disable(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);

            // 恢复TopMost状态
            if (_originalTopmostStates.TryGetValue(hwnd, out var wasTopMost))
            {
                window.SetIsAlwaysOnTop(wasTopMost);
                _originalTopmostStates.Remove(hwnd);
            }
            window.SetIsAlwaysOnTop(false);
            window.SetIsShownInSwitchers(true);
        }

        public static void SetLock(Window window, bool enable)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            if (enable)
            {
                // LAYERED 常驻；穿透默认开（按钮组悬停时由轮询临时取消）
                EnsureLayered(hwnd);
                SetClickThrough(hwnd, true);

                if (!_originalWindowStyles.ContainsKey(hwnd))
                    _originalWindowStyles[hwnd] = window.GetWindowStyle();
                // 移除标题栏/边框位 + OR-in WS_POPUP：整窗无边框（含 SWP_FRAMECHANGED 立即重算）
                window.ToggleWindowStyle(false, WindowStyle.Caption | WindowStyle.ThickFrame);
                window.ToggleWindowStyle(true, WindowStyle.Popup | WindowStyle.Visible);
                // presenter 意图同步为无边框，防止其簿记在后续事件中重放 WS_THICKFRAME
                if (window.AppWindow.Presenter is OverlappedPresenter presenter)
                    presenter.SetBorderAndTitleBar(false, false);
            }
            else
            {
                SetClickThrough(hwnd, false);
                if (_originalWindowStyles.TryGetValue(hwnd, out var style))
                {
                    window.SetWindowStyle(style);
                    _originalWindowStyles.Remove(hwnd);
                }
                if (window.AppWindow.Presenter is OverlappedPresenter presenter)
                    presenter.SetBorderAndTitleBar(true, false);
            }
        }

        public static void OpenWindow<T>()
        {
            var window = _activeWindows.Find(w => w is T);
            if (window == null)
            {
                if (typeof(T) == typeof(SettingWindow))
                {
                    window = new SettingWindow();
                    TrackWindow(window);
                    var castedWindow = (Window)window;
                    castedWindow.Restore();
                    castedWindow.Activate();
                }

            }
            else
            {
                var castedWindow = (Window)window;
                castedWindow.Restore();
                castedWindow.Activate();
                castedWindow.SetForegroundWindow();
            }
        }

        private static void TrackWindow(object window)
        {
            if (!_activeWindows.Contains(window))
            {
                _activeWindows.Add(window);
                var castedWindow = (Window)window;
                castedWindow.Closed += WindowHelper_Closed;
            }
        }

        private static void WindowHelper_Closed(object sender, WindowEventArgs args)
        {
            if (_activeWindows.Contains(sender))
            {
                _activeWindows.Remove(sender);
            }
        }

        public static IntPtr GetWindowHandle(Window window)
        {
            return WindowNative.GetWindowHandle(window);
        }

        public static void EnableClickThrough(IntPtr hwnd)
        {
            SetClickThrough(hwnd, true);
        }

        public static void DisableClickThrough(IntPtr hwnd)
        {
            SetClickThrough(hwnd, false);
        }

        public static bool IsWindowVisible(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            return IsWindowVisible(hwnd);
        }

        public static bool IsIconic(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            return IsIconic(hwnd);
        }

        public static void ShowWindow(Window window, ShowWindowCommand nCmdShow)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            User32.ShowWindow(hwnd, nCmdShow);
        }

        public static bool SetForegroundWindow(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            return User32.SetForegroundWindow(hwnd);
        }
    }
}
