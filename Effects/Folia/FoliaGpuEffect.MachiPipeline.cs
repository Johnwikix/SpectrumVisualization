using System;
using System.Threading;
using System.Threading.Tasks;
using Vortice.Direct3D12;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private Task<MachiDxrBackend.TracePipeline>? _machiPipelinePreparation;
    private MachiDxrBackend.TracePipeline? _machiTracePipeline;

    private bool MachiPipelinePrepared => _machiTracePipeline != null || _machiPipelinePreparation is { IsCompletedSuccessfully: true };

    private void BeginMachiPipelinePreparation()
    {
        if (_machiTracePipeline != null || _machiPipelinePreparation != null) return;
        var device = _device.QueryInterfaceOrNull<ID3D12Device5>();
        if (device == null)
        {
            _machiPipelinePreparation = Task.FromException<MachiDxrBackend.TracePipeline>(
                new InvalidOperationException("The D3D12 device does not support Machi ray tracing."));
            return;
        }
        // The driver can spend seconds building the DXR state object on a cold cache.
        // A separate device reference keeps the work independent of effect teardown.
        _machiPipelinePreparation = Task.Run(() =>
        {
            using (device) return new MachiDxrBackend.TracePipeline(device);
        });
    }

    private bool FinishMachiPipelinePreparation(in FoliaSceneRequest request)
    {
        if (_machiTracePipeline != null) return true;
        if (_machiPipelinePreparation is not { IsCompleted: true } pending) return false;
        _machiPipelinePreparation = null;
        try
        {
            _machiTracePipeline = pending.GetAwaiter().GetResult();
            return true;
        }
        catch (Exception error)
        {
            App.WriteCrashLog("Machi DXR pipeline preparation", error.Message, error);
            _failedMachiSelection = (request.Song, request.MachiScene, AppSettings.FoliaMachiDenoiser);
            return false;
        }
    }

    private void DisposeMachiPipeline()
    {
        GpuGraphics.DisposeResource(_machiTracePipeline);
        _machiTracePipeline = null;
        var pending = _machiPipelinePreparation;
        _machiPipelinePreparation = null;
        // No command list has used the unclaimed result. Its worker owns cleanup
        // even when the renderer exits while the driver is still compiling.
        if (pending != null)
            _ = pending.ContinueWith(static task =>
            {
                if (task.IsCompletedSuccessfully) GpuGraphics.DisposeResource(task.Result);
                else if (task.Exception is { } error) AppLog.Caught(error);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
