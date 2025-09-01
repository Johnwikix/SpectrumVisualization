using CommunityToolkit.Mvvm.Input;
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
        public void UnlockWindow()
        {
            var window = App.MainWindow;
            if (window == null) return;
            WindowHelper.SetClickThrough(window, false);
        }

        [RelayCommand]
        public void ExitApplication()
        {
            App.Current_Exit();
        }
    }
}
