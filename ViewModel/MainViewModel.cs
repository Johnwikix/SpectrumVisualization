using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

/// <summary>
/// 主窗口标题栏的命令与装饰状态（最大化 / 全屏图标）。
/// 窗口实现（presenter、拖动区、锁定轮询、壁纸宿主）仍在 MainWindow；
/// 命令按项目既有约定经 App.MainWindow 回跳，状态由窗口在变化时推送进来。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private bool _isMaximized;
    /// <summary>是否最大化；由窗口按 presenter 状态推送，驱动图标绑定。</summary>
    public bool IsMaximized
    {
        get => _isMaximized;
        internal set
        {
            if (SetProperty(ref _isMaximized, value)) OnPropertyChanged(nameof(MaximizeGlyph));
        }
    }

    public string MaximizeGlyph => IsMaximized ? "\uE744" : "\uE71A";

    private bool _isFullScreen;
    /// <summary>是否全屏；由窗口在进入 / 退出全屏时推送，驱动图标绑定。</summary>
    public bool IsFullScreen
    {
        get => _isFullScreen;
        internal set
        {
            if (SetProperty(ref _isFullScreen, value)) OnPropertyChanged(nameof(FullScreenGlyph));
        }
    }

    public string FullScreenGlyph => IsFullScreen ? "\uE73F" : "\uE740";

    public MainViewModel()
    {
        // 壁纸模式期间禁用全屏入口；与 MainWindow.ToggleFullScreen 内的守卫一致。
        AppSettings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged(string name)
    {
        if (name != nameof(AppSettings.WallpaperEnabled)) return;
        if (_dispatcher.HasThreadAccess)
        {
            ToggleFullScreenCommand.NotifyCanExecuteChanged();
            return;
        }
        _dispatcher.TryEnqueue(ToggleFullScreenCommand.NotifyCanExecuteChanged);
    }

    [RelayCommand]
    private void Minimize() => App.MainWindow?.MinimizeWindow();

    [RelayCommand]
    private void ToggleMaximize() => App.MainWindow?.ToggleMaximize();

    [RelayCommand]
    private void ExitApplication() => App.Current_Exit();

    private bool CanToggleFullScreen() => !AppSettings.WallpaperEnabled;

    [RelayCommand(CanExecute = nameof(CanToggleFullScreen))]
    private void ToggleFullScreen() => App.MainWindow?.ToggleFullScreen();

    [RelayCommand]
    private void ToggleLock() => App.MainWindow?.ToggleLock();
}
