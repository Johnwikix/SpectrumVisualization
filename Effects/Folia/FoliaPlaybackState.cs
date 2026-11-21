using System;
using System.Diagnostics;
using System.Threading;
using WinExSpectrumTest.Services;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Only immutable references cross threads; rendering never locks or calls SMTC / HTTP.</summary>
internal sealed class FoliaPlaybackState
{
    private FoliaSong _song = FoliaSong.Empty;
    private MediaPlaybackClock _clock = MediaPlaybackClock.Empty;
    internal FoliaSong Song => Volatile.Read(ref _song);
    internal MediaPlaybackClock Clock => Volatile.Read(ref _clock);
    internal void Publish(FoliaSong song) => Volatile.Write(ref _song, song);
    internal void SetClock(MediaPlaybackClock clock) => Volatile.Write(ref _clock, clock);
    internal void SetClock(double position, double duration, bool playing) => SetClock(
        new MediaPlaybackClock(Math.Max(0, position), Math.Max(0, duration), playing, Stopwatch.GetTimestamp()));
}
