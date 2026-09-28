using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinExSpectrumTest.Rendering;

/// <summary>Waits on absolute frame deadlines without rounding each 120 Hz frame to whole milliseconds.</summary>
internal sealed class GpuFramePacer : IDisposable
{
    private readonly TimerHandle _timer;
    private readonly WaitHandle[] _waits;
    private double _deadline;

    internal GpuFramePacer(WaitHandle wake)
    {
        var handle = CreateWaitableTimerExW(0, 0, 2, 0x001F0003);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        _timer = new TimerHandle(handle);
        _waits = [wake, _timer];
        Reset();
    }

    internal void Reset() => _deadline = Stopwatch.GetTimestamp();

    internal void Wait(double framesPerSecond)
    {
        _deadline += Stopwatch.Frequency / framesPerSecond;
        long now = Stopwatch.GetTimestamp();
        double remaining = _deadline - now;
        if (remaining <= 0)
        {
            _deadline = now;
            return;
        }
        long due = -Math.Max(1, (long)(remaining * 10_000_000 / Stopwatch.Frequency));
        if (!SetWaitableTimer(_timer.SafeWaitHandle, in due, 0, 0, 0, false))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (WaitHandle.WaitAny(_waits) == 0) Reset();
    }

    public void Dispose() => _timer.Dispose();

    private sealed class TimerHandle : WaitHandle
    {
        internal TimerHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(nint attributes, nint name, uint flags, uint access);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period, nint completion, nint argument,
        [MarshalAs(UnmanagedType.Bool)] bool resume);
}
