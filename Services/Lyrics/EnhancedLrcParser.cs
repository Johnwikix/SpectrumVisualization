using System;
using System.Collections.Generic;
using System.Globalization;
using Lyricify.Lyrics.Models;

namespace WinExSpectrumTest.Services.Lyrics;

/// <summary>Enhanced LRC uses absolute &lt;mm:ss.xx&gt; syllable boundaries, unlike KRC/QRC durations.</summary>
internal static class EnhancedLrcParser
{
    internal static bool TryParse(string text, out LyricsData data)
    {
        var lines = new List<ILineInfo>();
        var openEnds = new List<SyllableInfo>();
        bool enhanced = false;
        int offset = 0;
        foreach (var raw in text.AsSpan().EnumerateLines())
        {
            var line = raw.Trim();
            if (line.StartsWith("[offset:", StringComparison.OrdinalIgnoreCase))
            {
                int close = line.IndexOf(']');
                if (close > 8) int.TryParse(line[8..close], NumberStyles.Integer, CultureInfo.InvariantCulture, out offset);
                continue;
            }
            var starts = new List<int>(1);
            while (line.StartsWith("[", StringComparison.Ordinal))
            {
                int end = line.IndexOf(']');
                if (end < 0 || !TryTime(line[1..end], out int start)) break;
                starts.Add(start);
                line = line[(end + 1)..];
            }
            if (starts.Count == 0) continue;
            var syllables = new List<ISyllableInfo>();
            int position = 0, previousEnd = 0, pendingStart = starts[0];
            bool hasBoundary = false;
            while (position < line.Length)
            {
                int next = line[position..].IndexOf('<');
                if (next < 0) break;
                int begin = position + next, close = line[(begin + 1)..].IndexOf('>');
                if (close < 0) break;
                close += begin + 1;
                if (!TryTime(line[(begin + 1)..close], out int boundary)) { position = begin + 1; continue; }
                if (boundary < pendingStart) { position = close + 1; continue; }
                if (begin > previousEnd)
                    syllables.Add(new SyllableInfo(line[previousEnd..begin].ToString(), pendingStart, Math.Max(pendingStart + 1, boundary)));
                pendingStart = boundary;
                previousEnd = position = close + 1;
                hasBoundary = true;
            }
            if (!hasBoundary || syllables.Count == 0 && previousEnd == line.Length)
            {
                foreach (int start in starts) lines.Add(new LineInfo(line.ToString(), start));
                continue;
            }
            enhanced = true;
            if (previousEnd < line.Length)
            {
                var last = new SyllableInfo(line[previousEnd..].ToString(), pendingStart, pendingStart + 1);
                syllables.Add(last);
                openEnds.Add(last);
            }
            for (int si = 0; si < starts.Count; si++)
            {
                int start = starts[si];
                int shift = start - starts[0];
                if (si == 0) lines.Add(new SyllableLineInfo(syllables));
                else
                {
                    var repeated = new List<ISyllableInfo>(syllables.Count);
                    foreach (var item in syllables)
                        repeated.Add(new SyllableInfo(item.Text, checked(item.StartTime + shift), checked(item.EndTime + shift)));
                    if (previousEnd < line.Length) openEnds.Add((SyllableInfo)repeated[^1]);
                    lines.Add(new SyllableLineInfo(repeated));
                }
            }
        }
        // Infer only a missing final boundary; all supplied syllable timestamps stay exact.
        int[] lineStarts = openEnds.Count == 0 ? [] : new int[lines.Count];
        for (int i = 0; i < lineStarts.Length; i++) lineStarts[i] = lines[i].StartTime!.Value;
        Array.Sort(lineStarts);
        foreach (var last in openEnds)
        {
            int end = last.StartTime <= int.MaxValue - 4000 ? last.StartTime + 4000 : int.MaxValue;
            int low = 0, high = lineStarts.Length;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (lineStarts[mid] <= last.StartTime) low = mid + 1; else high = mid;
            }
            if (low < lineStarts.Length) end = Math.Min(end, lineStarts[low]);
            last.EndTime = end;
        }
        foreach (var line in lines)
        {
            if (line is SyllableLineInfo timed)
            {
                if (offset != 0)
                    foreach (var syllable in timed.Syllables)
                    {
                        var item = (SyllableInfo)syllable;
                        item.StartTime = checked(item.StartTime - offset);
                        item.EndTime = checked(item.EndTime - offset);
                    }
                timed.RefreshProperties();
            }
            else if (offset != 0 && line is LineInfo plain) plain.StartTime = checked(plain.StartTime!.Value - offset);
        }
        data = new LyricsData { Lines = lines };
        return enhanced;
    }

    internal static bool TryTime(ReadOnlySpan<char> text, out int milliseconds)
    {
        milliseconds = 0;
        int colon = text.IndexOf(':'), dot = text.IndexOf('.');
        if (colon <= 0 || dot <= colon || text.Length - dot is < 2 or > 4
            || !int.TryParse(text[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || !int.TryParse(text[(colon + 1)..dot], NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) || seconds >= 60
            || !int.TryParse(text[(dot + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int fraction)) return false;
        int scale = text.Length - dot == 2 ? 100 : text.Length - dot == 3 ? 10 : 1;
        long value = ((long)minutes * 60 + seconds) * 1000 + fraction * scale;
        if (value >= int.MaxValue) return false;
        milliseconds = (int)value;
        return true;
    }
}
