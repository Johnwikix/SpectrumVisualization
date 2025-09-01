using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Windows.System;
using WinRT.Interop;
using WinUIEx;

namespace WinExSpectrumTest.Helper
{
    public class WindowHelper
    {
        private static readonly Dictionary<IntPtr, WindowStyle> _originalWindowStyles = [];
        public static void SetClickThrough(Window window, bool enable)
        {
            IntPtr hwnd = WindowNative.GetWindowHandle(window);
            int exStyle = User32.GetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE);
            if (enable)
            {
                if (!_originalWindowStyles.ContainsKey(hwnd))
                    _originalWindowStyles[hwnd] = window.GetWindowStyle();
                window.ToggleWindowStyle(true, WindowStyle.Popup | WindowStyle.Visible);
                User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle | (int)User32.WindowStylesEx.WS_EX_TRANSPARENT | (int)User32.WindowStylesEx.WS_EX_LAYERED);
            }
            else
            {
                User32.SetWindowLong(hwnd, User32.WindowLongFlags.GWL_EXSTYLE, exStyle & ~(int)User32.WindowStylesEx.WS_EX_TRANSPARENT);
                if (_originalWindowStyles.TryGetValue(hwnd, out var style))
                {
                    window.SetWindowStyle(style);
                    _originalWindowStyles.Remove(hwnd);
                }
            }
        }
    }
}
