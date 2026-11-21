using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jint;
namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold compilation boundary for the embedded original Folia layout algorithms.
/// Only immutable native plans cross into the allocation-free D3D12 frame loop.</summary>
internal static partial class FoliaSourcePlanner
{
    private static string Hex(Vector3 color) => $"#{(int)MathF.Round(color.X * 255):x2}{(int)MathF.Round(color.Y * 255):x2}{(int)MathF.Round(color.Z * 255):x2}";
    // Jint's prepared scripts are immutable and shareable; each plan still uses an isolated
    // engine, so song data and planner globals cannot leak into a later song.
    private static readonly Lazy<Prepared<Acornima.Ast.Script>> Source = new(() => Engine.PrepareScript(LoadSource()));
    private static readonly Lazy<Prepared<Acornima.Ast.Script>> SonnetSource = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Spectrum.Folia.sonnet-graphics.js")!;
        using var reader = new StreamReader(stream);
        return Engine.PrepareScript(reader.ReadToEnd());
    });
    private static readonly Lazy<Prepared<Acornima.Ast.Script>> NativeStagesSource = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Spectrum.Folia.native-stages.js")!;
        using var reader = new StreamReader(stream);
        return Engine.PrepareScript(reader.ReadToEnd());
    });

    internal static JsonDocument CompileNativeStage(FoliaTextAtlas atlas, in FoliaSceneRequest request, string function, CancellationToken token,
        IReadOnlyDictionary<string, int>? strokeCounts = null)
    {
        string input = CreateInput(atlas, request.Width, request.Height, request.Scale, request.Family,
            request.Weight, request.Song.Palette, request.Song, strokeCounts, token);
        return JsonDocument.Parse(Compile(input, function, token));
    }

    // Meshes have a separate bounded numeric cache. Compress larger JSON layouts so
    // a song's mode tour does not retain every expanded UTF-16 plan simultaneously.
    private const int ResultLimit = 4 * 1024 * 1024, EntryLimit = 16;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<(string Function, string Input), FoliaCompiledResult> Results = new();
    private static readonly Queue<(string Function, string Input)> ResultOrder = new();
    private static int _resultBytes;

    /// <summary>Reuses the last decoded layout for rapid re-entry while it survives in the managed heap.</summary>
    internal sealed class ParsedPlanCache<TPlan>(string function) where TPlan : class
    {
        private readonly record struct InputDigest(ulong Part1, ulong Part2, ulong Part3, ulong Part4)
        {
            internal static InputDigest Create(string input)
            {
                Span<byte> digest = stackalloc byte[32];
                SHA256.HashData(MemoryMarshal.AsBytes(input.AsSpan()), digest);
                return new(BinaryPrimitives.ReadUInt64LittleEndian(digest),
                    BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]));
            }
        }

        private sealed record Entry(InputDigest Input, WeakReference<TPlan> Plan);
        private readonly ConditionalWeakTable<FoliaTextAtlas, Entry> _plans = new();

        internal TPlan Read(FoliaTextAtlas atlas, string input, CancellationToken token,
            Func<FoliaTextAtlas, JsonElement, CancellationToken, TPlan> parse)
        {
            token.ThrowIfCancellationRequested();
            var key = InputDigest.Create(input);
            if (_plans.TryGetValue(atlas, out var existing) && existing.Input == key
                && existing.Plan.TryGetTarget(out var cached))
                return cached;
            string output = Compile(input, function, token);
            using var document = JsonDocument.Parse(output);
            TPlan plan = parse(atlas, document.RootElement, token);
            token.ThrowIfCancellationRequested();
            _plans.AddOrUpdate(atlas, new(key, new(plan)));
            return plan;
        }
    }

    internal static void ClearResults()
    {
        lock (CacheGate)
        {
            Results.Clear();
            ResultOrder.Clear();
            _resultBytes = 0;
            ClearPendoloResult();
        }
    }
    internal static string CreateInput(FoliaTextAtlas atlas, float width, float height, float fontScale, string family, int weight, Vector3[]? palette = null, FoliaSong? nativeSong = null,
        IReadOnlyDictionary<string, int>? strokeCounts = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var body = GetInputBody(atlas, token);
        var input = body == null ? new ArrayBufferWriter<byte>()
            : new ArrayBufferWriter<byte>(checked(body.Widths.Length + body.Lines.Length + 512));
        using (var w = new Utf8JsonWriter(input))
        {
            w.WriteStartObject();
            if (nativeSong != null)
            {
                w.WriteString("title", nativeSong.Title);
                w.WriteString("artist", nativeSong.Artist);
                w.WriteNumber("duration", FoliaShotTimeline.SongEnd(nativeSong));
            }
            if (strokeCounts != null)
            {
                w.WriteStartObject("strokeCounts");
                foreach (var pair in strokeCounts)
                    w.WriteNumber(pair.Key, pair.Value);
                w.WriteEndObject();
            }
            w.WriteNumber("width", width);
            w.WriteNumber("height", height);
            w.WriteNumber("fontScale", fontScale);
            w.WriteString("fontFamily", family);
            w.WriteNumber("fontWeight", weight);
            if (palette != null)
            {
                w.WriteStartObject("theme");
                w.WriteString("backgroundColor", Hex(palette[0]));
                w.WriteString("primaryColor", Hex(palette[1]));
                w.WriteString("secondaryColor", Hex(palette[2]));
                w.WriteString("accentColor", Hex(palette[3]));
                w.WriteEndObject();
            }
            w.WriteBoolean("temperaDecor", Model.AppSettings.FoliaTemperaDecor);
            w.WriteString("seed", atlas.Seed);
            w.WritePropertyName("widths");
            if (body == null)
            {
                WriteInputWidths(w, atlas);
                w.WritePropertyName("lines");
                WriteInputLines(w, atlas, token);
            }
            else
            {
                w.WriteRawValue(body.Widths.Span, skipInputValidation: true);
                w.WritePropertyName("lines");
                w.WriteRawValue(body.Lines.Span, skipInputValidation: true);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(input.WrittenSpan);
    }

    // The glyph metrics and line data are immutable for the life of this atlas.
    // Keep short, reusable jobs with that atlas for rapid switches between planners.
    private const int MaxRetainedInputBodyBytes = 1024 * 1024;
    private sealed record InputBody(ReadOnlyMemory<byte> Widths, ReadOnlyMemory<byte> Lines);
    private static readonly InputBody OversizedInput = new(default, default);
    private static readonly ConditionalWeakTable<FoliaTextAtlas, InputBody> InputBodies = new();

    private static InputBody? GetInputBody(FoliaTextAtlas atlas, CancellationToken token)
    {
        if (InputBodies.TryGetValue(atlas, out var existing))
            return ReferenceEquals(existing, OversizedInput) ? null : existing;
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        WriteInputWidths(writer, atlas);
        writer.Flush();
        int lineOffset = buffer.WrittenCount;
        writer.Reset(buffer);
        WriteInputLines(writer, atlas, token);
        writer.Flush();
        bool retain = buffer.WrittenCount <= MaxRetainedInputBodyBytes;
        ReadOnlyMemory<byte> bytes = retain && buffer.Capacity > MaxRetainedInputBodyBytes
            ? buffer.WrittenSpan.ToArray() : buffer.WrittenMemory;
        var body = new InputBody(bytes[..lineOffset], bytes[lineOffset..]);
        if (retain) return InputBodies.GetValue(atlas, _ => body);
        InputBodies.GetValue(atlas, static _ => OversizedInput);
        return body;
    }

    private static void WriteInputWidths(Utf8JsonWriter w, FoliaTextAtlas atlas)
    {
        w.WriteStartObject();
        foreach (var pair in atlas.CharacterGlyphs)
            w.WriteNumber(pair.Key, pair.Value.Advance);
        w.WriteEndObject();
    }

    private static void WriteInputLines(Utf8JsonWriter w, FoliaTextAtlas atlas, CancellationToken token)
    {
        w.WriteStartArray();
        for (int li = 0; li < atlas.Lines.Length; li++)
        {
            token.ThrowIfCancellationRequested();
            var line = atlas.Lines[li];
            w.WriteStartObject();
            w.WriteString("fullText", line.Source.Text);
            bool hasKeywords = false;
            foreach (ref readonly var glyph in line.Glyphs.AsSpan()) hasKeywords |= glyph.ActiveColor.HasValue;
            if (hasKeywords)
            {
                w.WriteStartArray("keywordColors");
                foreach (ref readonly var glyph in line.Glyphs.AsSpan())
                    if (glyph.ActiveColor is { } color) w.WriteStringValue(Hex(color));
                    else w.WriteNullValue();
                w.WriteEndArray();
            }
            w.WriteNumber("startTime", line.Source.Start);
            w.WriteNumber("endTime", line.Source.End);
            w.WriteBoolean("isChorus", line.Source.IsChorus);
            w.WriteStartArray("words");
            foreach (var word in line.Source.Words)
            {
                w.WriteStartObject();
                w.WriteString("text", word.Text);
                w.WriteNumber("startTime", word.Start);
                w.WriteNumber("endTime", word.End);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("_segments");
            int[] offsets = StringInfo.ParseCombiningCharacters(line.Source.Text);
            int[] boundaries = FoliaWordSegmentation.Boundaries(line.Source.Text);
            int first = -1;
            foreach (int next in boundaries)
            {
                if (first >= 0 && next > first && line.Glyphs.Length > 0)
                {
                    var text = line.Source.Text.AsSpan(first, next - first);
                    w.WriteStartObject();
                    w.WriteString("text", text);
                    w.WriteNumber("startOffset", first);
                    w.WriteNumber("endOffset", next);
                    w.WriteBoolean("isWordLike", !text.IsWhiteSpace() && !char.IsPunctuation(text[0]));
                    int a = Array.BinarySearch(offsets, first);
                    if (a < 0)
                        a = Math.Max(0, ~a - 1);
                    int b = Array.BinarySearch(offsets, next);
                    if (b < 0)
                        b = ~b;
                    a = Math.Min(a, line.Glyphs.Length - 1);
                    b = Math.Min(b, line.Glyphs.Length);
                    w.WriteNumber("startTime", line.Glyphs[a].Start);
                    w.WriteNumber("endTime", line.Glyphs[Math.Max(a, b - 1)].End);
                    w.WriteStartArray("wordIndices");
                    w.WriteEndArray();
                    w.WriteStartArray("graphemes");
                    for (int gi = a; gi < b; gi++)
                    {
                        int end = gi + 1 < offsets.Length ? offsets[gi + 1] : line.Source.Text.Length;
                        w.WriteStartObject();
                        w.WriteString("char", line.Source.Text.AsSpan(offsets[gi], end - offsets[gi]));
                        w.WriteNumber("startTime", line.Glyphs[gi].Start);
                        w.WriteNumber("endTime", line.Glyphs[gi].End);
                        w.WriteNumber("line", li);
                        w.WriteNumber("index", gi);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                first = next;
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
    internal static string Compile(string json, string function, CancellationToken token = default)
    {
        // Only locally embedded source executes; lyric content is passed as data, with no CLR interop.
        token.ThrowIfCancellationRequested();
        var key = (Function: function, Input: json);
        FoliaCompiledResult? cached;
        lock (CacheGate) Results.TryGetValue(key, out cached);
        if (cached != null) return cached.Read(token);
        string result = CompileUncached(json, function, token);
        token.ThrowIfCancellationRequested();
        // Cache only immutable output, never an engine or its mutable song globals. Repeated
        // mode/effect switches avoid the interpreter's short-lived number/object graph.
        // Bound decoded cache entries as well as retained bytes. Small results keep
        // their original string; reading a large hit never reconstructs a JS engine.
        if (result.Length <= 8 * 1024 * 1024)
        {
            var entry = new FoliaCompiledResult(result, token);
            long bytes = ((long)function.Length + json.Length) * sizeof(char) + entry.Bytes;
            if (bytes <= ResultLimit)
            lock (CacheGate)
            {
                if (!Results.ContainsKey(key))
                {
                    while ((_resultBytes + bytes > ResultLimit || Results.Count >= EntryLimit)
                        && ResultOrder.TryDequeue(out var oldest))
                        if (Results.Remove(oldest, out var value))
                            _resultBytes -= (oldest.Function.Length + oldest.Input.Length) * sizeof(char) + value.Bytes;
                    Results.Add(key, entry);
                    ResultOrder.Enqueue(key);
                    _resultBytes += (int)bytes;
                }
            }
        }
        return result;
    }

    private static string CompileUncached(string json, string function, CancellationToken token)
    {
        // Keep the captured options delegate out of the cache-hit path.
        var engine = new Engine(options => options.CancellationToken(token));
        if (function == "compileCadenza") FoliaCadenzaSearch.Install(engine, token);
        if (function == "compileSonnet") FoliaSonnetCameraSamples.Install(engine, token);
        if (function == "compileDiorama") FoliaDioramaNumericArrays.Install(engine, token);
        if (function == "compileFume") FoliaFumeFontSearch.Install(engine, token);
        if (function == "compileTempera") FoliaTemperaPrimitives.Install(engine, token);
        if (function is "hanabi" or "metaphor" or "nebbia")
        {
            engine.Execute(NativeStagesSource.Value);
            engine.SetValue("nativeInput", json);
            return engine.Evaluate("FoliaNativeStages." + function + "(nativeInput)").AsString();
        }
        engine.Execute(Source.Value);
        if (function == "compileSonnet")
            engine.Execute(SonnetSource.Value);
        engine.SetValue("nativeInput", json);
        return engine.Evaluate((function == "compileSonnet" ? "FoliaSonnetGraphics.compile" : "FoliaNativeSource." + function) + "(nativeInput)").AsString();
    }
    private static string LoadSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Spectrum.Folia.native-layout.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
