using System.Diagnostics;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

internal readonly record struct PipelineSample(double PrepareCpu, double RecordCpu, double SubmitFenceCpu,
    double SceneGpu, double ReconstructionGpu, double OverlayGpu, double OutputGpu);

internal sealed unsafe partial class GpuGraphics
{
    // Deliberately serialized stage-cost sampler. Use --pipeline-benchmark for the actual
    // overlapping production path; these timestamps do not measure multi-queue throughput.
    internal PipelineSample ProfileFrame(ID3D12QueryHeap queries, ID3D12Resource readback, ulong frequency)
    {
        const double elapsed = 1d / 120;
        WaitForGpu();
        long start = Stopwatch.GetTimestamp();
        TemporalFrame frame = _temporal?.BeginFrame(elapsed) ?? default;
        if (_effect is ITemporalGpuEffect effect) effect.SetTemporalFrame(frame);
        _effect.PrepareFrame(elapsed, _options.Detail);
        double prepare = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        BeginCommands();
        _commands.EndQuery(queries, QueryType.Timestamp, 0);
        Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        _effect.RecordScene(_commands, Rtv(2));
        Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        _commands.EndQuery(queries, QueryType.Timestamp, 1);
        if (_temporal != null)
        {
            Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.NonPixelShaderResource);
            Transition(_reconstructed!, ResourceStates.PixelShaderResource, ResourceStates.UnorderedAccess);
            _temporal.Record(_commands, _scene!, (ITemporalGpuEffect)_effect, _reconstructed!, frame, elapsed);
        }
        _commands.EndQuery(queries, QueryType.Timestamp, 2);
        if (_temporal != null)
        {
            Transition(_reconstructed!, ResourceStates.UnorderedAccess, ResourceStates.RenderTarget);
            ((ITemporalGpuEffect)_effect).RecordOverlay(_commands, Rtv(4));
            Transition(_reconstructed!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            Transition(_scene!, ResourceStates.NonPixelShaderResource, ResourceStates.PixelShaderResource);
        }
        _commands.EndQuery(queries, QueryType.Timestamp, 3);
        _commands.SetDescriptorHeaps(_srvs);
        _commands.SetGraphicsRootSignature(_pipelines.Root);
        _commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commands.RSSetViewport(0, 0, Width, Height, 0, 1);
        _commands.RSSetScissorRect(Width, Height);
        _commands.SetGraphicsRootDescriptorTable(0, _srvs.GetGPUDescriptorHandleForHeapStart() + (_temporal != null ? 2 * _srvStride : 0));
        float* constants = stackalloc float[6] { _hdr ? 1 : 0, 200, 1000, 0, 1f / Width, 1f / Height };
        _commands.SetGraphicsRoot32BitConstants(1, 6, constants, 0);
        if (_antialias != null)
        {
            Transition(_antialias, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            _commands.OMSetRenderTargets(Rtv(3), null);
            _commands.SetPipelineState(_pipelines.PrepareAntialias);
            _commands.DrawInstanced(3, 1, 0, 0);
            Transition(_antialias, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            if (_smaa != null)
            {
                _smaa.Record(_commands, _hdr ? MathF.Sqrt(.2f) : 1);
                _commands.SetDescriptorHeaps(_srvs);
                _commands.SetGraphicsRootSignature(_pipelines.Root);
                _commands.SetGraphicsRoot32BitConstants(1, 6, constants, 0);
            }
            _commands.SetGraphicsRootDescriptorTable(0, _srvs.GetGPUDescriptorHandleForHeapStart() + _srvStride);
        }
        int buffer = (int)_swapChain.CurrentBackBufferIndex;
        Transition(_backBuffers[buffer]!, ResourceStates.Common, ResourceStates.RenderTarget);
        _commands.OMSetRenderTargets(Rtv(buffer), null);
        _commands.SetPipelineState(_smaa != null ? _pipelines.PreparedOutput : _antialias != null ? _pipelines.AntialiasOutput : _pipelines.Output);
        _commands.DrawInstanced(3, 1, 0, 0);
        Transition(_backBuffers[buffer]!, ResourceStates.RenderTarget, ResourceStates.Common);
        _commands.EndQuery(queries, QueryType.Timestamp, 4);
        _commands.ResolveQueryData(queries, QueryType.Timestamp, 0, 5, readback, 0);
        double record = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        Submit();
        ulong rendered = SignalGpu();
        var result = _swapChain.Present(0, PresentationFlags(nonblocking: true));
        if (result.Code != (int)Vortice.DXGI.ResultCode.WasStillDrawing) result.CheckError();
        WaitForGpu(rendered);
        double submitFence = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        ulong* ticks = readback.Map<ulong>(0);
        double scale = 1000d / frequency;
        var sample = new PipelineSample(prepare, record, submitFence,
            (ticks[1] - ticks[0]) * scale, (ticks[2] - ticks[1]) * scale,
            (ticks[3] - ticks[2]) * scale, (ticks[4] - ticks[3]) * scale);
        readback.Unmap(0);
        return sample;
    }

    internal ID3D12QueryHeap CreateTimingQueries() => _device.CreateQueryHeap<ID3D12QueryHeap>(
        new QueryHeapDescription { Type = QueryHeapType.Timestamp, Count = 5 });

    internal ID3D12Resource CreateTimingReadback() => _device.CreateCommittedResource(
        new HeapProperties(HeapType.Readback), HeapFlags.None, ResourceDescription.Buffer(5 * sizeof(ulong)), ResourceStates.CopyDest);

    internal ulong TimingFrequency()
    {
        _queue.GetTimestampFrequency(out ulong frequency).CheckError();
        return frequency;
    }

    internal GpuRenderSize ProfileSize => _size;
}
