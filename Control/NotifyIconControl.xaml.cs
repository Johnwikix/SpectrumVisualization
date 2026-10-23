using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.View;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest.Control
{
    public sealed partial class NotifyIconControl : UserControl
    {
        public ViewModel.SettingViewModel ViewModel { get; } = App.Services.GetRequiredService<ViewModel.SettingViewModel>();
        private bool _disposed;
        public NotifyIconControl()
        {
            InitializeComponent();
            UpdateTrayState();
            AppSettings.Changed += OnSettingsChanged;
        }

        [RelayCommand]
        public void ShowMainWindow()
        {
            App.MainWindow?.ShowFromActivation();
        }

        private void OnSettingsChanged(string propertyName)
        {
            if (_disposed || propertyName is not (nameof(AppSettings.WallpaperEnabled) or nameof(AppSettings.IsLocked))) return;
            if (!DispatcherQueue.HasThreadAccess)
            {
                DispatcherQueue.TryEnqueue(() => OnSettingsChanged(propertyName));
                return;
            }
            UpdateTrayState();
        }

        private void UpdateTrayState()
        {
            Unlock.IsEnabled = AppSettings.IsLocked && !AppSettings.WallpaperEnabled;
        }

        internal void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            AppSettings.Changed -= OnSettingsChanged;
            NotifyIcon.Dispose();
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
        public void ExitApplication()
        {
            App.Current_Exit();
        }
    }
}
