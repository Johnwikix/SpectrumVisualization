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
    public event Action<HdrOutputMode>? OutputChanged;
    public HdrOutputMode LastOutputMode { get; private set; } = HdrOutputMode.Starting;

    public GpuPanel()
    {
        HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
        VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top;
        RenderTransform = _scale;
        IsHitTestVisible = false;
    }

    public void Configure(SpectrumAnalyzer analyzer, nint hwnd, GpuRenderSettings settings, double dpiScale)
    {
        if (_stopping) return;
        Width = settings.Width;
        Height = settings.Height;
        _scale.ScaleX = _scale.ScaleY = 1 / dpiScale;
        if (_renderer == null)
        {
            if (!settings.Active || settings.Width == 0 || settings.Height == 0) return;
            _native = GetNative(this);
            _renderer = new GpuRenderer(analyzer, hwnd, BindFromWorker, PublishFromWorker, settings);
            try { _renderer.Start(); }
            catch
            {
                ReleaseNative(_native);
                _native = 0;
                _renderer = null;
                throw;
            }
        }
        else _renderer.Configure(settings);
    }

    private void PublishFromWorker(HdrOutputMode mode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_stopping)
            {
                LastOutputMode = mode;
                OutputChanged?.Invoke(mode);
            }
        });
    }

    private void BindFromWorker(nint swapChain)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // Pending binds are still acknowledged during shutdown; the worker will then detach.
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
