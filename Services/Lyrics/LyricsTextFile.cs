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
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBytes
        });
        var document = XDocument.Load(reader);
        foreach (var element in document.Descendants())
            if (element.Attribute("LyricContent") is { } content) return content.Value;
        return text;
    }
}
