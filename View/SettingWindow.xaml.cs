using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using WinExSpectrumTest.ViewModel;
using WinUIEx;

// To learn more about WinUI, visit the WinUI project structure,
// and more about WinUI project templates, see: http://aka.ms/winui-project-info.

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
            ViewModel = App.Services.GetRequiredService<SettingViewModel>();
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/icon.ico");
            if (File.Exists(iconPath))
            {
                this.SetIcon(iconPath);
            }
            this.AppWindow.Closing += AppWindow_Closing;
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            // 同步落盘：窗口关闭后异步写入可能被随后的应用退出终止。
            ViewModel.PersistSettingsCommand.Execute(null);
        }

        private async void OnBrowseLyricsCacheClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            button.IsEnabled = false;
            try
            {
                string? path;
                try
                {
                    var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
                    path = (await picker.PickSingleFolderAsync())?.Path;
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or NotSupportedException)
                {
                    var picker = new Windows.Storage.Pickers.FolderPicker();
                    picker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    path = (await picker.PickSingleFolderAsync())?.Path;
                }
                if (path is not null) ViewModel.LyricsCacheFolder = path;
            }
            catch (Exception ex)
            {
                App.WriteCrashLog("Lyrics cache folder picker", ex.Message, ex);
                if (Content?.XamlRoot is { } root)
                {
                    var resources = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
                    await new ContentDialog
                    {
                        XamlRoot = root, Title = resources.GetString("LyricsCachePickerFailed"),
                        Content = ex.Message, CloseButtonText = resources.GetString("LyricsCachePickerClose")
                    }.ShowAsync();
                }
            }
            finally { button.IsEnabled = true; }
        }

        /// <summary>重置是破坏性操作，先经确认对话框再执行 ViewModel 命令。</summary>
        private async void OnResetSettingsClick(object sender, RoutedEventArgs e)
        {
            if (Content?.XamlRoot is null) return;
            var resources = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = resources.GetString("ResetSettingsDialogTitle"),
                Content = resources.GetString("ResetSettingsDialogMessage"),
                PrimaryButtonText = resources.GetString("ResetSettingsDialogConfirm"),
                CloseButtonText = resources.GetString("ResetSettingsDialogCancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.ResetSettingsCommand.ExecuteAsync(null);
            }
        }
    }
}
