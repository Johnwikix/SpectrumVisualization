using WinExSpectrumTest.Service;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaWord(string Text, double Start, double End);
/// <summary>Contains immutable lyric timing and text-derived chorus metadata.</summary>
internal sealed record FoliaLine(string Text, string Translation, double Start, double End, FoliaWord[] Words, bool WordSynced)
{
    internal bool IsChorus { get; init; }
    internal FoliaLineTiming Timing => FoliaLineTiming.From(this);
}

/// <summary>Immutable CPU data; published atomically. GPU resources remain owned by the render thread.</summary>
internal sealed record FoliaSong(string Title, string Artist, FoliaLine[] Lines, byte[] CoverRgba, int CoverWidth, int CoverHeight, Vector3[] Palette)
{
    // SMTC source, title, artist and album identify the track across asynchronous updates.
    internal string TrackKey { get; init; } = "";
    // A metadata-only snapshot is provisional until lookup succeeds, finds nothing, or fails.
    internal bool LyricsPending { get; init; }
    internal double Duration { get; init; }
    // null means an externally supplied snapshot still needs cold extraction; [] means no usable opaque pixels.
    internal Vector3[]? CoverColors { get; init; }
    // Native image shaders consume opaque RGB. Keep straight-alpha pixels for extraction,
    // and composite transparent artwork once on the cold worker before GPU upload.
    internal byte[]? DisplayCoverRgba { get; init; }
    internal FoliaKeywordRange[][] KeywordRanges { get; init; } = [];
    internal bool IsLightTheme { get; init; }

    internal static FoliaSong Empty
    {
        get;
    } = new("", "", [], [], 0, 0,
        [new(14 / 255f, 17 / 255f, 24 / 255f), new(184 / 255f, 214 / 255f, 204 / 255f), new(79 / 255f, 105 / 255f, 130 / 255f), new(196 / 255f, 122 / 255f, 156 / 255f)]);

    internal static bool SameTrack(FoliaSong a, FoliaSong b) => a.TrackKey.Length != 0 || b.TrackKey.Length != 0
        ? a.TrackKey == b.TrackKey : a.Title == b.Title && a.Artist == b.Artist;

    internal static bool SameCoverImage(FoliaSong a, FoliaSong b) => a.CoverWidth == b.CoverWidth
        && a.CoverHeight == b.CoverHeight
        && ReferenceEquals(a.DisplayCoverRgba ?? a.CoverRgba, b.DisplayCoverRgba ?? b.CoverRgba);

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
            int endMilliseconds = line.EndTime is int value && value > milliseconds ? value : milliseconds;
            bool hasExplicitEnd = endMilliseconds > milliseconds;
            double sourceEnd = hasExplicitEnd ? endMilliseconds / 1000d : start;
            // End is the row's display boundary, independent of the final word's real end.
            // Like HighlightEndMs in the reference player, retain completed lyrics until the next entrance.
            double end = sourceEnd;
            bool hasNextEntrance = false;
            for (int nextIndex = i + 1; nextIndex < source.Count; nextIndex++)
            {
                if (source[nextIndex].StartTime is not int next || next <= milliseconds) continue;
                end = Math.Max(end, next / 1000d);
                hasNextEntrance = true;
                break;
            }
            if (!hasNextEntrance)
                end = Math.Max(end, Math.Max(start + 4, duration));
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
            {
                // Line-only lyrics have no syllable timing. Build linguistic words for layout and
                // a bounded display timeline, without promoting them to real word synchronization.
                double estimatedWordEnd = hasExplicitEnd ? sourceEnd : Math.Min(end, start + 4);
                words.AddRange(FoliaWordSegmentation.CreateLineSyncedWords(line.Text, start, estimatedWordEnd));
            }
            string sub = (line as IFullLineInfo)?.ChineseTranslation ?? line.SubLine?.Text ?? "";
            result.Add(new(line.Text, sub, start, Math.Max(start + .01, end), words.ToArray(), synced));
        }
        result.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        if (translations is { Count: > 0 } && result.Count > 0)
            MatchTranslations(result, translations);
        // Folia's text fallback marks only the most repeated trimmed lines; no audio model is required.
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        int maximum = 1;
        foreach (var item in result)
        {
            string text = item.Text.Trim();
            if (text.Length < 2 || text == "......") continue;
            frequencies.TryGetValue(text, out int count);
            frequencies[text] = ++count;
            maximum = Math.Max(maximum, count);
        }
        if (maximum > 1)
            for (int i = 0; i < result.Count; i++)
                if (frequencies.TryGetValue(result[i].Text.Trim(), out int count) && count == maximum)
                    result[i] = result[i] with { IsChorus = true };
        return result.ToArray();
    }

    private static void MatchTranslations(List<FoliaLine> lines, List<ILineInfo> translations)
    {
        var matches = new (long Distance, string? Text)[lines.Count];
        foreach (var translation in translations)
        {
            if (translation.StartTime is not int time || string.IsNullOrWhiteSpace(translation.Text))
                continue;
            int lo = 0, hi = lines.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Milliseconds(lines[mid]) <= time) lo = mid + 1;
                else hi = mid;
            }
            long previous = lo > 0 ? time - Milliseconds(lines[lo - 1]) : long.MaxValue;
            long next = lo < lines.Count ? Milliseconds(lines[lo]) - time : long.MaxValue;
            long distance = Math.Min(previous, next);
            // Match each translation to its closest original once. Equal offsets give no preference.
            if (distance > 100 || previous == next) continue;
            int index = previous < next ? lo - 1 : lo;
            if (matches[index].Text == null || distance < matches[index].Distance)
                matches[index] = (distance, translation.Text);
        }
        for (int i = 0; i < matches.Length; i++)
            if (matches[i].Text is string text)
                lines[i] = lines[i] with { Translation = text };

        static long Milliseconds(FoliaLine line) => (long)Math.Round(line.Start * 1000);
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
            if (lyrics.Contains('\r'))
                lyrics = lyrics.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (var line in lyrics.AsSpan().EnumerateLines())
                if (Services.Lyrics.LyricsTextFile.IsKrcLine(line))
                    return Lyricify.Lyrics.Parsers.KrcParser.Parse(lyrics);
            if (lyrics.Contains('<') && Services.Lyrics.EnhancedLrcParser.TryParse(lyrics, out var enhanced)) return enhanced;
            if (Services.Lyrics.LyricsTextFile.TryNormalizeQrc(lyrics, out string normalized))
            {
                var qrc = Lyricify.Lyrics.Parsers.QrcParser.Parse(normalized);
                if (qrc.Lines?.Exists(static line => line is SyllableLineInfo { Syllables.Count: > 0 }) == true) return qrc;
            }
            return Lyricify.Lyrics.Parsers.LrcParser.Parse(lyrics);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException or IndexOutOfRangeException
            or System.Xml.XmlException or System.Text.Json.JsonException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            AppLog.Caught(ex);
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
        return new("Folia · 辞曲新境", "Spectrum shader preview", lines, cover, width, height, Empty.Palette) { Duration = 64 };
    }
}
