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
