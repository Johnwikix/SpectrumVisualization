using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Control;
using WinExSpectrumTest.Effects;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;
using WinExSpectrumTest.Service;
using WinExSpectrumTest.ViewModel;

namespace WinExSpectrumTest.Canvas;

/// <summary>UI owner of mutually exclusive Win2D Aurora and native D3D12 Sonic hosts.</summary>
public sealed partial class CanvasPanel : UserControl
{
    public RenderDebugViewModel DebugOverlay { get; } = new();
    private SpectrumAnalyzer _analyzer = null!;
    private AuroraRingEffect? _aurora;
    private GpuPanel? _gpu;
    private SonicMediaViewModel? _media;
    private SonicMediaCard? _card;
    private XamlRoot? _root;
    private bool _loaded;
    private volatile bool _disposed;
    private volatile bool _suspended;
    private volatile bool _drawAurora;
    private string _requestedId = EffectRegistry.DefaultEffectId;
    private string? _activeId;
    private Task? _switchTask;
    private Task? _stopTask;
    private bool _drawErrorLogged;
    private bool _switching;
    private bool CanRender => !_disposed && !_suspended && !_switching && (_root?.IsHostVisible ?? true);

    public CanvasPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        RenderHost.SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed || _loaded) return;
        _loaded = true;
        // XAML constructs the view; resolve services only at this initialization boundary.
        _analyzer = App.Services.GetRequiredService<SpectrumAnalyzer>();
        _root = XamlRoot;
        if (_root != null) _root.Changed += OnRootChanged;
        AppSettings.Changed += OnSettingsChanged;
        ChangeRefreshRate();
        LoadEffect(AppSettings.VisualEffect);
    }

    public void LoadEffect(string id)
    {
        if (_disposed) return;
        _requestedId = EffectRegistry.Resolve(id).Id;
        if (AppSettings.VisualEffect != _requestedId)
        {
            AppSettings.VisualEffect = _requestedId;
            _ = DataJsonService.SaveSettingAsync();
        }
        if (!_loaded) return;
        if (_switchTask == null || _switchTask.IsCompleted) _switchTask = ApplyRequestedEffectAsync();
    }

    public void SwitchEffect(int direction)
    {
        var catalog = EffectRegistry.GetCatalog();
        int index = Array.FindIndex(catalog, item => item.Id == AppSettings.VisualEffect);
        LoadEffect(catalog[(Math.Max(0, index) + direction % catalog.Length + catalog.Length) % catalog.Length].Id);
    }

    private async Task ApplyRequestedEffectAsync()
    {
        try
        {
            while (!_disposed && _activeId != _requestedId)
            {
                _switching = true;
                DebugOverlay.SetActive(false);
                _drawAurora = false;
                SpectrumCanvasControl.Paused = true;
                _aurora?.SetActive(false);
                _media?.SetActive(false);
                // Paused does not mean an in-flight Draw has completed.
                await SpectrumCanvasControl.RunOnGameLoopThreadAsync(static () => { });
                if (_gpu != null) await _gpu.PauseAsync();
                if (_disposed) return;
                string id = _requestedId;
                var descriptor = EffectRegistry.Resolve(id);
                if (descriptor.Host == EffectHost.Gpu)
                {
                    EnsureGpuHost();
                    SpectrumCanvasControl.Visibility = Visibility.Collapsed;
                    _gpu!.Visibility = Visibility.Visible;
                    _card!.Visibility = descriptor.UsesMedia ? Visibility.Visible : Visibility.Collapsed;
                    _activeId = id;
                    HdrStatus.Set(_gpu.LastOutputMode);
                }
                else
                {
                    if (_aurora == null)
                    {
                        var effect = new AuroraRingEffect();
                        try
                        {
                            effect.Initialize(new VisualizerServices
                            {
                                Control = SpectrumCanvasControl,
                                Analyzer = _analyzer,
                                Media = App.MediaInfoService
                            });
                            effect.OnResize((float)RenderHost.ActualWidth, (float)RenderHost.ActualHeight);
                            _aurora = effect;
                        }
                        catch { effect.Dispose(); throw; }
                    }
                    if (_gpu != null) _gpu.Visibility = Visibility.Collapsed;
                    if (_card != null) _card.Visibility = Visibility.Collapsed;
                    SpectrumCanvasControl.Visibility = Visibility.Visible;
                    _activeId = id;
                    HdrStatus.Set(HdrOutputMode.Unsupported);
                }
                _switching = false;
                ApplyActivity();
            }
        }
        catch (Exception ex)
        {
            App.WriteCrashLog("Effect switch", ex.Message, ex);
            HdrStatus.Set(HdrOutputMode.Failed);
        }
        finally
        {
            _switching = false;
        }
    }

    private void EnsureGpuHost()
    {
        if (_gpu != null) return;
        _gpu = new GpuPanel(DebugOverlay.Statistics);
        _gpu.OutputChanged += OnGpuOutputChanged;
        RenderHost.Children.Add(_gpu);
        _media = new SonicMediaViewModel(App.MediaInfoService, DispatcherQueue);
        _card = new SonicMediaCard(_media)
        {
            Margin = new Thickness(16, 24, 30, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        };
        RenderHost.Children.Add(_card);
    }

    private void OnGpuOutputChanged(HdrOutputMode mode)
    {
        if (!_disposed && EffectRegistry.Resolve(_activeId).Host == EffectHost.Gpu) HdrStatus.Set(mode);
    }

    private void ConfigureGpu()
    {
        if (_gpu == null || _disposed) return;
        double dpi = XamlRoot?.RasterizationScale ?? 1;
        if (!double.IsFinite(dpi) || dpi <= 0) dpi = 1;
        int width = (int)Math.Clamp(Math.Ceiling(RenderHost.ActualWidth * dpi), 0, 16384);
        int height = (int)Math.Clamp(Math.Ceiling(RenderHost.ActualHeight * dpi), 0, 16384);
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        var effect = EffectRegistry.Resolve(_activeId);
        _gpu.Configure(_analyzer, hwnd, new GpuRenderSettings(width, height,
            effect.Host == EffectHost.Gpu && CanRender,
            AppSettings.HdrEnabled, AppSettings.HdrWhiteNits, AppSettings.HdrPeakNits, AppSettings.RefreshRate,
            effect, effect.GetSceneOptions?.Invoke() ?? default), dpi);
    }

    private void ApplyActivity()
    {
        if (_switching || _disposed) return;
        bool aurora = _activeId == AuroraRingEffect.EffectId && CanRender;
        _drawAurora = aurora;
        _aurora?.SetActive(aurora);
        if (aurora) SpectrumCanvasControl.ResetElapsedTime();
        SpectrumCanvasControl.Paused = !aurora;
        ConfigureGpu();
        _media?.SetActive(EffectRegistry.Resolve(_activeId).UsesMedia && CanRender);
        UpdateDebugOverlay();
    }

    private void UpdateDebugOverlay() => DebugOverlay.SetActive(_loaded && CanRender && _activeId != null && AppSettings.ShowDebugOverlay);

    public void SetRenderingSuspended(bool suspended)
    {
        if (_disposed || _suspended == suspended) return;
        _suspended = suspended;
        ApplyActivity();
        RenderDiagnostics.RecordRenderSuspension(suspended);
    }

    public void ChangeRefreshRate()
    {
        if (_disposed) return;
        float rate = AppSettings.RefreshRate;
        SpectrumCanvasControl.IsFixedTimeStep = rate != 0;
        if (rate != 0) SpectrumCanvasControl.TargetElapsedTime = TimeSpan.FromSeconds(1d / rate);
        ConfigureGpu();
    }

    private void OnSettingsChanged(string name)
    {
        if (_disposed) return;
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnSettingsChanged(name));
            return;
        }
        if (name == nameof(AppSettings.ShowDebugOverlay)) { UpdateDebugOverlay(); return; }
        if (name == nameof(AppSettings.RefreshRate)) { ChangeRefreshRate(); return; }
        if (name is nameof(AppSettings.HdrEnabled) or nameof(AppSettings.HdrWhiteNits) or nameof(AppSettings.HdrPeakNits)
            or nameof(AppSettings.SonicQuality)) ConfigureGpu();
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => ConfigureGpu();
    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyActivity();

    private void SpectrumCanvasControl_Update(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        if (!_drawAurora || _disposed || _suspended) return;
        try { _aurora?.Update(args.Timing.ElapsedTime.TotalSeconds); }
        catch (Exception ex) { LogDrawError(ex); }
    }

    private void SpectrumCanvasControl_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (!_drawAurora || _disposed || _suspended) return;
        try
        {
            bool debugEnabled = DebugOverlay.Statistics.Enabled;
            long started = debugEnabled ? Stopwatch.GetTimestamp() : 0;
            _aurora?.Draw(args.DrawingSession, (float)sender.Size.Width, (float)sender.Size.Height);
            if (debugEnabled)
                DebugOverlay.Statistics.Record(new RenderDebugFrame(false, false,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds, 0, 0, default,
                    ReconstructionMode.Off, null, false, false, AppSettings.RefreshRate));
            RenderDiagnostics.RecordFrame(AuroraRingEffect.EffectId, _analyzer.PublicationCount);
            _drawErrorLogged = false;
        }
        catch (Exception ex) { LogDrawError(ex); }
    }

    private void LogDrawError(Exception ex)
    {
        if (!_drawErrorLogged) App.WriteCrashLog("Aurora render", ex.Message, ex);
        _drawErrorLogged = true;
    }

    public Task StopAsync() => _stopTask ??= StopCoreAsync();
    private async Task StopCoreAsync()
    {
        _disposed = true;
        DebugOverlay.Dispose();
        _drawAurora = false;
        SpectrumCanvasControl.Paused = true;
        _aurora?.SetActive(false);
        Loaded -= OnLoaded;
        RenderHost.SizeChanged -= OnSizeChanged;
        if (_root != null) _root.Changed -= OnRootChanged;
        AppSettings.Changed -= OnSettingsChanged;
        if (_switchTask != null) await _switchTask;
        if (_gpu != null)
        {
            _gpu.OutputChanged -= OnGpuOutputChanged;
            try { await _gpu.StopAsync(); }
            catch (Exception ex) { App.WriteCrashLog("Sonic host shutdown", ex.Message, ex); }
        }
        if (_media != null)
        {
            try { await _media.StopAsync(); }
            catch (Exception ex) { App.WriteCrashLog("Artwork shutdown", ex.Message, ex); }
        }
        try { await SpectrumCanvasControl.RunOnGameLoopThreadAsync(static () => { }); }
        catch (TaskCanceledException) { }
        catch (Exception ex) { App.WriteCrashLog("Aurora drain", ex.Message, ex); }
        try { _aurora?.Dispose(); }
        catch (Exception ex) { App.WriteCrashLog("Aurora shutdown", ex.Message, ex); }
        _aurora = null;
        SpectrumCanvasControl.RemoveFromVisualTree();
        // SpectrumAnalyzer belongs to the application service container, not either renderer.
    }
}
