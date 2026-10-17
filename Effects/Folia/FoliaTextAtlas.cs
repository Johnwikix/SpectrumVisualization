using System;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.Geometry;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Text;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaGlyph(float U, float V, float Uw, float Vh, float Width, float Height, float Advance, float BearingX, float BearingY, float CanvasCenterY = 0, float LineHeight = 1.32f);
internal readonly record struct FoliaPreparedGlyph(FoliaGlyph Glyph, float Offset, double Start, double End, int Word, int Index);
internal sealed record FoliaPreparedLine(FoliaLine Source, FoliaPreparedGlyph[] Glyphs, float Width, FoliaPreparedGlyph[] Translation, float TranslationWidth)
{
    internal FoliaGlyph[] TiltItalic { get; init; } = [];
}

/// <summary>DirectWrite Bezier outlines become MTSDF once per song / font change; frame shaders reconstruct sharp coverage at the actual scale.</summary>
internal sealed class FoliaTextAtlas
{
    internal static FoliaTextAtlas Placeholder { get; } = new();

    private FoliaTextAtlas()
    {
        Seed = "";
        Width = Height = 1;
        Pixels = new byte[8];
        ShadowPixels = [0];
        CharacterGlyphs = new Dictionary<string, FoliaGlyph>();
        Lines = [];
        Title = Artist = MonetTitle = MonetLabel = [];
        ChatTimestamps = [];
        ChatTimestampWidths = [];
        ChatSenders = [];
    }
    private static long _nextIdentity;
    internal long Identity { get; } = System.Threading.Interlocked.Increment(ref _nextIdentity);
    internal void ReleaseDistancePixels() => Pixels = [];
    internal FoliaPreparedGlyph[] MonetTitle { get; }
    internal float MonetTitleWidth { get; }
    internal FoliaPreparedGlyph[][] ChatTimestamps { get; }
    internal float[] ChatTimestampWidths { get; }
    internal string Seed
    {
        get;
    }
    internal IReadOnlyDictionary<string, FoliaGlyph> CharacterGlyphs { get; private set; } = null!;
    internal FoliaPreparedGlyph[] MonetLabel
    {
        get;
    }
    internal FoliaChatSender[] ChatSenders
    {
        get;
    }
    private const float Em = 96;
    private const int Padding = 24;
    internal int Width
    {
        get;
    }
    internal int Height
    {
        get;
    }
    internal byte[] ShadowPixels { get; }
    internal byte[] Pixels
    {
        get; private set;
    }
    internal FoliaPreparedLine[] Lines
    {
        get;
    }
    internal FoliaPreparedGlyph[] Title
    {
        get;
    }
    internal FoliaPreparedGlyph[] Artist
    {
        get;
    }
    internal float TitleWidth
    {
        get;
    }
    internal float ArtistWidth
    {
        get;
    }

    internal FoliaTextAtlas(FoliaSong song, string family, int weight, CancellationToken token = default)
    {
        Seed = song.Title.StartsWith("Folia ·", StringComparison.Ordinal) ? "spectrum-reference" : song.Title + "\n" + song.Artist;
        var characters = new SortedSet<string>(StringComparer.Ordinal);
        AddCharacters(characters, song.Title);
        AddCharacters(characters, song.Artist);
        AddCharacters(characters, "MONET0123456789:");
        foreach (var line in song.Lines)
        {
            AddCharacters(characters, line.Text);
            AddCharacters(characters, line.Translation);
            foreach (var word in line.Words)
                AddCharacters(characters, word.Text);
        }
        using var device = new CanvasDevice();
        using var format = new CanvasTextFormat
        {
            FontFamily = family,
            FontSize = Em,
            FontWeight = new FontWeight { Weight = (ushort)Math.Clamp(weight, 100, 900) },
            WordWrapping = CanvasWordWrapping.NoWrap,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };
        // Mode-specific CSS headings and timestamps have fixed weights; retain their real outlines.
        var requests = new List<(string Text, int Weight, bool Italic)>();
        foreach (string text in characters)
            requests.Add((text, weight, false));
        if (weight != 600)
        {
            var titleCharacters = new SortedSet<string>(StringComparer.Ordinal);
            AddCharacters(titleCharacters, song.Title);
            foreach (string text in titleCharacters)
                requests.Add((text, 600, false));
        }
        if (weight != 500)
            foreach (char digit in "0123456789:")
                requests.Add((digit.ToString(), 500, false));
        foreach (string text in characters)
            requests.Add((text, weight, true));
        // A shelf atlas preserves actual glyph bounds, including combining marks and italic overhang.
        var entries = new List<(string Text, int Weight, bool Italic, int X, int Y, int Width, int Height, float Advance, float XBearing, float YBearing)>();
        Width = requests.Count > 256 ? 2048 : 1024;
        int x = 0, y = 0, row = 0;
        foreach (var request in requests)
        {
            token.ThrowIfCancellationRequested();
            string character = request.Text;
            format.FontWeight = new FontWeight { Weight = (ushort)request.Weight };
            format.FontStyle = request.Italic ? FontStyle.Italic : FontStyle.Normal;
            using var layout = new CanvasTextLayout(device, character, format, 2048, 256);
            var bounds = layout.DrawBounds;
            int w = Math.Max(1, (int)Math.Ceiling(bounds.Width) + Padding * 2);
            int h = Math.Max(1, (int)Math.Ceiling(bounds.Height) + Padding * 2);
            if (w > Width)
                continue;
            if (x + w > Width)
            {
                x = 0;
                y += row;
                row = 0;
            }
            entries.Add((character, request.Weight, request.Italic, x, y, w, h, (float)layout.LayoutBoundsIncludingTrailingWhitespace.Width,
                (float)bounds.X, (float)bounds.Y));
            x += w;
            row = Math.Max(row, h);
        }
        Height = 64;
        while (Height < y + row)
            Height *= 2;
        if (Height > 16384)
            throw new InvalidOperationException("The lyric glyph atlas exceeds the D3D12 texture limit.");
        var glyphs = new Dictionary<string, FoliaGlyph>(characters.Count, StringComparer.Ordinal);
        var titleGlyphs = weight == 600 ? glyphs : new Dictionary<string, FoliaGlyph>(StringComparer.Ordinal);
        var italicGlyphs = new Dictionary<string, FoliaGlyph>(characters.Count, StringComparer.Ordinal);
        var timestampGlyphs = weight == 500 ? glyphs : new Dictionary<string, FoliaGlyph>(StringComparer.Ordinal);
        Pixels = new byte[checked(Width * Height * 8)];
        var atlasPixels = MemoryMarshal.Cast<byte, Half>(Pixels.AsSpan());
        int maximumPixels = 0;
        foreach (var entry in entries)
            maximumPixels = Math.Max(maximumPixels, entry.Width * entry.Height * 4);
        var field = new float[maximumPixels];
        var coverage = new float[maximumPixels / 4];
        var horizontal = new float[maximumPixels / 4];
        var kernel = FoliaGlyphShadow.Kernel(Em * .09f);
        ShadowPixels = new byte[Width * Height];
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            format.FontWeight = new FontWeight { Weight = (ushort)entry.Weight };
            format.FontStyle = entry.Italic ? FontStyle.Italic : FontStyle.Normal;
            using var layout = new CanvasTextLayout(device, entry.Text, format, 2048, 256);
            using var outline = CanvasGeometry.CreateText(layout);
            byte[] tile = FoliaGlyphCache.Get(outline, entry.Width, entry.Height, Padding - entry.XBearing, Padding - entry.YBearing,
                field, coverage, horizontal, kernel);
            int tilePixels = entry.Width * entry.Height;
            var tileDistances = MemoryMarshal.Cast<byte, Half>(tile.AsSpan(0, tilePixels * 8));
            var tileShadow = tile.AsSpan(tilePixels * 8);
            for (int rowIndex = 0; rowIndex < entry.Height; rowIndex++)
            {
                tileDistances.Slice(rowIndex * entry.Width * 4, entry.Width * 4)
                    .CopyTo(atlasPixels.Slice(((entry.Y + rowIndex) * Width + entry.X) * 4, entry.Width * 4));
                tileShadow.Slice(rowIndex * entry.Width, entry.Width)
                    .CopyTo(ShadowPixels.AsSpan((entry.Y + rowIndex) * Width + entry.X, entry.Width));
            }
            var destinationGlyphs = entry.Italic ? italicGlyphs : entry.Weight == weight ? glyphs : entry.Weight == 600 ? titleGlyphs : timestampGlyphs;
            destinationGlyphs[entry.Text] = new((float)entry.X / Width, (float)entry.Y / Height,
                (float)entry.Width / Width, (float)entry.Height / Height,
                entry.Width / Em, entry.Height / Em, entry.Advance / Em,
                (entry.XBearing - Padding) / Em, (entry.YBearing - Padding) / Em, FoliaDistanceField.CanvasMiddleCenter(layout, Em), (float)layout.LayoutBounds.Height / Em);
        }
        CharacterGlyphs = glyphs;
        Lines = new FoliaPreparedLine[song.Lines.Length];
        for (int i = 0; i < Lines.Length; i++)
        {
            var line = song.Lines[i];
            var prepared = new List<FoliaPreparedGlyph>();
            var italics = new List<FoliaGlyph>();
            float offset = 0;
            int index = 0;
            for (int wi = 0; wi < line.Words.Length; wi++)
            {
                var word = line.Words[wi];
                var enumerator = StringInfo.GetTextElementEnumerator(word.Text);
                int count = StringInfo.ParseCombiningCharacters(word.Text).Length;
                int inWord = 0;
                while (enumerator.MoveNext())
                {
                    string text = enumerator.GetTextElement();
                    if (!glyphs.TryGetValue(text, out var glyph))
                        continue;
                    // Preserve true word intervals; graphemes subdivide only a real syllable's interval.
                    double start = line.WordSynced ? word.Start + (word.End - word.Start) * inWord / Math.Max(1, count) : line.Start;
                    double end = line.WordSynced ? word.Start + (word.End - word.Start) * (inWord + 1) / Math.Max(1, count) : line.End;
                    inWord++;
                    prepared.Add(new(glyph, offset, start, Math.Max(start + .001, end), wi, index++));
                    italics.Add(italicGlyphs.TryGetValue(text, out var italic) ? italic : glyph);
                    offset += glyph.Advance;
                }
            }
            var translation = PrepareText(line.Translation, glyphs, out float translationWidth);
            Lines[i] = new(line, prepared.ToArray(), offset, translation, translationWidth) { TiltItalic = italics.ToArray() };
        }
        MonetTitle = PrepareText(song.Title, titleGlyphs, out float monetTitleWidth);
        MonetTitleWidth = monetTitleWidth;
        ChatTimestamps = new FoliaPreparedGlyph[song.Lines.Length][];
        ChatTimestampWidths = new float[song.Lines.Length];
        for (int i = 0; i < song.Lines.Length; i++)
        {
            int seconds = (int)Math.Max(0, song.Lines[i].End);
            string timestamp = string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
            ChatTimestamps[i] = PrepareText(timestamp, timestampGlyphs, out float timestampWidth);
            ChatTimestampWidths[i] = timestampWidth;
        }
        Title = PrepareText(song.Title, glyphs, out float titleWidth);
        Artist = PrepareText(song.Artist, glyphs, out float artistWidth);
        TitleWidth = titleWidth;
        ArtistWidth = artistWidth;
        MonetLabel = PrepareText("MONET", glyphs, out _);
        ChatSenders = FoliaCappellaSenders.Build(song.Lines);
    }

    private static FoliaPreparedGlyph[] PrepareText(string text, Dictionary<string, FoliaGlyph> glyphs, out float width)
    {
        var result = new List<FoliaPreparedGlyph>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        width = 0;
        while (enumerator.MoveNext())
        {
            if (!glyphs.TryGetValue(enumerator.GetTextElement(), out var glyph))
                continue;
            result.Add(new(glyph, width, 0, double.MaxValue, 0, result.Count));
            width += glyph.Advance;
        }
        return result.ToArray();
    }

    private static void AddCharacters(SortedSet<string> characters, string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            characters.Add(enumerator.GetTextElement());
    }
}
