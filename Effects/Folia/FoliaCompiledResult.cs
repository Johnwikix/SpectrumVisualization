using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Large immutable JSON results remain compressed between mode switches, not as retained UTF-16 graphs.</summary>
internal sealed class FoliaCompiledResult
{
    private readonly string? _text;
    private readonly byte[]? _compressed;
    private readonly int _characters;
    internal int Bytes => _text != null ? _text.Length * sizeof(char) : _compressed!.Length;

    internal FoliaCompiledResult(string text, CancellationToken token)
    {
        _characters = text.Length;
        if (text.Length < 32768) { _text = text; return; }
        using var output = new MemoryStream();
        using (var compressed = new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new StreamWriter(compressed, Encoding.UTF8, 4096))
            for (int offset = 0; offset < text.Length; offset += 4096)
            {
                token.ThrowIfCancellationRequested();
                writer.Write(text.AsSpan(offset, Math.Min(4096, text.Length - offset)));
            }
        _compressed = output.ToArray();
    }

    internal string Read(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_text != null) return _text;
        return string.Create(_characters, (Data: _compressed!, Token: token), static (destination, state) =>
        {
            using var input = new MemoryStream(state.Data, writable: false);
            using var decompressed = new BrotliStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(decompressed, Encoding.UTF8, true, 4096);
            while (!destination.IsEmpty)
            {
                state.Token.ThrowIfCancellationRequested();
                int count = reader.Read(destination[..Math.Min(4096, destination.Length)]);
                if (count == 0) throw new InvalidDataException("Incomplete compiled lyric layout cache.");
                destination = destination[count..];
            }
        });
    }
}
