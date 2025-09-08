using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
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
        }

        [RelayCommand]
        public void ShowMainWindow()
        {
            if (App.MainWindow == null) return;
            if (!AppSettings.IsLocked)
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
            WindowHelper.SetLock(window, false);
            WindowHelper.Disable(window);
            window.SetAppStyle();
            AppSettings.IsLocked = false;
        }

        [RelayCommand]
        public void ExitApplication()
        {
            App.Current_Exit();
        }
    }
}
