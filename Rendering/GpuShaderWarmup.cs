using System;
using System.Threading;
using System.Threading.Tasks;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;

namespace WinExSpectrumTest.Rendering;

/// <summary>One startup warmup, with no window, audio capture, or UI-thread GPU work.</summary>
internal sealed class GpuShaderWarmup
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();
    private Task? _work;
    private Task? _shutdown;
    private GpuDeviceLease? _device;
    internal uint PreparedModes { get; private set; }
    internal uint PreparedDlssPresets { get; private set; }

    // The live render worker uses spatial AA until warmup releases all SDK contexts.
    // NGX initialization/shutdown is device-wide and must not race the warmup contexts.
    internal bool IsReady => Volatile.Read(ref _work)?.IsCompleted ?? true;

    internal Task Start()
    {
        lock (_gate)
        {
            if (_shutdown != null) return _shutdown;
            return _work ??= Task.Factory.StartNew(Run, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    private void Run()
    {
        try
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
            if (_stop.IsCancellationRequested) return;
            // Keep pipeline caches until a live renderer takes ownership, or application shutdown.
            _device = GpuDeviceLease.Acquire();
            // 仅用静默合成输入预热；禁止为了预热启动 WASAPI 环回采集。
            using var analyzer = new SpectrumAnalyzer(captureAudio: false);
            using var graphics = new GpuGraphics(analyzer, static (_, _, _) => { }, 256, 144,
                static () => new SonicGpuEffect(), new(100, ReconstructionMode.Fxaa, 80));
            foreach (var mode in new[] { ReconstructionMode.Off, ReconstructionMode.Fxaa, ReconstructionMode.Smaa,
                ReconstructionMode.XeSS, ReconstructionMode.Fsr, ReconstructionMode.Dlss })
            {
                if (mode >= ReconstructionMode.XeSS && (graphics.Reconstruction.Capabilities & (1u << (int)mode)) == 0) continue;
                int presets = mode == ReconstructionMode.Dlss ? 4 : 1;
                for (int preset = 0; preset < presets; preset++)
                {
                    string name = preset switch { 1 => "j", 2 => "l", 3 => "m", _ => "k" };
                    for (int scale = mode >= ReconstructionMode.XeSS ? 50 : 100; scale <= 100; scale += 50)
                    {
                        if (_stop.IsCancellationRequested) return;
                        try
                        {
                            graphics.Configure(new(scale, mode, 80, name));
                            // Real dispatch also warms kernels the SDK creates lazily on its first frame.
                            graphics.Render(1d / 60, 200, 1000);
                            if (graphics.Reconstruction.Active == mode)
                            {
                                PreparedModes |= 1u << (int)mode;
                                if (mode == ReconstructionMode.Dlss) PreparedDlssPresets |= 1u << preset;
                            }
                        }
                        catch (Exception ex)
                        {
                            App.WriteCrashLog("GPU shader warmup " + mode, ex.Message, ex);
                            // A partially recorded frame must not be reused after an unexpected failure.
                            return;
                        }
                    }
                }
            }
        }
        catch (Exception ex) { App.WriteCrashLog("GPU shader warmup", ex.Message, ex); }
    }

    internal Task StopAsync()
    {
        lock (_gate)
        {
            if (_shutdown != null) return _shutdown;
            _stop.Cancel();
            Task work = _work ?? Task.CompletedTask;
            // Even an already completed warmup must release the device off the UI thread.
            return _shutdown = Task.Run(async () =>
            {
                await work.ConfigureAwait(false);
                ReleaseDeviceCache();
                _stop.Dispose();
            });
        }
    }

    /// <summary>Hands cache ownership to the live renderer; also allows device recreation after failure.</summary>
    internal void ReleaseDeviceCache()
    {
        if (!IsReady) return;
        GpuGraphics.DisposeResource(Interlocked.Exchange(ref _device, null));
    }
}
