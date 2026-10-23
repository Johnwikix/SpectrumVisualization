using System;
using System.Threading;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Reflection;
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
    // Meshes have a separate bounded numeric cache. Compress larger JSON layouts so
    // a song's mode tour does not retain every expanded UTF-16 plan simultaneously.
    private const int ResultLimit = 4 * 1024 * 1024, EntryLimit = 16;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<(string Function, string Input), FoliaCompiledResult> Results = new();
    private static readonly Queue<(string Function, string Input)> ResultOrder = new();
    private static int _resultBytes;

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
    internal static string CreateInput(FoliaTextAtlas atlas, float width, float height, float fontScale, string family, int weight, Vector3[]? palette = null)
    {
        using var input = new MemoryStream();
        using (var w = new Utf8JsonWriter(input))
        {
            w.WriteStartObject();
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
            w.WriteStartObject("widths");
            foreach (var pair in atlas.CharacterGlyphs)
                w.WriteNumber(pair.Key, pair.Value.Advance);
            w.WriteEndObject();
            w.WriteStartArray("lines");
            for (int li = 0; li < atlas.Lines.Length; li++)
            {
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
                        string text = line.Source.Text[first..next];
                        w.WriteStartObject();
                        w.WriteString("text", text);
                        w.WriteNumber("startOffset", first);
                        w.WriteNumber("endOffset", next);
                        w.WriteBoolean("isWordLike", !string.IsNullOrWhiteSpace(text) && !char.IsPunctuation(text[0]));
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
                            w.WriteString("char", line.Source.Text[offsets[gi]..end]);
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
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(input.GetBuffer(), 0, (int)input.Length);
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
