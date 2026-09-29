using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WinExSpectrumTest.Audio;
using WinRT;

namespace WinExSpectrumTest.Rendering;

/// <summary>UI-thread bridge. No GPU wait or thread join is ever performed by the UI.</summary>
internal sealed partial class GpuPanel : SwapChainPanel
{
    private GpuRenderer? _renderer;
    private nint _native;
    private bool _stopping;
    private Task? _stopTask;
    private readonly ScaleTransform _scale = new();
    private int _bufferWidth, _bufferHeight;
    private int _requestedWidth, _requestedHeight;
    private double _dpiScale = 1;
    public event Action<HdrOutputMode>? OutputChanged;
    public HdrOutputMode LastOutputMode { get; private set; } = HdrOutputMode.Starting;

    private readonly RenderDebugStatistics _debug;

    public GpuPanel(RenderDebugStatistics debug)
    {
        _debug = debug;
        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
        RenderTransform = _scale;
        IsHitTestVisible = false;
    }

    public void Configure(SpectrumAnalyzer analyzer, nint hwnd, GpuRenderSettings settings, double dpiScale)
    {
        if (_stopping) return;
        _requestedWidth = settings.Width;
        _requestedHeight = settings.Height;
        _dpiScale = dpiScale;
        if (_renderer == null)
        {
            if (!settings.Active || settings.Width == 0 || settings.Height == 0) return;
            _bufferWidth = settings.Width;
            _bufferHeight = settings.Height;
            ApplySurfaceSize();
            _native = GetNative(this);
            _renderer = new GpuRenderer(analyzer, hwnd, BindFromWorker, PublishFromWorker, settings,
                PublishReconstructionFromWorker, _debug, App.ShaderWarmup);
            try { _renderer.Start(); }
            catch
            {
                ReleaseNative(_native);
                _native = 0;
                _renderer = null;
                throw;
            }
        }
        else
        {
            ApplySurfaceSize();
            _renderer.Configure(settings);
        }
    }

    private void ApplySurfaceSize()
    {
        Width = _bufferWidth;
        Height = _bufferHeight;
        // .NET 10: reuse one transform; no per-frame delegate, UI notification, or buffer copy.
        // Scaling includes physical-pixel resize and inverse DPI; media-card coordinates stay in DIPs.
        _scale.ScaleX = (double)_requestedWidth / _bufferWidth / _dpiScale;
        _scale.ScaleY = (double)_requestedHeight / _bufferHeight / _dpiScale;
    }

    private void PublishFromWorker(HdrOutputMode mode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_stopping)
            {
                LastOutputMode = mode;
                if (mode is HdrOutputMode.Starting or HdrOutputMode.Failed) ReconstructionAvailability.Clear();
                OutputChanged?.Invoke(mode);
            }
        });
    }

    private void PublishReconstructionFromWorker(ReconstructionStatus status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_stopping) ReconstructionAvailability.Set(status);
        });
    }

    private void BindFromWorker(nint swapChain, int width, int height)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // Pending binds are still acknowledged during shutdown; the worker will then detach.
                // Commit buffer dimensions and inverse-DPI transform in the same UI callback.
                if (swapChain != 0)
                {
                    _bufferWidth = width;
                    _bufferHeight = height;
                    ApplySurfaceSize();
                }
                SetSwapChain(_native, swapChain);
                completed.SetResult();
            }
            catch (Exception ex) { completed.SetException(ex); }
        })) throw new InvalidOperationException("The rendering host dispatcher is unavailable.");
        completed.Task.GetAwaiter().GetResult();
    }

    public Task StopAsync() => _stopTask ??= StopCoreAsync();
    public Task PauseAsync() => _renderer?.PauseAsync() ?? Task.CompletedTask;

    private async Task StopCoreAsync()
    {
        _stopping = true;
        ReconstructionAvailability.Clear();
        if (_renderer != null) await _renderer.StopAsync();
        ReleaseNative(_native);
        _native = 0;
        _renderer = null;
    }

    private static unsafe nint GetNative(SwapChainPanel panel)
    {
        Guid id = new("63AAD0B8-7C24-40FF-85A8-640D944CC325");
        int hr = ((IWinRTObject)panel).NativeObject.TryAs(id, out nint pointer);
        Marshal.ThrowExceptionForHR(hr);
        return pointer;
    }

    private static unsafe void SetSwapChain(nint instance, nint chain)
    {
        if (instance == 0) throw new ObjectDisposedException(nameof(GpuPanel));
        void** vtable = *(void***)instance;
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, int>)vtable[3])(instance, chain);
        Marshal.ThrowExceptionForHR(hr);
    }

    private static unsafe void ReleaseNative(nint instance)
    {
        if (instance == 0) return;
        void** vtable = *(void***)instance;
        _ = ((delegate* unmanaged[Stdcall]<nint, uint>)vtable[2])(instance);
    }
}
