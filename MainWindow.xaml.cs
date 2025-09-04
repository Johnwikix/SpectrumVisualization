using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Service;
using WinRT.Interop;
using WinUIEx;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

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
        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            this.SetIcon("Assets/icon.ico");
            this.AppWindow.Closing += AppWindow_Closing;
            _hwnd = WindowNative.GetWindowHandle(this);
            InitializeData();
        }

        private async void InitializeData()
        {
            await DataService.Initialize();
            await DataService.LoadSettingAsync();
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            this.Hide();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            TopCommand.Opacity = 0;
            WindowHelper.Enable(this);
            WindowHelper.SetLock(this,true);
            AppSettings.IsLocked = true;
        }

        private void TopCommand_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (!AppSettings.IsLocked) {
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
    }
}
