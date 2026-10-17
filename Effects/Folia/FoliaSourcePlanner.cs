using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Jint;
namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Cold compilation boundary for the embedded original Folia layout algorithms.
/// Only immutable native plans cross into the allocation-free D3D12 frame loop.</summary>
internal static class FoliaSourcePlanner
{
    private static string? _source;
    internal static string CreateInput(FoliaTextAtlas atlas, float width, float height, float fontScale, string family, int weight)
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
                w.WriteNumber("startTime", line.Source.Start);
                w.WriteNumber("endTime", line.Source.End);
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
    internal static string Compile(string json, string function)
    {
        _source ??= LoadSource();
        // Only locally embedded source executes; lyric content is passed as data, with no CLR interop.
        var engine = new Engine();
        engine.Execute(_source);
        engine.SetValue("nativeInput", json);
        string result = engine.Evaluate("FoliaNativeSource." + function + "(nativeInput)").AsString();
        return result;
    }
    private static string LoadSource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Spectrum.Folia.native-layout.js")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
