using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.System;
using WinExSpectrumTest.View;
using WinRT.Interop;
using WinUIEx;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Window = Microsoft.UI.Xaml.Window;

namespace WinExSpectrumTest.Helper
{
    public class WindowHelper
    {
        private static readonly Dictionary<IntPtr, WindowStyle> _originalWindowStyles = [];
        private static readonly Dictionary<IntPtr, bool> _originalTopmostStates = [];
        private static List<object> _activeWindows = [];
        private static readonly Dictionary<IntPtr, (double X, double Y, double Width, double Height)> _originalWindowBounds = [];
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
            //int exStyle = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
            if (enable)
            {
                if (!_originalWindowStyles.ContainsKey(hwnd))
                    _originalWindowStyles[hwnd] = window.GetWindowStyle();
                window.ToggleWindowStyle(true, WindowStyle.Popup | WindowStyle.Visible);
                //User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (int)User32.WindowStylesEx.WS_EX_TRANSPARENT | (int)User32.WindowStylesEx.WS_EX_LAYERED);
            }
            else
            {
                //User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle & ~(int)User32.WindowStylesEx.WS_EX_TRANSPARENT);
                if (_originalWindowStyles.TryGetValue(hwnd, out var style))
                {
                    window.SetWindowStyle(style);
                    _originalWindowStyles.Remove(hwnd);
                }
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
            int exStyle = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
            User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (int)User32.WindowStylesEx.WS_EX_TRANSPARENT | (int)User32.WindowStylesEx.WS_EX_LAYERED);
        }

        public static void DisableClickThrough(IntPtr hwnd)
        {
            int exStyle = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
            User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle & ~(int)User32.WindowStylesEx.WS_EX_TRANSPARENT);
        }

        public static bool IsWindowVisible(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            return User32.IsWindowVisible(hwnd);
        }

        public static bool IsIconic(Window window)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            return User32.IsIconic(hwnd);
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
