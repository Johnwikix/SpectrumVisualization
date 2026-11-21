using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace WinExSpectrumTest.Services.Lyrics;

internal static class LyricsTextFile
{
    internal const int MaxBytes = 2 * 1024 * 1024;

    internal static async Task<string> ReadAsync(string path, CancellationToken token)
    {
        // Permit the player to replace/save the file; a partial JSON/lyric is rejected by the caller.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
        if (stream.Length > MaxBytes) throw new FormatException("Lyrics file exceeds the size limit.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        using var reader = new StreamReader(new MemoryStream(bytes, false), Encoding.UTF8, true);
        return await reader.ReadToEndAsync(token).ConfigureAwait(false);
    }

    internal static string UnwrapQrc(string text)
    {
        if (!text.TrimStart().StartsWith('<')) return text;
        if (text.Length > MaxBytes) throw new FormatException("Lyrics file exceeds the size limit.");
        // The lyric is an attribute, so XML's default normalization would replace physical
        // line endings with spaces. Keep them while the reader still validates and decodes XML.
        using var source = new XmlTextReader(new StringReader(text))
        {
            Normalization = false, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null
        };
        using var reader = XmlReader.Create(source, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes
        });
        var document = XDocument.Load(reader);
        foreach (var element in document.Descendants())
            if (element.Attribute("LyricContent") is { } content) return content.Value;
        return text;
    }

    internal static bool TryNormalizeQrc(string text, out string normalized)
    {
        StringBuilder? output = null;
        int copied = 0;
        bool found = false;
        var source = text.AsSpan();
        foreach (var raw in source.EnumerateLines())
        {
            var line = raw.TrimStart();
            int header = FirstQrcHeader(line);
            if (header < 0)
                continue;
            found = true;
            source.Overlaps(line, out int offset);

            // Some suppliers and old caches have already flattened the XML attribute.
            // Restore the leading metadata tags before passing the rows to the QRC parser.
            for (int i = 0; i < header;)
            {
                int end = i + line[i..header].IndexOf(']') + 1;
                i = end;
                while (i < header && char.IsWhiteSpace(line[i])) i++;
                InsertBreak(offset + end, offset + i);
            }

            int start = header;
            int search = header + TimingPairLength(line[header..], '[', ']');
            while (search < line.Length)
            {
                int next = line[search..].IndexOf('[');
                if (next < 0) break;
                next += search;
                int length = TimingPairLength(line[next..], '[', ']', out int time, out int duration);
                var preceding = line[start..next].TrimEnd();
                if (length > 0 && EndsWithTimedSyllable(preceding)
                    && StartsTimedRow(line[(next + length)..], time, duration))
                {
                    InsertBreak(offset + start + preceding.Length, offset + next);
                    start = next;
                    search = next + length;
                }
                else
                    search = next + 1;
            }
        }
        normalized = output?.Append(text.AsSpan(copied)).ToString() ?? text;
        return found;

        void InsertBreak(int after, int before)
        {
            output ??= new StringBuilder(text.Length);
            output.Append(text.AsSpan(copied, after - copied)).Append('\n');
            copied = before;
        }
    }

    internal static bool IsKrcLine(ReadOnlySpan<char> line)
    {
        line = line.TrimStart();
        int header = TimingPairLength(line, '[', ']');
        if (header == 0) return false;
        var word = line[header..].TrimStart();
        int end = word.IndexOf('>');
        if (end <= 0 || word[0] != '<') return false;
        var values = word[1..end];
        int first = values.IndexOf(','), last = values.LastIndexOf(',');
        return first >= 0 && first != last && int.TryParse(values[..first], out _)
            && int.TryParse(values[(first + 1)..last], out _) && int.TryParse(values[(last + 1)..], out _);
    }

    private static int FirstQrcHeader(ReadOnlySpan<char> line)
    {
        int offset = 0;
        while (offset < line.Length && line[offset] == '[')
        {
            if (TimingPairLength(line[offset..], '[', ']') > 0)
                return offset;
            int end = line[offset..].IndexOf(']');
            if (end < 3 || !char.IsAsciiLetter(line[offset + 1]) || !line.Slice(offset + 1, end - 1).Contains(':'))
                break;
            offset += end + 1;
            while (offset < line.Length && char.IsWhiteSpace(line[offset])) offset++;
        }
        return -1;
    }

    private static bool EndsWithTimedSyllable(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty || text[^1] != ')') return false;
        // Two Int32 values and the three separators occupy at most 23 characters.
        var tail = text[Math.Max(0, text.Length - 23)..];
        int start = tail.LastIndexOf('(');
        return start >= 0 && TimingPairLength(tail[start..], '(', ')') == tail.Length - start;
    }

    private static bool StartsTimedRow(ReadOnlySpan<char> text, int time, int duration)
    {
        // Numeric brackets can also be sung text. Require a following syllable
        // whose timestamp belongs to this candidate row.
        while (!text.IsEmpty)
        {
            int start = text.IndexOf('(');
            if (start < 0) return false;
            var token = text[start..];
            if (TimingPairLength(token, '(', ')', out int word, out _) > 0)
                return word >= time && word <= (long)time + duration;
            text = token[1..];
        }
        return false;
    }

    private static int TimingPairLength(ReadOnlySpan<char> text, char open, char close) =>
        TimingPairLength(text, open, close, out _, out _);

    private static int TimingPairLength(ReadOnlySpan<char> text, char open, char close, out int position, out int length)
    {
        position = length = 0;
        if (text.IsEmpty || text[0] != open) return 0;
        int comma = DigitEnd(text, 1);
        if (comma == 1 || comma >= text.Length || text[comma] != ',') return 0;
        int end = DigitEnd(text, comma + 1);
        if (end == comma + 1 || end >= text.Length || text[end] != close) return 0;
        return int.TryParse(text[1..comma], out position) && int.TryParse(text[(comma + 1)..end], out length) ? end + 1 : 0;
    }

    private static int DigitEnd(ReadOnlySpan<char> text, int start)
    {
        int end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end])) end++;
        return end;
    }
}
