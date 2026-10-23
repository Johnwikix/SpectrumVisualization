using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.Geometry;
using System.Runtime.InteropServices;
using Windows.UI;
using Windows.UI.Text;
using System.Numerics;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaGlyph(float U, float V, float Uw, float Vh, float Width, float Height, float Advance, float BearingX, float BearingY, float CanvasCenterY = 0, float LineHeight = 1.32f, int Page = 0);
internal readonly record struct FoliaPreparedGlyph(FoliaGlyph Glyph, float Offset, double Start, double End, int Word, int Index, Vector3? ActiveColor = null);
internal sealed record FoliaPreparedLine(FoliaLine Source, FoliaPreparedGlyph[] Glyphs, float Width, FoliaPreparedGlyph[] Translation, float TranslationWidth)
{
    internal FoliaGlyph[] TiltItalic { get; init; } = [];
}

/// <summary>Scene layouts reference reusable glyph tiles. A private CPU atlas remains available for capacity fallback.</summary>
internal sealed class FoliaTextAtlas : IDisposable
{
    private FoliaPixelBuffer? _distancePixels, _shadowPixels;
    private int _owners = 1;
    private int _initialOwnerReleased;
    private readonly FoliaGlyphStore? _store;
    private IReadOnlyDictionary<string, FoliaGlyph> _italicGlyphs = new Dictionary<string, FoliaGlyph>();
    internal FoliaCachedGlyph[] SharedGlyphs { get; private set; } = [];
    internal bool IsShared => _store != null;
    internal static FoliaTextAtlas Placeholder { get; } = new();

    private FoliaTextAtlas()
    {
        Seed = "";
        Width = Height = 1;
        _distancePixels = new(8);
        _shadowPixels = new(1);
        CharacterGlyphs = new Dictionary<string, FoliaGlyph>();
        Lines = [];
        Title = Artist = MonetTitle = [];
        ChatTimestamps = [];
        ChatTimestampWidths = [];
        ChatSenders = [];
    }
    private static long _nextIdentity;
    internal long Identity { get; } = System.Threading.Interlocked.Increment(ref _nextIdentity);
    internal void ReleaseDistancePixels()
    {
        DetachDistancePixels()?.Dispose();
    }
    internal FoliaPixelBuffer? DetachDistancePixels() => Interlocked.Exchange(ref _distancePixels, null);
    internal FoliaTextAtlas Retain()
    {
        if (Interlocked.Increment(ref _owners) <= 1)
        {
            Interlocked.Decrement(ref _owners);
            throw new ObjectDisposedException(nameof(FoliaTextAtlas));
        }
        return this;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _initialOwnerReleased, 1) == 0) ReleaseLease();
    }
    // Each prepared scene releases its own lease once. Dispose separately owns the
    // constructor/preparation lease, and remains idempotent even while a scene is active.
    internal void ReleaseLease()
    {
        if (Interlocked.Decrement(ref _owners) != 0) return;
        ReleaseDistancePixels();
        _shadowPixels?.Dispose();
        _shadowPixels = null;
        _store?.Release(SharedGlyphs);
    }
    internal FoliaPreparedGlyph[] MonetTitle { get; }
    internal FoliaWordColor[] KeywordRules { get; } = [];
    internal float MonetTitleWidth { get; }
    internal FoliaPreparedGlyph[][] ChatTimestamps { get; }
    internal float[] ChatTimestampWidths { get; }
    internal string Seed
    {
        get;
    }
    internal IReadOnlyDictionary<string, FoliaGlyph> CharacterGlyphs { get; private set; } = null!;
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
    internal Span<byte> ShadowPixels => _shadowPixels == null ? [] : _shadowPixels.Span;
    internal Span<byte> Pixels => _distancePixels == null ? [] : _distancePixels.Span;
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

    internal bool HasItalicGlyphs(IReadOnlySet<string> characters)
    {
        foreach (string character in characters)
            if (!_italicGlyphs.ContainsKey(character)) return false;
        return true;
    }
    internal FoliaTextAtlas(FoliaSong song, string family, int weight, CancellationToken token = default, bool includeItalics = false,
        FoliaGlyphStore? store = null, IReadOnlySet<string>? italicCharacters = null)
    {
        _store = store;
        try
        {
            Seed = song.Title.StartsWith("Folia ·", StringComparison.Ordinal) ? "spectrum-reference" : song.Title + "\n" + song.Artist;
            if (song.KeywordSignature.Length > 0 && FoliaThemePlanner.TryParseRules(song.KeywordSignature, out var rules, out _)) KeywordRules = rules;
            var characters = new SortedSet<string>(StringComparer.Ordinal);
            AddCharacters(characters, song.Title);
            AddCharacters(characters, song.Artist);
            AddCharacters(characters, "0123456789:");
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
            if (includeItalics)
                foreach (string text in characters)
                    if (italicCharacters == null || italicCharacters.Contains(text)) requests.Add((text, weight, true));
            var glyphs = new Dictionary<string, FoliaGlyph>(characters.Count, StringComparer.Ordinal);
            var titleGlyphs = weight == 600 ? glyphs : new Dictionary<string, FoliaGlyph>(StringComparer.Ordinal);
            var italicGlyphs = new Dictionary<string, FoliaGlyph>(characters.Count, StringComparer.Ordinal);
            var timestampGlyphs = weight == 500 ? glyphs : new Dictionary<string, FoliaGlyph>(StringComparer.Ordinal);
            if (store != null)
            {
                Width = Height = FoliaGlyphStore.PageSize;
                SharedGlyphs = new FoliaCachedGlyph[requests.Count];
                Parallel.For(0, requests.Count, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = 4 },
                    () => new GlyphWorker(family, 0), (i, _, worker) =>
                    {
                        var request = requests[i];
                        var key = new FoliaGlyphKey(family, request.Weight, request.Italic, request.Text);
                        SharedGlyphs[i] = store.Acquire(key, () =>
                        {
                            worker.Format.FontWeight = new FontWeight { Weight = (ushort)request.Weight };
                            worker.Format.FontStyle = request.Italic ? FontStyle.Italic : FontStyle.Normal;
                            using var layout = new CanvasTextLayout(device, request.Text, worker.Format, 2048, 256);
                            var bounds = layout.DrawBounds;
                            using var geometry = CanvasGeometry.CreateText(layout);
                            float center = FoliaDistanceField.CanvasMiddleCenter(layout, Em);
                            return new FoliaGlyphSource(new FoliaGlyphOutline(geometry),
                                Math.Max(1, (int)Math.Ceiling(bounds.Width) + Padding * 2),
                                Math.Max(1, (int)Math.Ceiling(bounds.Height) + Padding * 2),
                                Padding - (float)bounds.X, Padding - (float)bounds.Y,
                                (float)layout.LayoutBoundsIncludingTrailingWhitespace.Width,
                                (float)bounds.X - Padding, (float)bounds.Y - Padding,
                                center, (float)layout.LayoutBounds.Height / Em);
                        });
                        return worker;
                    }, static worker => worker.Dispose());
                for (int i = 0; i < requests.Count; i++)
                {
                    var request = requests[i];
                    var destination = request.Italic ? italicGlyphs : request.Weight == weight ? glyphs : request.Weight == 600 ? titleGlyphs : timestampGlyphs;
                    destination[request.Text] = SharedGlyphs[i].Glyph;
                }
                store.PrepareCpuTiles(SharedGlyphs, token);
            }
            else
            {
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
                _distancePixels = new(checked(Width * Height * 8));
                var atlasPixels = MemoryMarshal.Cast<byte, Half>(Pixels);
                int maximumPixels = 0;
                foreach (var entry in entries)
                    maximumPixels = Math.Max(maximumPixels, entry.Width * entry.Height * 4);
                var kernel = FoliaGlyphShadow.Kernel(Em * .09f);
                _shadowPixels = new(checked(Width * Height));
                var tiles = new byte[entries.Count][];
                var metrics = new FoliaGlyph[entries.Count];
                // Outline conversion is CPU work. Bound concurrency and give each worker its own mutable format/scratch.
                // Only this cold preparation path uses Parallel; atlas packing and dictionaries remain single-owner.
                Parallel.For(0, entries.Count, new ParallelOptions
                {
                    CancellationToken = token, MaxDegreeOfParallelism = Math.Min(4, Math.Max(1, Environment.ProcessorCount / 2))
                }, () => new GlyphWorker(family, maximumPixels), (i, _, worker) =>
                {
                    var entry = entries[i];
                    worker.Format.FontWeight = new FontWeight { Weight = (ushort)entry.Weight };
                    worker.Format.FontStyle = entry.Italic ? FontStyle.Italic : FontStyle.Normal;
                    using var layout = new CanvasTextLayout(device, entry.Text, worker.Format, 2048, 256);
                    using var outline = CanvasGeometry.CreateText(layout);
                    tiles[i] = FoliaGlyphCache.Get(outline, entry.Width, entry.Height, Padding - entry.XBearing, Padding - entry.YBearing,
                        worker.Field, worker.Coverage, worker.Horizontal, kernel);
                    metrics[i] = new((float)entry.X / Width, (float)entry.Y / Height,
                        (float)entry.Width / Width, (float)entry.Height / Height,
                        entry.Width / Em, entry.Height / Em, entry.Advance / Em,
                        (entry.XBearing - Padding) / Em, (entry.YBearing - Padding) / Em,
                        FoliaDistanceField.CanvasMiddleCenter(layout, Em), (float)layout.LayoutBounds.Height / Em);
                    return worker;
                }, static worker => worker.Dispose());
                for (int i = 0; i < entries.Count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var entry = entries[i];
                    byte[] tile = tiles[i];
                    int tilePixels = entry.Width * entry.Height;
                    var tileDistances = MemoryMarshal.Cast<byte, Half>(tile.AsSpan(0, tilePixels * 8));
                    var tileShadow = tile.AsSpan(tilePixels * 8);
                    for (int rowIndex = 0; rowIndex < entry.Height; rowIndex++)
                    {
                        tileDistances.Slice(rowIndex * entry.Width * 4, entry.Width * 4)
                            .CopyTo(atlasPixels.Slice(((entry.Y + rowIndex) * Width + entry.X) * 4, entry.Width * 4));
                        tileShadow.Slice(rowIndex * entry.Width, entry.Width)
                            .CopyTo(ShadowPixels.Slice((entry.Y + rowIndex) * Width + entry.X, entry.Width));
                    }
                    var destinationGlyphs = entry.Italic ? italicGlyphs : entry.Weight == weight ? glyphs : entry.Weight == 600 ? titleGlyphs : timestampGlyphs;
                    destinationGlyphs[entry.Text] = metrics[i];
                }
            }
            CharacterGlyphs = glyphs;
            _italicGlyphs = italicGlyphs;
            Lines = new FoliaPreparedLine[song.Lines.Length];
            for (int i = 0; i < Lines.Length; i++)
            {
                var line = song.Lines[i];
                var prepared = new List<FoliaPreparedGlyph>();
                var italics = new List<FoliaGlyph>();
                float offset = 0;
                int index = 0;
                int textOffset = 0;
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
                        Vector3? activeColor = null;
                        if (i < song.KeywordRanges.Length)
                            foreach (var range in song.KeywordRanges[i])
                                if (textOffset + enumerator.ElementIndex + text.Length > range.Start && textOffset + enumerator.ElementIndex < range.End)
                                {
                                    activeColor = range.Color;
                                    break;
                                }
                        prepared.Add(new(glyph, offset, start, Math.Max(start + .001, end), wi, index++, activeColor));
                        italics.Add(italicGlyphs.TryGetValue(text, out var italic) ? italic : glyph);
                        offset += glyph.Advance;
                    }
                    textOffset += word.Text.Length;
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
            ChatSenders = FoliaCappellaSenders.Build(song.Lines);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private sealed class GlyphWorker(string family, int pixels) : IDisposable
    {
        internal CanvasTextFormat Format { get; } = new()
        {
            FontFamily = family, FontSize = Em, WordWrapping = CanvasWordWrapping.NoWrap,
            VerticalAlignment = CanvasVerticalAlignment.Top
        };
        internal float[] Field { get; } = new float[pixels];
        internal float[] Coverage { get; } = new float[pixels / 4];
        internal float[] Horizontal { get; } = new float[pixels / 4];
        public void Dispose() => Format.Dispose();
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
