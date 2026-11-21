using System;
using System.Diagnostics;

namespace WinExSpectrumTest.Services;

/// <summary>A playback position anchored to the monotonic clock at publication.</summary>
internal sealed record MediaPlaybackClock(double Position, double Duration, bool Playing, long Timestamp, double Rate = 1)
{
    internal static readonly MediaPlaybackClock Empty = new(0, 0, false, 0);
    internal double Now => At(Stopwatch.GetTimestamp());

    internal double At(long timestamp)
    {
        double value = Playing ? Position + Stopwatch.GetElapsedTime(Timestamp, timestamp).TotalSeconds * Rate : Position;
        return Duration > 0 ? Math.Clamp(value, 0, Duration) : Math.Max(0, value);
    }
}
