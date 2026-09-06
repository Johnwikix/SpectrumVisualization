using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using WinUIEx;

namespace WinExSpectrumTest.Helper
{
    /// <summary>
    /// Wallpaper mode (Wallpaper Engine / Lively 同款实现)：
    /// 1. 向 Progman 发送未公开消息 0x052C，令 shell 在桌面图标层（SHELLDLL_DefView）
    ///    之后再生成一个 WorkerW 窗口；
    /// 2. 枚举找到"不含 SHELLDLL_DefView 的那个 WorkerW"（桌面图标下方的那层）；
    /// 3. SetParent 把本窗口挂为该 WorkerW 的子窗口 —— 于是壁纸渲染在桌面图标
    ///    （SHELLDLL_DefView）之下，图标不会被遮挡；
    /// 4. WS_EX_LAYERED | WS_EX_TRANSPARENT 让鼠标点击穿透到桌面/图标。
    /// HWND_BOTTOM 压底方案（旧实现）做不到这两点：top-level 窗口永远盖在图标层
    /// 之上且抢占鼠标。
    /// </summary>
    public static class WallpaperHelper
    {
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const int SW_HIDE = 0;
        private const int SW_SHOWNA = 8;
        private const int SMTO_NORMAL = 0x0000;
        private const uint LWA_ALPHA = 0x0002;

        private static (int W, int H) _originalSize = (512, 512);
        private static IntPtr _workerW;
        private static int _savedStyle;
        private static int _savedExStyle;

        /// <summary>Whether the window is currently docked to the wallpaper layer.</summary>
        public static bool IsAttached { get; private set; }

        public static void Enter(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);

            if (!IsAttached)
            {
                var aw = window.AppWindow;
                if (aw != null)
                {
                    _originalSize = (aw.Size.Width, aw.Size.Height);
                }
                _savedStyle = GetWindowLong(hwnd, GWL_STYLE);
                _savedExStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            }

            // 仍为 top-level 时完成所有 AppWindow/样式调整（子窗口状态下这些 API 无效）。
            if (window.AppWindow?.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(false, false);
            }
            window.ToggleWindowStyle(true, WindowStyle.Popup | WindowStyle.Visible);
            DisplayArea area = DisplayArea.Primary;
            SetWindowPos(hwnd, IntPtr.Zero, area.WorkArea.X, area.WorkArea.Y,
                area.WorkArea.Width, area.WorkArea.Height, SWP_FRAMECHANGED | SWP_NOACTIVATE);

            // Tool window（不进 alt-tab）+ 分层透明（保持完全不透明）+ 鼠标穿透。
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            _ = SetWindowLong(hwnd, GWL_EXSTYLE,
                exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED | WS_EX_TRANSPARENT);
            _ = SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);

            AttachToWorkerW(hwnd, area.WorkArea.Width, area.WorkArea.Height);
            IsAttached = true;
        }

        /// <summary>确保窗口仍是 WorkerW 子窗口（explorer 重启会销毁原 WorkerW，需重新附着）。</summary>
        public static void EnsureAttached(IntPtr hwnd)
        {
            if (!IsAttached) return;
            if (_workerW != IntPtr.Zero && GetAncestor(hwnd, GA_PARENT) == _workerW) return;
            if (!IsWindow(hwnd)) return;
            DisplayArea area = DisplayArea.Primary;
            if (TrySpawnAndFindWorkerW())
            {
                AttachToWorkerW(hwnd, area.WorkArea.Width, area.WorkArea.Height);
            }
        }

        private static void AttachToWorkerW(IntPtr hwnd, int width, int height)
        {
            if (!TrySpawnAndFindWorkerW())
            {
                return;
            }

            // 隐藏 → 手术 → 挂靠 → 定位 → 显示：避免窗口以"半子窗口"状态接收
            // 绘制/输入回调（0xc000041d 用户回调异常的根源之一）。
            _ = ShowWindow(hwnd, SW_HIDE);

            // 掩码式样式修改：只清 WS_POPUP|WS_CAPTION|WS_THICKFRAME、加
            // WS_CHILD|WS_CLIPSIBLINGS，保留其余位（整样式覆写会砸掉
            // WS_CLIPCHILDREN 等交换链依赖的位）。
            int style = GetWindowLong(hwnd, GWL_STYLE);
            _ = SetWindowLong(hwnd, GWL_STYLE,
                (style & ~(WS_POPUP | WS_CAPTION | WS_THICKFRAME)) | WS_CHILD | WS_CLIPSIBLINGS);

            _ = SetParent(hwnd, _workerW);

            // 子窗口坐标相对父（WorkerW）客户区原点
            _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, width, height, SWP_NOACTIVATE | SWP_FRAMECHANGED);
            _ = ShowWindow(hwnd, SW_SHOWNA);
        }

        /// <summary>0x052C 触发 shell 生成图标层下方的 WorkerW 并枚举定位它。</summary>
        private static bool TrySpawnAndFindWorkerW()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                _ = SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero,
                    SMTO_NORMAL, 1000, out _);
            }

            _workerW = IntPtr.Zero;
            EnumWindows((topLevel, _) =>
            {
                // 桌面图标层 SHELLDLL_DefView 所在的 top-level（Progman 或首个 WorkerW）
                if (FindWindowEx(topLevel, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    // 其后同级的下一个 WorkerW 就是壁纸层
                    _workerW = FindWindowEx(IntPtr.Zero, topLevel, "WorkerW", null);
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (_workerW != IntPtr.Zero) return true;

            // 兜底：某些 shell 状态下 0x052C 不生成第二个 WorkerW，
            // 直接挂到承载图标层的窗口（Progman）上，图标仍在子层之上。
            _workerW = progman != IntPtr.Zero ? progman : GetDesktopWindow();
            return _workerW != IntPtr.Zero;
        }

        public static void Exit(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);

            // 同 Enter 的顺序原则：先隐藏，脱离父级并整体恢复保存的样式，再重设尺寸
            _ = ShowWindow(hwnd, SW_HIDE);
            _ = SetParent(hwnd, IntPtr.Zero);
            _ = SetWindowLong(hwnd, GWL_STYLE, _savedStyle);
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, _savedExStyle);

            window.ToggleWindowStyle(false, WindowStyle.Popup | WindowStyle.Visible);
            if (window.AppWindow?.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
            }
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, _originalSize.W, _originalSize.H,
                SWP_NOMOVE | SWP_FRAMECHANGED | SWP_NOACTIVATE);
            _ = ShowWindow(hwnd, SW_SHOWNA);

            IsAttached = false;
            _workerW = IntPtr.Zero;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        private const uint GA_PARENT = 1;

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
    }
}
