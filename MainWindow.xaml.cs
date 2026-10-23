using WinExSpectrumTest.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using Windows.Graphics;
using WinExSpectrumTest.Backdrop;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Model;
using WinRT.Interop;
using WinUIEx;

namespace WinExSpectrumTest
{
    // 基类与 music_player 一致使用 WinUIEx.WindowEx：基类构造阶段即完成 AppWindow
    // 初始化；SetIsShownInSwitchers / ToggleWindowStyle 等 WinUIEx 扩展方法同样可用。
    public sealed partial class MainWindow : WindowEx
    {
        private IntPtr _hwnd;
        private ThemeStyleHelper themeStyleHelper;
        private bool IsFullScreen = false;
        private bool _restoreFullScreenTitleBarExtension;
        private Thickness _restoreFullScreenVisualizerMargin;
        // WindowEx 基类构造中 WindowManager 已订阅 AppWindow 事件，此引用必定非 null：
        // 缓存后全程只用字段。
        private readonly AppWindow _appWindow;
        private bool _dragRegionRegistered;

        // 锁定态光标轮询（DesktopLyrics 同款两档）：
        // 慢轮询 200ms 做进窗检测与自愈；悬停窗口期间切 50ms 快轮询保证按钮跟手；
        // 光标移到标题栏按钮区上才临时取消穿透供点击，离开立即恢复穿透。
        private const double HoverPollingIntervalMs = 50;
        private const double IdlePollingIntervalMs = 200;
        private const double TitleBarHoverMargin = 6.0;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hoverTimer;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _idleTimer;
        private bool _cursorOverPanel;
        private RectInt32? _panelScreenRectCache;

        public ViewModel.SettingViewModel ViewModel { get; }

        /// <summary>标题栏命令与图标状态；实现按项目约定回跳本窗口的公共入口。</summary>
        public ViewModel.MainViewModel MainViewModel { get; }

        public MainWindow()
        {
            ViewModel = App.Services.GetRequiredService<ViewModel.SettingViewModel>();
            MainViewModel = new ViewModel.MainViewModel();
            InitializeComponent();
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
            AppWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
            // 窗口句柄与 AppWindow 引用只取一次，全程只用字段。
            _hwnd = WindowNative.GetWindowHandle(this);
            _appWindow = AppWindow;
            _appWindow.Resize(new SizeInt32(1024, 1024));
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/icon.ico");
            if (File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }

            // music_player 同款：保留边框，移除系统标题栏与右上角系统按钮，
            // 由自绘 AppTitleBar（拖动区 + 最小化/最大化/关闭）接管。
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
            }

            _appWindow.Closing += AppWindow_Closing;
            _appWindow.Changed += AppWindow_Changed;
            // music_player MainPage.SetTitleBarArea 同款：Loaded/SizeChanged 主动注册拖动区，
            // 只靠 AppWindow.Changed 被动刷新会在首帧 XamlRoot 未就绪时被跳过，导致拖动区缺失。
            AppTitleBar.Loaded += AppTitleBar_Loaded;
            AppTitleBar.SizeChanged += AppTitleBar_SizeChanged;
            // 首次激活时 AppWindow.TitleBar 才保证就绪（SizeChanged 可能早于它），补注册一次
            Activated += MainWindow_Activated;
            themeStyleHelper = new ThemeStyleHelper(this, _appWindow);
            themeStyleHelper.SetAppStyle();
        }

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_dragRegionRegistered) return;
            try
            {
                UpdateDragRegion();
                UpdateMaximizeGlyph();
            }
            catch (Exception caughtException)
            {
                AppLog.Caught(caughtException);
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            if (App.IsExiting) return;
            if (AppSettings.IsRunningBackend)
            {
                // Wallpaper remains attached; a hidden widget can suspend its renderer until restored.
                if (!AppSettings.WallpaperEnabled)
                {
                    this.Hide();
                    MyCanvas.SetRenderingSuspended(true);
                }
                AppLog.Info("Window close requested; continuing in background (tray Exit ends the process).");
                return;
            }
            App.Current_Exit();
        }

        internal void ShowFromActivation()
        {
            if (App.IsExiting) return;
            AppLog.Info("Showing existing instance.");
            if (AppSettings.WallpaperEnabled || AppSettings.IsLocked)
            {
                // Preserve the desktop child's no-activation policy and the locked widget's focus policy.
                if (!Visible)
                {
                    _appWindow.Show(activateWindow: false);
                    MyCanvas.SetRenderingSuspended(false);
                }
                WindowHelper.OpenWindow<View.SettingWindow>();
                return;
            }
            this.Show();
            if (_appWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
                presenter.Restore();
            MyCanvas.SetRenderingSuspended(false);
            WindowHelper.SetForegroundWindow(this);
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // 锁定态：z 序变动后被挤出置顶层时幂等重申
            if (AppSettings.IsLocked && !_changingWallpaper && _wallpaperHost == null && args.DidZOrderChange)
            {
                WindowHelper.EnsureTopmost(_hwnd);
            }
            if (args.DidPositionChange || args.DidSizeChange)
            {
                _panelScreenRectCache = null;
                // AppWindow.Changed 在锁定/解锁的样式切换中途同步触发，
                // 此刻改动 XAML/标题栏可能抛出 stowed exception，延迟到 UI 队列执行
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    try
                    {
                        UpdateDragRegion();
                        UpdateMaximizeGlyph();
                    }
                    catch (Exception caughtException2)
                    {
                        AppLog.Caught(caughtException2);
                    }
                });
            }
        }

        // ==== 自绘标题栏 ====

        private void AppTitleBar_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateDragRegion();
        }

        private void AppTitleBar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateDragRegion();
        }

        private void AppTitleBar_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (!AppSettings.IsLocked)
            {
                AppTitleBar.Opacity = 1;
            }
        }

        private void AppTitleBar_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!AppSettings.IsLocked)
            {
                AppTitleBar.Opacity = 0;
            }
        }

        // ==== 标题栏命令入口（MainViewModel 经 App.MainWindow 回调，窗口实现在此）====

        public void MinimizeWindow()
        {
            if (_appWindow.Presenter is OverlappedPresenter overlapped)
            {
                overlapped.Minimize();
            }
        }

        public void ToggleMaximize()
        {
            if (_appWindow.Presenter is OverlappedPresenter overlapped)
            {
                if (overlapped.State == OverlappedPresenterState.Maximized)
                {
                    overlapped.Restore();
                }
                else
                {
                    overlapped.Maximize();
                }
            }
            UpdateMaximizeGlyph();
        }

        /// <summary>锁定按钮可切换（DesktopLyrics 同款）：锁定态下悬停放行点击后可直接解锁。</summary>
        public void ToggleLock()
        {
            if (AppSettings.IsLocked)
            {
                ExitLock();
            }
            else
            {
                ApplyLock();
            }
        }

        public void ToggleFullScreen()
        {
            if (_wallpaperHost != null || _changingWallpaper) return;
            SetFullScreen(!IsFullScreen);
        }

        private void UpdateMaximizeGlyph() => MainViewModel.IsMaximized =
            _appWindow.Presenter is OverlappedPresenter p && p.State == OverlappedPresenterState.Maximized;

        /// <summary>整个 AppTitleBar 注册为系统拖动区（物理像素，music_player SetTitleBarArea 同款；
        /// 区域内的 Button 仍可正常点击，拖动只作用于空白处）。TitleBar 未就绪时静默跳过，
        /// 由 MainWindow_Activated 在首次激活后重试。</summary>
        private void UpdateDragRegion()
        {
            if (_changingWallpaper || _wallpaperHost != null) return;
            if (AppTitleBar.ActualWidth <= 0 || Content?.XamlRoot is null) return;
            if (_appWindow.TitleBar is null) return;
            double scale = Content.XamlRoot.RasterizationScale;
            _appWindow.TitleBar.SetDragRectangles(
            [
                new RectInt32(
                    0,
                    0,
                    (int)(AppTitleBar.ActualWidth * scale),
                    (int)(AppTitleBar.ActualHeight * scale)),
            ]);
            _dragRegionRegistered = true;
        }

        // ==== 锁定 / 解锁 ====

        /// <summary>进入锁定态：无边框 + 点击穿透 + 置顶；按钮组靠光标轮询显隐与放行。</summary>
        public void ApplyLock()
        {
            if (_wallpaperHost != null) return;
            AppTitleBar.Opacity = 0;
            themeStyleHelper?.SetTransparent();
            WindowHelper.Enable(this);
            WindowHelper.SetLock(this, true);
            AppSettings.IsLocked = true;
            _panelScreenRectCache = null;
            StartIdleTimer();
        }

        /// <summary>退出锁定态：还原边框/穿透/置顶。</summary>
        public void ExitLock()
        {
            StopIdleTimer();
            StopHoverTimer();
            _cursorOverPanel = false;
            WindowHelper.SetLock(this, false);
            WindowHelper.Disable(this);
            AppSettings.IsLocked = false;
            SetAppStyle();
            AppTitleBar.Opacity = 0;
        }

        private void StartIdleTimer()
        {
            if (_idleTimer == null)
            {
                _idleTimer = DispatcherQueue.CreateTimer();
                _idleTimer.Interval = TimeSpan.FromMilliseconds(IdlePollingIntervalMs);
                _idleTimer.Tick += OnIdleTimerTick;
            }
            _idleTimer.Start();
        }

        private void StopIdleTimer() => _idleTimer?.Stop();

        private void StartHoverTimer()
        {
            if (_hoverTimer == null)
            {
                _hoverTimer = DispatcherQueue.CreateTimer();
                _hoverTimer.Interval = TimeSpan.FromMilliseconds(HoverPollingIntervalMs);
                _hoverTimer.Tick += OnHoverTimerTick;
            }
            _hoverTimer.Start();
        }

        private void StopHoverTimer() => _hoverTimer?.Stop();

        /// <summary>锁定态静默轮询：自愈 + 进窗检测；光标进入窗口即显示按钮组并切快轮询。</summary>
        private void OnIdleTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            try
            {
                if (!AppSettings.IsLocked)
                {
                    sender.Stop();
                    return;
                }
                // 自愈：锁定窗不进任务栏，被偶发最小化/隐藏即无恢复入口（表现为可视化消失）
                if (WindowHelper.IsWindowMinimized(this) || !WindowHelper.IsWindowVisible(this))
                {
                    WindowHelper.RestoreOverlay(_hwnd);
                }
                if (WindowHelper.GetCursorPos(out WindowHelper.POINT cursor) && IsCursorOverWindow(cursor))
                {
                    AppTitleBar.Opacity = 1;   // 仅显示按钮组，穿透保持
                    StartHoverTimer();
                }
            }
            catch (Exception caughtException3)
            {
                AppLog.Caught(caughtException3);
                // 定时器回调里的异常会被 XAML stow 成进程崩溃，必须就地吞掉
            }
        }

        private void OnHoverTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            try
            {
                if (!AppSettings.IsLocked || !WindowHelper.GetCursorPos(out WindowHelper.POINT cursor) || !IsCursorOverWindow(cursor))
                {
                    // 离开窗口：还原按钮组与穿透，回到慢轮询
                    sender.Stop();
                    _cursorOverPanel = false;
                    AppTitleBar.Opacity = 0;
                    WindowHelper.SetClickThrough(_hwnd, true);
                    return;
                }
                // 光标移到标题栏按钮区 = 临时取消穿透供点击；离开立即恢复穿透
                bool overPanel = IsCursorOverControlPanel(cursor);
                if (overPanel != _cursorOverPanel)
                {
                    _cursorOverPanel = overPanel;
                    WindowHelper.SetClickThrough(_hwnd, !overPanel);
                }
            }
            catch (Exception caughtException4)
            {
                AppLog.Caught(caughtException4);
            }
        }

        private bool IsCursorOverWindow(WindowHelper.POINT cursor)
        {
            PointInt32 pos = _appWindow.Position;
            SizeInt32 size = _appWindow.Size;
            return cursor.X >= pos.X && cursor.X < pos.X + size.Width
                && cursor.Y >= pos.Y && cursor.Y < pos.Y + size.Height;
        }

        /// <summary>光标是否悬停在标题栏按钮区上；屏幕矩形缓存，窗口静止时纯数值比较。</summary>
        private bool IsCursorOverControlPanel(WindowHelper.POINT cursor)
        {
            if (_panelScreenRectCache is { } cached)
            {
                return cursor.X >= cached.X && cursor.X <= cached.X + cached.Width
                    && cursor.Y >= cached.Y && cursor.Y <= cached.Y + cached.Height;
            }
            if (AppTitleBar.ActualWidth <= 0 || Content?.XamlRoot is null) return false;
            double scale = Content.XamlRoot.RasterizationScale;
            Windows.Foundation.Rect bounds = AppTitleBar.TransformToVisual(null)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, AppTitleBar.ActualWidth, AppTitleBar.ActualHeight));
            var origin = new WindowHelper.POINT();
            if (!WindowHelper.ClientToScreen(_hwnd, ref origin)) return false;
            int left = origin.X + (int)((bounds.X - TitleBarHoverMargin) * scale);
            int top = origin.Y + (int)((bounds.Y - TitleBarHoverMargin) * scale);
            int right = origin.X + (int)((bounds.X + bounds.Width + TitleBarHoverMargin) * scale);
            int bottom = origin.Y + (int)((bounds.Y + bounds.Height + TitleBarHoverMargin) * scale);
            _panelScreenRectCache = new RectInt32(left, top, right - left, bottom - top);
            return cursor.X >= left && cursor.X <= right && cursor.Y >= top && cursor.Y <= bottom;
        }

        // ==== 其它 ====

        public void ChangeRefreshRate()
        {
            MyCanvas.ChangeRefreshRate();
        }

        public void SetAppStyle()
        {
            if (_wallpaperHost != null || AppSettings.IsLocked)
            {
                themeStyleHelper.SetTransparent();
                return;
            }
            themeStyleHelper.SetAppStyle();
        }

        public void SetCustomAppStyle()
        {
            if (_wallpaperHost != null || AppSettings.IsLocked) return;
            themeStyleHelper.ChangeCustomAcrylicStyle();
        }

        public void SetAppTheme()
        {
            SetAppStyle();
            themeStyleHelper.SetAppTheme();
        }

        /// <summary>Activates a specific effect page by registry id.</summary>
        public void SwitchToEffect(string id)
        {
            MyCanvas.LoadEffect(id);
        }

        internal System.Threading.Tasks.Task StopRenderingAsync() => MyCanvas.StopAsync();

        /// <summary>Cycles to the next/previous effect page.</summary>
        public void SwitchEffect(int direction)
        {
            MyCanvas.SwitchEffect(direction);
        }

        private void SetFullScreen(bool enabled)
        {
            if (enabled == IsFullScreen) return;
            if (enabled)
            {
                _restoreFullScreenTitleBarExtension = ExtendsContentIntoTitleBar;
                _restoreFullScreenVisualizerMargin = VisualizerHost.Margin;
                // Match wallpaper mode: remove both the XAML island's title-bar
                // inset and the widget's margin before filling the screen.
                ExtendsContentIntoTitleBar = false;
                VisualizerHost.Margin = new Thickness(0);
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.Default);
                ExtendsContentIntoTitleBar = _restoreFullScreenTitleBarExtension;
                VisualizerHost.Margin = _restoreFullScreenVisualizerMargin;
            }
            IsFullScreen = enabled;
            MainViewModel.IsFullScreen = enabled;
        }
    }
}
