using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.UI.ViewManagement;
using WinExSpectrumTest.Backdrop;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Model;
using WinRT.Interop;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : WinUIEx.WindowEx
    {
        private IntPtr _hwnd;
        private ThemeStyleHelper themeStyleHelper;
        private bool IsFullScreen = false;
        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            this.SetIcon("Assets/icon.ico");
            this.AppWindow.Closing += AppWindow_Closing;
            _hwnd = WindowNative.GetWindowHandle(this);
            themeStyleHelper = new ThemeStyleHelper(this, this.AppWindow);
            themeStyleHelper.SetAppStyle();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            App.Current_Exit();
            //this.Hide();
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
            if (!AppSettings.IsLocked)
            {
                TopCommand.Opacity = 1;
            }
        }

        private void TopCommand_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!AppSettings.IsLocked)
            {
                TopCommand.Opacity = 0;
            }
        }

        private void MyMainwindow_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (AppSettings.IsLocked)
            {
                WindowHelper.EnableClickThrough(_hwnd);
            }
        }

        private void MyMainwindow_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (AppSettings.IsLocked)
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
