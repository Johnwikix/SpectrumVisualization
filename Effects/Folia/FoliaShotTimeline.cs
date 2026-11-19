using System;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold-built cue lookup shared by the two time-directed lyric stages.</summary>
internal readonly record struct FoliaShot(double Start, double End, int Line, int Word, uint Seed)
{
    internal float Progress(double position) => Math.Clamp((float)((position - Start) / Math.Max(.001, End - Start)), 0, 1);
}

internal static class FoliaShotTimeline
{
    internal static uint Seed(ReadOnlySpan<char> identity)
    {
        uint value = 2166136261;
        foreach (char character in identity)
            value = unchecked((value ^ character) * 16777619);
        return value;
    }

    internal static float Random(uint seed, uint offset)
    {
        uint bits = unchecked(seed + offset * 0x9E3779B9);
        bits = unchecked((bits ^ (bits >> 16)) * 0x7FEB352D);
        bits = unchecked((bits ^ (bits >> 15)) * 0x846CA68B);
        return ((bits ^ (bits >> 16)) >> 8) * (1f / 16777216);
    }

    internal static int Find(ReadOnlySpan<FoliaShot> shots, double position)
    {
        int start = 0, end = shots.Length;
        while (start < end)
        {
            int middle = (start + end) >> 1;
            if (shots[middle].Start <= position)
                start = middle + 1;
            else
                end = middle;
        }
        return start - 1;
    }

    internal static double SongEnd(FoliaSong song)
    {
        double last = song.Lines.Length == 0 ? 0 : SungEnd(song.Lines[^1]);
        return song.Duration > 0 ? Math.Max(song.Duration, last + .1) : Math.Max(480, last + 20);
    }

    internal static double SungEnd(FoliaLine line) => line.Words.Length == 0
        ? Math.Min(line.End, line.Start + 4)
        : Math.Max(line.Start + .001, line.Words[^1].End);

    internal static void FillGap(System.Collections.Generic.List<FoliaShot> shots, double start, double end,
        double interval, uint seed, CancellationToken token)
    {
        int count = (int)Math.Clamp(Math.Ceiling((end - start) / interval), 0, 4096);
        double width = (end - start) / Math.Max(1, count);
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            shots.Add(new(start + width * i, start + width * (i + 1), -1, -1, unchecked(seed + (uint)shots.Count)));
        }
    }
}
