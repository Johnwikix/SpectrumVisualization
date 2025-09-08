using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using System;
using WinExSpectrumTest.ViewModel;
using WinUIEx;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest.View
{
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingWindow : WindowEx
    {
        public SettingViewModel ViewModel { get; }
        public SettingWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            ViewModel = App.Services.GetRequiredService<SettingViewModel>();
            this.AppWindow.Closing += AppWindow_Closing;
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            ViewModel.SaveSettings();
        }

        private bool GetIsChecked(string style, string currentOption)
        {
            if (style == currentOption)
                return true;
            else
                return false;
        }
    }
}
