using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using Vanara.PInvoke;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.View;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest.Control
{
    public sealed partial class NotifyIconControl : UserControl
    {
        public NotifyIconControl()
        {
            InitializeComponent();
            UpdateTrayState();
            AppSettings.Changed += OnSettingsChanged;
        }

        [RelayCommand]
        public void ShowMainWindow()
        {
            if (App.MainWindow == null) return;
            if (!AppSettings.IsLocked && !AppSettings.WallpaperEnabled)
            {
                if (WindowHelper.IsWindowVisible(App.MainWindow))
                {
                    if (WindowHelper.IsIconic(App.MainWindow))
                    {
                        WindowHelper.ShowWindow(App.MainWindow, ShowWindowCommand.SW_RESTORE);
                    }
                    WindowHelper.SetForegroundWindow(App.MainWindow);
                }
                else
                {
                    if (!App.MainWindow.Visible)
                    {
                        App.MainWindow.Show();
                    }
                }
            }
        }

        private void OnSettingsChanged(string propertyName)
        {
            if (propertyName is nameof(AppSettings.WallpaperEnabled) or nameof(AppSettings.IsLocked))
                UpdateTrayState();
        }

        private void UpdateTrayState()
        {
            WallpaperMode.IsChecked = AppSettings.WallpaperEnabled;
            Unlock.IsEnabled = AppSettings.IsLocked && !AppSettings.WallpaperEnabled;
        }

        internal void Dispose()
        {
            AppSettings.Changed -= OnSettingsChanged;
            NotifyIcon.Dispose();
        }

        [RelayCommand]
        private void ToggleWallpaperMode()
        {
            // SecondWindow owns a separate flyout; use the actual window state,
            // not a flyout Opening event or the transient menu check mark.
            App.MainWindow?.SetWallpaperMode(!AppSettings.WallpaperEnabled);
            WallpaperMode.IsChecked = AppSettings.WallpaperEnabled;
        }
        [RelayCommand]
        public void Setting()
        {
            WindowHelper.OpenWindow<SettingWindow>();
        }

        [RelayCommand]
        public void UnlockWindow()
        {
            var window = App.MainWindow;
            if (window == null) return;
            if (AppSettings.IsLocked)
            {
                window.ExitLock();
            }
        }

        [RelayCommand]
        public void CycleEffect()
        {
            App.MainWindow?.SwitchEffect(1);
        }

        [RelayCommand]
        public void ExitApplication()
        {
            App.Current_Exit();
        }
    }
}
