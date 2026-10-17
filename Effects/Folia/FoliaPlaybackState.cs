using System;
using System.Diagnostics;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed record FoliaClock(double Position, double Duration, bool Playing, long Timestamp)
{
    internal double Now => Math.Clamp(Position + (Playing ? Stopwatch.GetElapsedTime(Timestamp).TotalSeconds : 0), 0, Math.Max(Position, Duration));
}

/// <summary>Only immutable references cross threads; rendering never locks or calls SMTC / HTTP.</summary>
internal sealed class FoliaPlaybackState
{
    private FoliaSong _song = FoliaSong.Empty;
    private FoliaClock _clock = new(0, 0, false, Stopwatch.GetTimestamp());
    internal FoliaSong Song => Volatile.Read(ref _song);
    internal FoliaClock Clock => Volatile.Read(ref _clock);
    internal void Publish(FoliaSong song) => Volatile.Write(ref _song, song);
    internal void SetClock(double position, double duration, bool playing) => Volatile.Write(ref _clock,
        new(Math.Max(0, position), Math.Max(0, duration), playing, Stopwatch.GetTimestamp()));
}
