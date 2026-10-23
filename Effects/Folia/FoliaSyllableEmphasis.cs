using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaEmphasisSpan(int Start, int End, float Strength);

/// <summary>Duration-based emphasis only. Line timing is never promoted into invented syllable timing.</summary>
internal static class FoliaSyllableEmphasis
{
    private readonly record struct Candidate(int Start, int End, double PerSyllable, double Duration);

    internal static FoliaEmphasisSpan[][] Analyze(FoliaLine[] lines, CancellationToken token)
    {
        var all = new List<double>();
        foreach (var line in lines)
        {
            token.ThrowIfCancellationRequested();
            if (line.WordSynced)
                foreach (var word in line.Words)
                {
                    int units = CountSyllables(word.Text);
                    double duration = word.End - word.Start;
                    if (units > 0 && double.IsFinite(duration) && duration >= .04 && duration <= 30) all.Add(duration / units);
                }
        }
        if (all.Count < 3) return [];
        all.Sort();
        double songMedian = all[all.Count / 2];
        FoliaEmphasisSpan[][] result = [];
        for (int li = 0; li < lines.Length; li++)
        {
            token.ThrowIfCancellationRequested();
            var line = lines[li];
            if (!line.WordSynced) continue;
            var candidates = new List<Candidate>(line.Words.Length);
            int offset = 0;
            foreach (var word in line.Words)
            {
                int units = CountSyllables(word.Text);
                double duration = word.End - word.Start;
                if (units > 0 && double.IsFinite(duration) && duration >= .04 && duration <= 30)
                    candidates.Add(new(offset, offset + word.Text.Length, duration / units, duration));
                offset += word.Text.Length;
            }
            if (candidates.Count < 3) continue;
            candidates.Sort(static (a, b) => a.PerSyllable != b.PerSyllable
                ? b.PerSyllable.CompareTo(a.PerSyllable) : a.Start.CompareTo(b.Start));
            double localMedian = candidates[candidates.Count / 2].PerSyllable;
            double threshold = Math.Max(.48, Math.Max(localMedian, songMedian * .75) * 1.6);
            int maximum = Math.Max(1, candidates.Count / 4);
            var selected = new List<FoliaEmphasisSpan>(maximum);
            foreach (var candidate in candidates)
                if (selected.Count < maximum && candidate.Duration >= .65 && candidate.PerSyllable >= threshold)
                    selected.Add(new(candidate.Start, candidate.End, (float)Math.Clamp(candidate.PerSyllable / threshold - .5, .5, 1)));
            if (selected.Count == 0) continue;
            selected.Sort(static (a, b) => a.Start.CompareTo(b.Start));
            if (result.Length == 0) result = new FoliaEmphasisSpan[lines.Length][];
            result[li] = selected.ToArray();
        }
        for (int i = 0; i < result.Length; i++) result[i] ??= [];
        return result;
    }

    // CJK letters count as mora-sized units. Latin vowel groups approximate syllable
    // count so a long multi-syllable word is not emphasized merely because it is longer.
    internal static int CountSyllables(string text)
    {
        int units = 0, groups = 0, latinLetters = 0;
        char last = '\0'; bool vowel = false;
        foreach (var rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            if (value is >= 'A' and <= 'Z') value += 32;
            if (value is >= 'a' and <= 'z')
            {
                bool current = value is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
                if (current && !vowel) groups++;
                vowel = current;
                latinLetters++;
                last = (char)value;
            }
            else if (Rune.IsLetter(rune))
            {
                if (latinLetters > 0) units += Math.Max(1, groups - (last == 'e' && latinLetters > 3 && groups > 1 ? 1 : 0));
                groups = latinLetters = 0;
                vowel = false;
                units++;
            }
            else if (value != '\'' && latinLetters > 0)
            {
                units += Math.Max(1, groups - (last == 'e' && latinLetters > 3 && groups > 1 ? 1 : 0));
                groups = latinLetters = 0;
                vowel = false;
            }
        }
        if (latinLetters > 0) units += Math.Max(1, groups - (last == 'e' && latinLetters > 3 && groups > 1 ? 1 : 0));
        return units;
    }

    internal static FoliaKeywordRange[][] Colorize(FoliaEmphasisSpan[][] spans, Vector3[] palette, uint seed, bool light)
    {
        if (spans.Length == 0) return [];
        var result = new FoliaKeywordRange[spans.Length][];
        for (int li = 0; li < spans.Length; li++)
        {
            var row = result[li] = new FoliaKeywordRange[spans[li].Length];
            for (int i = 0; i < row.Length; i++)
            {
                var span = spans[li][i];
                row[i] = new(span.Start, span.End, FoliaEmphasisColor.Create(palette, seed + (uint)(li * 17 + i), span.Strength, light));
            }
        }
        return result;
    }
}
