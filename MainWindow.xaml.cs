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
    public sealed partial class MainWindow : WinUIEx.WindowEx
    {
        private IntPtr _hwnd;
        private ThemeStyleHelper themeStyleHelper;
        private bool IsFullScreen = false;

        // 锁定态光标轮询（DesktopLyrics 同款两档）：
        // 慢轮询 200ms 做进窗检测与自愈；悬停窗口期间切 50ms 快轮询保证按钮跟手；
        // 光标移到按钮组上才临时取消穿透供点击，离开立即恢复穿透。
        private const double HoverPollingIntervalMs = 50;
        private const double IdlePollingIntervalMs = 200;
        private const double ControlPanelHoverMargin = 6.0;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _hoverTimer;
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _idleTimer;
        private bool _cursorOverPanel;
        private RectInt32? _panelScreenRectCache;

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets/icon.ico");
            if (File.Exists(iconPath))
            {
                this.SetIcon(iconPath);
            }

            // 移除右上角系统标题栏按钮（最小化/最大化/关闭），保留边框与调整大小。
            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, false);
            }

            this.AppWindow.Closing += AppWindow_Closing;
            this.AppWindow.Changed += AppWindow_Changed;
            _hwnd = WindowNative.GetWindowHandle(this);
            themeStyleHelper = new ThemeStyleHelper(this, this.AppWindow);
            themeStyleHelper.SetAppStyle();
            if (AppSettings.WallpaperMode)
            {
                // Persisted wallpaper mode: re-attach directly on launch.
                themeStyleHelper.SetTransparent();
                this.SetIsShownInSwitchers(false);
                WallpaperHelper.Enter(this);
            }
        }

        private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            args.Cancel = true;
            App.Current_Exit();
        }

        private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
        {
            // z 序变动后被挤出置顶层时幂等重申（锁定态防遮挡）；位置/尺寸变化使命中缓存失效
            if (AppSettings.IsLocked && args.DidZOrderChange)
            {
                WindowHelper.EnsureTopmost(_hwnd);
            }
            if (args.DidPositionChange || args.DidSizeChange)
            {
                _panelScreenRectCache = null;
            }
        }

        /// <summary>进入锁定态：无边框 + 点击穿透 + 置顶；按钮组靠光标轮询显隐与放行。</summary>
        public void ApplyLock()
        {
            TopCommand.Opacity = 0;
            themeStyleHelper?.SetTransparent();
            WindowHelper.Enable(this);
            WindowHelper.SetLock(this, true);
            AppSettings.IsLocked = true;
            _panelScreenRectCache = null;
            StartIdleTimer();
        }

        /// <summary>退出锁定态：还原边框/穿透/置顶，按钮组恢复常显。</summary>
        public void ExitLock()
        {
            StopIdleTimer();
            StopHoverTimer();
            _cursorOverPanel = false;
            WindowHelper.SetLock(this, false);
            WindowHelper.Disable(this);
            AppSettings.IsLocked = false;
            SetAppStyle();
            TopCommand.Opacity = 1;
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // 锁定按钮可切换（DesktopLyrics 同款）：锁定态下悬停按钮组放行点击后可直接解锁
            if (AppSettings.IsLocked)
            {
                ExitLock();
            }
            else
            {
                ApplyLock();
            }
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
                TopCommand.Opacity = 1;   // 仅显示按钮组，穿透保持
                StartHoverTimer();
            }
        }

        private void OnHoverTimerTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
        {
            if (!AppSettings.IsLocked || !WindowHelper.GetCursorPos(out WindowHelper.POINT cursor) || !IsCursorOverWindow(cursor))
            {
                // 离开窗口：还原按钮组与穿透，回到慢轮询
                sender.Stop();
                _cursorOverPanel = false;
                TopCommand.Opacity = 0;
                WindowHelper.SetClickThrough(_hwnd, true);
                return;
            }
            // 光标移到按钮组上 = 临时取消穿透供点击；离开按钮组立即恢复穿透
            bool overPanel = IsCursorOverControlPanel(cursor);
            if (overPanel != _cursorOverPanel)
            {
                _cursorOverPanel = overPanel;
                WindowHelper.SetClickThrough(_hwnd, !overPanel);
            }
        }

        private bool IsCursorOverWindow(WindowHelper.POINT cursor)
        {
            PointInt32 pos = AppWindow.Position;
            SizeInt32 size = AppWindow.Size;
            return cursor.X >= pos.X && cursor.X < pos.X + size.Width
                && cursor.Y >= pos.Y && cursor.Y < pos.Y + size.Height;
        }

        /// <summary>光标是否悬停在顶部按钮组上；屏幕矩形缓存，窗口静止时纯数值比较。</summary>
        private bool IsCursorOverControlPanel(WindowHelper.POINT cursor)
        {
            if (_panelScreenRectCache is { } cached)
            {
                return cursor.X >= cached.X && cursor.X <= cached.X + cached.Width
                    && cursor.Y >= cached.Y && cursor.Y <= cached.Y + cached.Height;
            }
            if (TopCommand.ActualWidth <= 0 || Content?.XamlRoot is null) return false;
            double scale = Content.XamlRoot.RasterizationScale;
            Windows.Foundation.Rect bounds = TopCommand.TransformToVisual(null)
                .TransformBounds(new Windows.Foundation.Rect(0, 0, TopCommand.ActualWidth, TopCommand.ActualHeight));
            var origin = new WindowHelper.POINT();
            if (!WindowHelper.ClientToScreen(_hwnd, ref origin)) return false;
            int left = origin.X + (int)((bounds.X - ControlPanelHoverMargin) * scale);
            int top = origin.Y + (int)((bounds.Y - ControlPanelHoverMargin) * scale);
            int right = origin.X + (int)((bounds.X + bounds.Width + ControlPanelHoverMargin) * scale);
            int bottom = origin.Y + (int)((bounds.Y + bounds.Height + ControlPanelHoverMargin) * scale);
            _panelScreenRectCache = new RectInt32(left, top, right - left, bottom - top);
            return cursor.X >= left && cursor.X <= right && cursor.Y >= top && cursor.Y <= bottom;
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

        /// <summary>Whether the window is currently docked to the wallpaper layer.</summary>
        public bool IsWallpaperModeActive => AppSettings.WallpaperMode;

        /// <summary>Activates a specific effect page by registry id.</summary>
        public void SwitchToEffect(string id)
        {
            MyCanvas.LoadEffect(id);
        }

        /// <summary>Cycles to the next/previous effect page.</summary>
        public void SwitchEffect(int direction)
        {
            MyCanvas.SwitchEffect(direction);
        }

        private void PreviousEffect_Click(object sender, RoutedEventArgs e)
        {
            SwitchEffect(-1);
        }

        private void NextEffect_Click(object sender, RoutedEventArgs e)
        {
            SwitchEffect(1);
        }

        /// <summary>Toggles the desktop wallpaper (WorkerW) display mode.</summary>
        public void ToggleWallpaper()
        {
            if (AppSettings.WallpaperMode)
            {
                WallpaperHelper.Exit(this);
                AppSettings.WallpaperMode = false;
                WindowHelper.Disable(this);
                this.SetIsShownInSwitchers(true);
                themeStyleHelper?.SetAppStyle();
                if (!AppSettings.IsLocked)
                {
                    TopCommand.Opacity = 1;
                }
            }
            else
            {
                TopCommand.Opacity = 0;
                themeStyleHelper?.SetTransparent();
                this.SetIsShownInSwitchers(false);
                WallpaperHelper.Enter(this);
                AppSettings.WallpaperMode = true;
            }
            _ = Service.DataJsonService.SaveSettingAsync();
        }

        private void Wallpaper_Click(object sender, RoutedEventArgs e)
        {
            ToggleWallpaper();
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
