using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>ICU word boundaries match the dictionary-based Intl.Segmenter used by Folia.
/// The UTF-16 source stays pinned for the complete native iterator lifetime; this is a cold path.</summary>
internal static unsafe partial class FoliaWordSegmentation
{
    internal static int[] Boundaries(string text)
    {
        if (text.Length == 0)
            return [0];
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            return GraphemeBoundaries(text);
        fixed (char* characters = text)
        {
            int status = 0;
            nint iterator = Open(1, null, characters, text.Length, ref status);
            if (iterator == 0 || status > 0)
            {
                if (iterator != 0)
                    Close(iterator);
                return GraphemeBoundaries(text);
            }
            try
            {
                var boundaries = new List<int>();
                for (int next = First(iterator); next >= 0; next = Next(iterator))
                    boundaries.Add(next);
                return boundaries.ToArray();
            }
            finally { Close(iterator); }
        }
    }
    /// <summary>Builds contiguous layout words and an estimated display timeline for line-only lyrics.</summary>
    internal static FoliaWord[] CreateLineSyncedWords(string text, double start, double end)
    {
        if (string.IsNullOrWhiteSpace(text) || end <= start)
            return [];
        // A row of dots is Folia's interlude marker, not six independently placed words.
        if (text == "......")
            return [new(text, start, end)];
        int[] boundaries = Boundaries(text);
        if (!CompleteBoundaries(boundaries, text.Length))
            boundaries = GraphemeBoundaries(text);
        var pieces = new List<string>(boundaries.Length - 1);
        string leadingWhitespace = "";
        for (int i = 1; i < boundaries.Length; i++)
        {
            string piece = text[boundaries[i - 1]..boundaries[i]];
            if (string.IsNullOrWhiteSpace(piece))
            {
                // Keep the source text contiguous, but never create an invisible animated word.
                if (pieces.Count == 0) leadingWhitespace += piece;
                else pieces[^1] += piece;
            }
            else
            {
                pieces.Add(leadingWhitespace + piece);
                leadingWhitespace = "";
            }
        }
        var weights = new int[pieces.Count];
        int total = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            weights[i] = StringInfo.ParseCombiningCharacters(pieces[i]).Length;
            total += weights[i];
        }
        var result = new FoliaWord[pieces.Count];
        int consumed = 0;
        double cursor = start;
        for (int i = 0; i < pieces.Count; i++)
        {
            consumed += weights[i];
            double next = i + 1 == pieces.Count ? end : start + (end - start) * consumed / total;
            result[i] = new(pieces[i], cursor, next);
            cursor = next;
        }
        return result;
    }

    private static bool CompleteBoundaries(int[] boundaries, int length)
    {
        if (boundaries.Length < 2 || boundaries[0] != 0 || boundaries[^1] != length)
            return false;
        for (int i = 1; i < boundaries.Length; i++)
            if (boundaries[i] <= boundaries[i - 1]) return false;
        return true;
    }

    private static int[] GraphemeBoundaries(string text)
    {
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        Array.Resize(ref starts, starts.Length + 1);
        starts[^1] = text.Length;
        return starts;
    }
    [LibraryImport("icu.dll", EntryPoint = "ubrk_open")]
    private static partial nint Open(int type, byte* locale, char* text, int length, ref int status);
    [LibraryImport("icu.dll", EntryPoint = "ubrk_first")]
    private static partial int First(nint iterator);
    [LibraryImport("icu.dll", EntryPoint = "ubrk_next")]
    private static partial int Next(nint iterator);
    [LibraryImport("icu.dll", EntryPoint = "ubrk_close")]
    private static partial void Close(nint iterator);
}
