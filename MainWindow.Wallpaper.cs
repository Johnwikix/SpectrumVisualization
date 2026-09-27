using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using WinExSpectrumTest.Helper;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest;

public sealed partial class MainWindow
{
    private DesktopWallpaperHost? _wallpaperHost;
    private DispatcherQueueTimer? _wallpaperTimer;
    private DispatcherQueueTimer? _wallpaperVisibilityTimer;
    private WallpaperOcclusionDetector? _wallpaperOcclusion;
    private bool _changingWallpaper;
    private bool _restoreLocked, _restoreFullScreen;
    private bool _restoreExtendsContentIntoTitleBar;
    private Thickness _restoreVisualizerMargin;
    private OverlappedPresenterState _restoreWindowState;

    public void SetWallpaperMode(bool enabled)
    {
        if (_changingWallpaper || enabled == (_wallpaperHost != null)) return;
        _changingWallpaper = true;
        try
        {
            if (enabled)
            {
                _restoreLocked = AppSettings.IsLocked;
                _restoreFullScreen = IsFullScreen;
                _restoreExtendsContentIntoTitleBar = ExtendsContentIntoTitleBar;
                _restoreVisualizerMargin = VisualizerHost.Margin;
                if (_restoreLocked) ExitLock();
                if (IsFullScreen)
                {
                    _appWindow.SetPresenter(AppWindowPresenterKind.Default);
                    IsFullScreen = false;
                    FullScreenIcon.Glyph = "\uE740";
                }

                if (_appWindow.Presenter is OverlappedPresenter presenter)
                {
                    _restoreWindowState = presenter.State;
                    presenter.Restore(activateWindow: false);
                }

                // Snapshot the normal widget before removing chrome/parenting.
                _wallpaperHost = new DesktopWallpaperHost(_hwnd);
                StopIdleTimer();
                StopHoverTimer();
                AppTitleBar.Visibility = Visibility.Collapsed;
                AppTitleBar.IsHitTestVisible = false;
                _appWindow.TitleBar.SetDragRectangles([]);
                _dragRegionRegistered = false;
                // Custom title-bar extension leaves a one-pixel top inset in the
                // XAML island even after Win32 chrome is removed. Disable it before
                // hiding the system title bar, then let the canvas fill every edge.
                ExtendsContentIntoTitleBar = false;
                if (_appWindow.Presenter is OverlappedPresenter borderPresenter)
                    borderPresenter.SetBorderAndTitleBar(false, false);
                VisualizerHost.Margin = new Thickness(0);
                themeStyleHelper.SetTransparent();
                _wallpaperHost.Attach();
                AppSettings.WallpaperEnabled = true;
                StartWallpaperTimer();
            }
            else
            {
                RestoreWidget();
            }
            DataJsonService.SaveSettingNow();
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("Wallpaper", ex.Message, ex);
            try
            {
                RestoreWidget();
                DataJsonService.SaveSettingNow();
            }
            catch (Exception restoreError)
            {
                App.WriteCrashLog("WallpaperRestore", restoreError.Message, restoreError);
            }
            ShowWallpaperError();
        }
        finally
        {
            _changingWallpaper = false;
            _panelScreenRectCache = null;
            if (_wallpaperHost == null)
            {
                UpdateDragRegion();
                UpdateMaximizeGlyph();
            }
        }
    }

    private void RestoreWidget()
    {
        _wallpaperTimer?.Stop();
        _wallpaperVisibilityTimer?.Stop();
        _wallpaperOcclusion = null;
        MyCanvas.SetRenderingSuspended(false);
        _wallpaperHost?.Restore();
        _wallpaperHost = null;
        AppSettings.WallpaperEnabled = false;
        ExtendsContentIntoTitleBar = _restoreExtendsContentIntoTitleBar;
        VisualizerHost.Margin = _restoreVisualizerMargin;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, false);
            if (_restoreWindowState == OverlappedPresenterState.Maximized) presenter.Maximize();
            else if (_restoreWindowState == OverlappedPresenterState.Minimized) presenter.Minimize();
        }
        if (_restoreFullScreen)
        {
            _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            IsFullScreen = true;
            FullScreenIcon.Glyph = "\uE73F";
        }
        AppTitleBar.Visibility = Visibility.Visible;
        AppTitleBar.IsHitTestVisible = true;
        AppTitleBar.Opacity = 0;
        if (_restoreLocked) ApplyLock();
        else SetAppStyle();
    }

    private void StartWallpaperTimer()
    {
        if (_wallpaperTimer == null)
        {
            _wallpaperTimer = DispatcherQueue.CreateTimer();
            _wallpaperTimer.Interval = TimeSpan.FromSeconds(2);
            _wallpaperTimer.Tick += WallpaperTimer_Tick;
        }
        _wallpaperTimer.Start();
        _wallpaperOcclusion ??= new WallpaperOcclusionDetector(_hwnd);
        if (_wallpaperVisibilityTimer == null)
        {
            _wallpaperVisibilityTimer = DispatcherQueue.CreateTimer();
            _wallpaperVisibilityTimer.Interval = TimeSpan.FromMilliseconds(500);
            _wallpaperVisibilityTimer.Tick += WallpaperVisibilityTimer_Tick;
        }
        UpdateWallpaperRendering();
        _wallpaperVisibilityTimer.Start();
    }

    private void WallpaperVisibilityTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_changingWallpaper) return;
        UpdateWallpaperRendering();
    }

    private void UpdateWallpaperRendering()
    {
        MyCanvas.SetRenderingSuspended(_wallpaperHost != null &&
            _wallpaperOcclusion?.IsFullyCovered() == true);
    }

    private void WallpaperTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (_wallpaperHost == null || _changingWallpaper) return;
        try
        {
            _wallpaperHost.Refresh();
            UpdateWallpaperRendering();
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("WallpaperRefresh", ex.Message, ex);
            SetWallpaperMode(false);
            ShowWallpaperError();
        }
    }

    private void ShowWallpaperError()
    {
        try
        {
            var resources = new ResourceLoader();
            AppNotifyIconControl.NotifyIcon.ShowNotification(
                resources.GetString("WallpaperErrorTitle"),
                resources.GetString("WallpaperErrorMessage"), H.NotifyIcon.Core.NotificationIcon.Warning);
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("WallpaperNotification", ex.Message, ex);
        }
    }

    /// <summary>Release Explorer's child before exit, preserving the saved mode.</summary>
    internal void ReleaseWallpaper()
    {
        _wallpaperTimer?.Stop();
        _wallpaperVisibilityTimer?.Stop();
        _wallpaperOcclusion = null;
        StopIdleTimer();
        StopHoverTimer();
        try
        {
            _wallpaperHost?.Restore();
            _wallpaperHost = null;
        }
        finally
        {
            AppNotifyIconControl.Dispose();
        }
    }
}
