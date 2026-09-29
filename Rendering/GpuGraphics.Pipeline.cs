using System;
using System.Diagnostics;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    private const int PipelineDepth = 2;
    private ID3D12CommandQueue? _sceneQueue;
    private ID3D12Fence? _sceneFence;
    private ulong _sceneFenceValue;
    private readonly PipelineFrame?[] _frames = new PipelineFrame?[PipelineDepth];
    private ID3D12GraphicsCommandList _maintenanceCommands = null!;
    private ID3D12Resource? _secondScene;
    private int _frameIndex;
    private int _nextFrameIndex;
    private bool PipelineEnabled => _temporal != null && _effect is IBufferedGpuEffect;

    private sealed class PipelineFrame : IDisposable
    {
        internal ID3D12CommandAllocator SceneAllocator = null!;
        internal ID3D12CommandAllocator PostAllocator = null!;
        internal ID3D12GraphicsCommandList SceneCommands = null!;
        internal ID3D12GraphicsCommandList PostCommands = null!;
        internal ulong RetireFenceValue;

        internal PipelineFrame(ID3D12Device device)
        {
            try
            {
                SceneAllocator = device.CreateCommandAllocator(CommandListType.Direct);
                PostAllocator = device.CreateCommandAllocator(CommandListType.Direct);
                SceneCommands = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, SceneAllocator);
                SceneCommands.Close();
                PostCommands = device.CreateCommandList<ID3D12GraphicsCommandList>(0, CommandListType.Direct, PostAllocator);
                PostCommands.Close();
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            DisposeResource(SceneCommands);
            DisposeResource(PostCommands);
            DisposeResource(SceneAllocator);
            DisposeResource(PostAllocator);
        }
    }

    private void ConfigurePipeline(int detail)
    {
        _frameIndex = 0;
        _nextFrameIndex = 0;
        if (PipelineEnabled)
        {
            _sceneQueue ??= _device.CreateCommandQueue(new CommandQueueDescription(CommandListType.Direct));
            _sceneFence ??= _device.CreateFence(0, FenceFlags.None);
            for (int i = 0; i < PipelineDepth; i++)
            {
                _frames[i] ??= new PipelineFrame(_device);
                _frames[i]!.RetireFenceValue = 0;
            }
        }
        if (_effect is IBufferedGpuEffect buffered)
        {
            buffered.ConfigureFrames(PipelineEnabled ? PipelineDepth : 1, detail);
            buffered.SelectFrame(0);
        }
    }

    private bool RenderPipelined(double elapsed, float white, float peak)
    {
        var slot = _frames[_nextFrameIndex]!;
        long waiting = CaptureTimings ? Stopwatch.GetTimestamp() : 0;
        // Bound resource reuse; SDK-specific synchronization below still lets the next scene run.
        WaitForGpu(slot.RetireFenceValue);
        LastGpuWaitMilliseconds = CaptureTimings ? Stopwatch.GetElapsedTime(waiting).TotalMilliseconds : 0;
        _frameIndex = _nextFrameIndex;
        ((IBufferedGpuEffect)_effect).SelectFrame(_frameIndex);
        var frame = _temporal!.BeginFrame(elapsed);
        var temporalEffect = (ITemporalGpuEffect)_effect;
        temporalEffect.SetTemporalFrame(frame);
        _effect.PrepareFrame(elapsed, _options.Detail);

        slot.SceneAllocator.Reset();
        slot.SceneCommands.Reset(slot.SceneAllocator);
        _commands = slot.SceneCommands;
        Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        _effect.RecordScene(_commands, Rtv(2));
        Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        _commands.Close();

        bool submitSceneFirst = _temporal.RequiresPreviousDispatchCompletion;
        ulong sceneReady = 0;
        if (submitSceneFirst)
        {
            // Keep the next scene running while the CPU waits for the SDK's previous dispatch.
            // A GPU queue dependency alone cannot protect SDK state changed by Evaluate on the CPU.
            sceneReady = SubmitScene(slot.SceneCommands);
            waiting = CaptureTimings ? Stopwatch.GetTimestamp() : 0;
            WaitForGpu(_fenceValue);
            if (CaptureTimings) LastGpuWaitMilliseconds += Stopwatch.GetElapsedTime(waiting).TotalMilliseconds;
        }

        slot.PostAllocator.Reset();
        slot.PostCommands.Reset(slot.PostAllocator);
        _commands = slot.PostCommands;
        // On failure the shared recovery path drains both queues, including an already submitted scene.
        if (!RecordReconstruction(in frame, elapsed)) return false;
        RecordComposition(white, peak);
        _commands.Close();

        if (!submitSceneFirst) sceneReady = SubmitScene(slot.SceneCommands);
        _queue.Wait(_sceneFence!, sceneReady).CheckError();
        _submission[0] = slot.PostCommands;
        _queue.ExecuteCommandLists(_submission);
        // Signal before Present so slot retirement does not inherit compositor pacing.
        slot.RetireFenceValue = SignalGpu();
        _nextFrameIndex = (_frameIndex + 1) % PipelineDepth;
        return PresentFrame();
    }

    private ulong SubmitScene(ID3D12GraphicsCommandList commands)
    {
        _submission[0] = commands;
        _sceneQueue!.ExecuteCommandLists(_submission);
        ulong ready = ++_sceneFenceValue;
        _sceneQueue.Signal(_sceneFence!, ready).CheckError();
        return ready;
    }

    /// <summary>Drains queued scene and reconstruction work on the worker, never on the UI thread.</summary>
    internal void Drain() => WaitForGpu();

    private void DisposePipeline()
    {
        foreach (var slot in _frames) DisposeResource(slot);
        DisposeResource(_sceneFence);
        DisposeResource(_sceneQueue);
    }
}
