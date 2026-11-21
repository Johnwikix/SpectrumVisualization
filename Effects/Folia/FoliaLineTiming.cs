using System;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Preserves Folia's short-line render hints independently of the scene anchor.</summary>
internal readonly record struct FoliaLineTiming(double RenderEnd, float Enter, float Exit, float Hold, int Mode)
{
    internal static FoliaLineTiming From(FoliaLine line)
    {
        double duration = Math.Max(0, line.End - line.Start);
        int mode = duration < .10 ? 2 : duration < .18 ? 1 : 0;
        if (mode == 2)
            return new(Math.Max(line.End, line.Start + .067), 0, 0, 0, mode);
        float enter = mode == 1 ? Math.Clamp((float)duration * .45f, .045f, .06f)
            : Math.Clamp((float)Math.Max(duration, .12) * .34f, .22f, .42f);
        float exit = mode == 1 ? Math.Clamp((float)duration * .22f, .03f, .04f)
            : Math.Clamp((float)Math.Max(duration, .12) * .18f, .18f, .32f);
        float hold = mode == 1 ? .03f : .06f;
        double last = line.Words.Length > 0 ? line.Words[^1].End : line.End;
        double pass = Math.Max(last, line.Start) + hold;
        double exitStart = Math.Max(pass, line.End - exit);
        if (mode == 1)
            exitStart = Math.Max(line.Start + enter + .01, exitStart);
        return new(Math.Max(line.End, exitStart + exit), enter, exit, hold, mode);
    }

    internal float Lookahead => Mode == 2 ? .03f : Mode == 1 ? .08f : .15f;
    internal double WordEntrance(double start) => start - Lookahead;
    internal double WordEnd(double start, double end) => Mode == 2 ? RenderEnd : Mode == 1 ? Math.Min(RenderEnd, Math.Max(end, start + .12)) : end;
}

/// <summary>Lets gaps in the timeline skip all earlier lines that have already exited.</summary>
internal readonly struct FoliaLineTimingIndex
{
    private readonly record struct Entry(double End, double LatestEnd);
    private readonly Entry[] _entries;

    internal FoliaLineTimingIndex(FoliaPreparedLine[] lines)
    {
        _entries = lines.Length == 0 ? [] : new Entry[lines.Length];
        double latest = double.NegativeInfinity;
        for (int i = 0; i < lines.Length; i++)
        {
            double end = lines[i].Source.Timing.RenderEnd;
            if (end > latest) latest = end;
            _entries[i] = new(end, latest);
        }
    }

    internal int FindActive(int anchor, double time)
    {
        for (int i = anchor; i >= 0 && time <= _entries[i].LatestEnd; i--)
            if (time <= _entries[i].End)
                return i;
        return -1;
    }
}
