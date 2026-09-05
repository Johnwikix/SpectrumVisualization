using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using Windows.UI.ViewManagement;
using WinExSpectrumTest.Backdrop;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Model;
using WinRT.Interop;
using WinUIEx;

namespace WinExSpectrumTest
{
    public sealed partial class MainWindow : WinUIEx.WindowEx
    {
        private IntPtr _hwnd;
        private ThemeStyleHelper themeStyleHelper;
        private bool IsFullScreen = false;
        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/icon.ico");
            if (File.Exists(iconPath))
            {
                this.SetIcon(iconPath);
            }
            this.AppWindow.Closing += AppWindow_Closing;
            _hwnd = WindowNative.GetWindowHandle(this);
            themeStyleHelper = new ThemeStyleHelper(this, this.AppWindow);
            themeStyleHelper.SetAppStyle();
            if (AppSettings.WallpaperMode)
            {
                // Persisted wallpaper mode: re-attach directly on launch.
                themeStyleHelper.SetTransparent();
                this.SetIsShownInSwitchers(false);
                WallpaperHelper.Enter(this);
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            App.Current_Exit();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            TopCommand.Opacity = 0;
            themeStyleHelper?.SetTransparent();
            WindowHelper.Enable(this);
            WindowHelper.SetLock(this, true);
            AppSettings.IsLocked = true;
        }

        private void TopCommand_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (!AppSettings.IsLocked && !AppSettings.WallpaperMode)
            {
                TopCommand.Opacity = 1;
            }
        }

        private void TopCommand_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!AppSettings.IsLocked && !AppSettings.WallpaperMode)
            {
                TopCommand.Opacity = 0;
            }
        }

        private void MyMainwindow_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (AppSettings.IsLocked && !AppSettings.WallpaperMode)
            {
                WindowHelper.EnableClickThrough(_hwnd);
            }
        }

        private void MyMainwindow_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (AppSettings.IsLocked && !AppSettings.WallpaperMode)
            {
                WindowHelper.DisableClickThrough(_hwnd);
            }
        }

        public void ChangeRefreshRate()
        {
            MyCanvas.ChangeRefreshRate();
        }

        public void SetAppStyle()
        {
            themeStyleHelper.SetAppStyle();
        }

        public void SetCustomAppStyle()
        {
            themeStyleHelper.ChangeCustomAcrylicStyle();
        }

        public void SetAppTheme()
        {
            themeStyleHelper.SetAppStyle();
            themeStyleHelper.SetAppTheme();
        }

        /// <summary>Whether the window is currently docked to the wallpaper layer.</summary>
        public bool IsWallpaperModeActive => AppSettings.WallpaperMode;

        /// <summary>Activates a specific effect page by registry id.</summary>
        public void SwitchToEffect(string id)
        {
            MyCanvas.LoadEffect(id);
        }

        /// <summary>Cycles to the next/previous effect page.</summary>
        public void SwitchEffect(int direction)
        {
            MyCanvas.SwitchEffect(direction);
        }

        private void PreviousEffect_Click(object sender, RoutedEventArgs e)
        {
            SwitchEffect(-1);
        }

        private void NextEffect_Click(object sender, RoutedEventArgs e)
        {
            SwitchEffect(1);
        }

        /// <summary>Toggles the desktop wallpaper (WorkerW) display mode.</summary>
        public void ToggleWallpaper()
        {
            if (AppSettings.WallpaperMode)
            {
                WallpaperHelper.Exit(this);
                AppSettings.WallpaperMode = false;
                WindowHelper.Disable(this);
                this.SetIsShownInSwitchers(true);
                themeStyleHelper?.SetAppStyle();
                if (!AppSettings.IsLocked)
                {
                    TopCommand.Opacity = 1;
                }
            }
            else
            {
                TopCommand.Opacity = 0;
                themeStyleHelper?.SetTransparent();
                this.SetIsShownInSwitchers(false);
                WallpaperHelper.Enter(this);
                AppSettings.WallpaperMode = true;
            }
        }

        private void Wallpaper_Click(object sender, RoutedEventArgs e)
        {
            ToggleWallpaper();
        }

        private void FullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (IsFullScreen)
            {
                this.AppWindow.SetPresenter(AppWindowPresenterKind.Default);
                FullScreenIcon.Glyph = "\uE740";
                LockBtn.IsEnabled = true;
            }
            else
            {
                this.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
                FullScreenIcon.Glyph = "\uE73F";
                LockBtn.IsEnabled = false;
            }
            IsFullScreen = !IsFullScreen;
        }
    }
}
