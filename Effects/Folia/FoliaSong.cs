using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaWord(string Text, double Start, double End);
internal sealed record FoliaLine(string Text, string Translation, double Start, double End, FoliaWord[] Words, bool WordSynced);

/// <summary>Immutable CPU data; published atomically. GPU resources remain owned by the render thread.</summary>
internal sealed record FoliaSong(string Title, string Artist, FoliaLine[] Lines, byte[] CoverRgba, int CoverWidth, int CoverHeight, Vector3[] Palette)
{
    internal static FoliaSong Empty
    {
        get;
    } = new("", "", [], [], 0, 0,
        [new(14 / 255f, 17 / 255f, 24 / 255f), new(184 / 255f, 214 / 255f, 204 / 255f), new(79 / 255f, 105 / 255f, 130 / 255f), new(196 / 255f, 122 / 255f, 156 / 255f)]);

    internal static FoliaLine[] Parse(string lyrics, string? translation, double duration)
    {
        var data = ParseRaw(lyrics);
        if (data?.Lines is not { Count: > 0 } source)
            return [];
        var translations = string.IsNullOrWhiteSpace(translation) ? null : ParseRaw(translation)?.Lines;
        var result = new List<FoliaLine>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            var line = source[i];
            if (string.IsNullOrWhiteSpace(line.Text) || line.StartTime is not int milliseconds)
                continue;
            double start = milliseconds / 1000d;
            double end = line.EndTime is int endMs && endMs > milliseconds ? endMs / 1000d :
                i + 1 < source.Count && source[i + 1].StartTime is int next && next > milliseconds ? next / 1000d : Math.Max(start + 4, duration);
            var words = new List<FoliaWord>();
            bool synced = line is SyllableLineInfo { Syllables.Count: > 0 };
            if (line is SyllableLineInfo syllable)
            {
                foreach (var word in syllable.Syllables)
                {
                    if (word is FullSyllableInfo full)
                    {
                        foreach (var part in full.SubItems)
                            words.Add(new(part.Text, part.StartTime / 1000d, Math.Max(part.StartTime + 1, part.EndTime) / 1000d));
                    }
                    else
                        words.Add(new(word.Text, word.StartTime / 1000d, Math.Max(word.StartTime + 1, word.EndTime) / 1000d));
                }
            }
            if (words.Count == 0)
                words.Add(new(line.Text, start, end));
            string sub = (line as IFullLineInfo)?.ChineseTranslation ?? line.SubLine?.Text ?? "";
            if (translations != null)
                foreach (var translated in translations)
                    if (translated.StartTime is int ts && Math.Abs(ts - milliseconds) <= 100)
                    {
                        sub = translated.Text;
                        break;
                    }
            result.Add(new(line.Text, sub, start, Math.Max(start + .01, end), words.ToArray(), synced));
        }
        result.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        return result.ToArray();
    }

    private static LyricsData? ParseRaw(string lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics) || lyrics.Length > Services.Lyrics.LyricsTextFile.MaxBytes) return null;
        try
        {
            // TypeHelper.GetLyricsTypes is a stub in the vendored library. Detect the four player formats explicitly.
            if (lyrics.AsSpan().TrimStart().StartsWith("<", StringComparison.Ordinal))
            {
                string unwrapped = Services.Lyrics.LyricsTextFile.UnwrapQrc(lyrics);
                if (unwrapped == lyrics) return Lyricify.Lyrics.Parsers.TtmlParser.Parse(lyrics);
                lyrics = unwrapped;
            }
            foreach (var line in lyrics.AsSpan().EnumerateLines())
            {
                int end = line.IndexOf(']');
                if (end > 0 && line[..end].Contains(',') && line[(end + 1)..].TrimStart().StartsWith("<", StringComparison.Ordinal))
                    return Lyricify.Lyrics.Parsers.KrcParser.Parse(lyrics);
            }
            var qrc = Lyricify.Lyrics.Parsers.QrcParser.Parse(lyrics);
            if (qrc.Lines?.Exists(static line => line is SyllableLineInfo { Syllables.Count: > 0 }) == true) return qrc;
            return Lyricify.Lyrics.Parsers.LrcParser.Parse(lyrics);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException
            or System.Xml.XmlException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return null;
        }
    }

    internal static FoliaSong Preview()
    {
        string[] text = ["让光掠过漫长的夜", "在每一个音符之间", "我们听见星河的回响", "时间化成一片流动的海", "风穿过安静的街道", "把未说出口的话点亮", "沿着旋律慢慢远行", "每次呼吸都有新的颜色", "这一刻让世界安静下来", "愿所有微光都找到方向", "再听一次心跳的声音", "让故事停在最温柔的地方"];
        var lines = new FoliaLine[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            var graphemes = StringInfo.ParseCombiningCharacters(text[i]);
            var words = new FoliaWord[graphemes.Length];
            double start = 2 + i * 5;
            for (int g = 0; g < words.Length; g++)
                words[g] = new(text[i].Substring(graphemes[g], (g + 1 < graphemes.Length ? graphemes[g + 1] : text[i].Length) - graphemes[g]), start + g * 4d / words.Length, start + (g + 1) * 4d / words.Length);
            lines[i] = new(text[i], "", start, start + 4.4, words, true);
        }
        const int width = 256, height = 256;
        var cover = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4;
                cover[p] = (byte)(70 + x * .35);
                cover[p + 1] = (byte)(105 + y * .28);
                cover[p + 2] = (byte)(140 + (Math.Sin(x * .025 + y * .013) + 1) * 40);
                cover[p + 3] = 255;
            }
        return new("Folia · 辞曲新境", "Spectrum shader preview", lines, cover, width, height, Empty.Palette);
    }
}
