using System;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    private ID3D12QueryHeap? _aaQueries;
    private ID3D12Resource? _aaReadback;
    private ulong* _aaTimestamps;
    private double _aaTickMilliseconds;
    private ulong _aaPendingFence;
    private long _aaNextSampleAt;
    private bool _aaMeasureFrame, _aaRecorded, _aaAcceptPending, _aaTimingUnavailable;
    private ReconstructionMode _aaSampleMode;
    private GpuRenderSize _aaSampleSize;

    internal double? LastAntialiasMilliseconds { get; private set; }

    private void BeginAntialiasTimingFrame()
    {
        LastAntialiasMilliseconds = null;
        _aaMeasureFrame = false;
        _aaRecorded = false;
        if (!CaptureTimings)
        {
            _aaAcceptPending = false;
            return;
        }

        // Never wait for a diagnostic query. The resolve and its fence use the post queue,
        // including when scene rendering overlaps reconstruction on a different queue.
        if (_aaPendingFence != 0 && _fence.CompletedValue >= _aaPendingFence)
        {
            if (_aaAcceptPending && _aaSampleMode == _activeMode && _aaSampleSize == _size &&
                _aaTimestamps[1] >= _aaTimestamps[0])
                LastAntialiasMilliseconds = (_aaTimestamps[1] - _aaTimestamps[0]) * _aaTickMilliseconds;
            _aaPendingFence = 0;
        }
        if (_aaPendingFence != 0 || _activeMode == ReconstructionMode.Off ||
            Environment.TickCount64 < _aaNextSampleAt || _aaTimingUnavailable) return;

        if (_aaQueries == null)
        {
            try
            {
                _queue.GetTimestampFrequency(out ulong frequency).CheckError();
                if (frequency == 0) throw new InvalidOperationException("GPU timestamp frequency is zero.");
                _aaTickMilliseconds = 1000d / frequency;
                _aaQueries = _device.CreateQueryHeap<ID3D12QueryHeap>(new QueryHeapDescription
                    { Type = QueryHeapType.Timestamp, Count = 2 });
                _aaReadback = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
                    ResourceDescription.Buffer(2 * sizeof(ulong)), ResourceStates.CopyDest);
                _aaTimestamps = _aaReadback.Map<ulong>(0);
            }
            catch (Exception ex)
            {
                // Optional diagnostics must not take down rendering or retry every frame.
                DisposeAntialiasTiming();
                _aaTimingUnavailable = true;
                App.WriteCrashLog("GPU timing", ex.Message, ex);
                return;
            }
        }
        _aaMeasureFrame = true;
        _aaSampleMode = _activeMode;
        _aaSampleSize = _size;
    }

    private void BeginAntialiasTiming()
    {
        if (_aaMeasureFrame) _commands.EndQuery(_aaQueries!, QueryType.Timestamp, 0);
    }

    private void EndAntialiasTiming()
    {
        if (!_aaMeasureFrame) return;
        _commands.EndQuery(_aaQueries!, QueryType.Timestamp, 1);
        _aaRecorded = true;
    }

    private void ResolveAntialiasTiming()
    {
        if (_aaRecorded) _commands.ResolveQueryData(_aaQueries!, QueryType.Timestamp, 0, 2, _aaReadback!, 0);
    }

    private void CommitAntialiasTiming(ulong fence)
    {
        if (!_aaRecorded) return;
        _aaPendingFence = fence;
        _aaAcceptPending = true;
        _aaNextSampleAt = Environment.TickCount64 + 250;
    }

    private void DisposeAntialiasTiming()
    {
        if (_aaTimestamps != null) _aaReadback!.Unmap(0);
        _aaTimestamps = null;
        DisposeResource(_aaReadback);
        _aaReadback = null;
        DisposeResource(_aaQueries);
        _aaQueries = null;
    }
}
