using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using Jint;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaWordColor(string Word, string Hex);
internal readonly record struct FoliaKeywordRange(int Start, int End, Vector3 Color);

/// <summary>Original non-AI theme and range matching, executed only on the cancellable CPU worker.</summary>
internal static class FoliaThemePlanner
{
    private static readonly Lazy<Prepared<Acornima.Ast.Script>> Source = new(() =>
    {
        using var stream = typeof(FoliaThemePlanner).Assembly.GetManifestResourceStream("Spectrum.Folia.themes.js")!;
        using var reader = new StreamReader(stream);
        return Engine.PrepareScript(reader.ReadToEnd());
    });

    internal static string Execute(string method, string input, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var engine = new Engine(options => options.CancellationToken(token));
        engine.Execute(Source.Value);
        engine.SetValue("nativeInput", input);
        string output = engine.Evaluate("FoliaThemes." + method + "(nativeInput)").AsString();
        token.ThrowIfCancellationRequested();
        return output;
    }

    internal static bool TryParseRules(string text, out FoliaWordColor[] rules, out int errorLine)
    {
        var result = new List<FoliaWordColor>();
        int row = 0;
        foreach (var raw in text.AsSpan().EnumerateLines())
        {
            row++;
            var line = raw.Trim();
            if (line.IsEmpty) continue;
            int split = line.LastIndexOf('=');
            if (split <= 0 || line[..split].Trim().IsEmpty || !FoliaThemeColors.TryNormalize(line[(split + 1)..].ToString(), out string hex) || result.Count >= 128)
            {
                rules = [];
                errorLine = row;
                return false;
            }
            result.Add(new(line[..split].Trim().ToString(), hex));
        }
        rules = result.ToArray();
        errorLine = 0;
        return true;
    }

    internal static FoliaSong Resolve(FoliaSong song, FoliaThemeSettings settings, CancellationToken token)
        => new FoliaThemeResolver().Resolve(song, settings, token);

    internal static Vector3[][] Generate(FoliaSong song, FoliaThemeSettings settings, CancellationToken token)
    {
        byte[] pixels = song.CoverWidth > 0 ? FoliaCoverSampling.PalettePixels(song.CoverRgba, song.CoverWidth, song.CoverHeight) : [];
        using var input = new MemoryStream();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartObject();
            uint seed = unchecked((uint)settings.Seed) ^ 2166136261;
            foreach (char c in song.Title) seed = unchecked((seed ^ c) * 16777619);
            foreach (char c in song.Artist) seed = unchecked((seed ^ c) * 16777619);
            writer.WriteNumber("seed", seed);
            writer.WriteStartArray("pixels");
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i + 3];
                for (int c = 0; c < 3; c++) writer.WriteNumberValue(alpha == 0 ? 0 : Math.Min(255, (pixels[i + c] * 255 + alpha / 2) / alpha));
                writer.WriteNumberValue(alpha);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(Execute("generate", Encoding.UTF8.GetString(input.GetBuffer(), 0, (int)input.Length), token));
        return [Read("dark"), Read("light")];
        Vector3[] Read(string variant)
        {
            var theme = document.RootElement.GetProperty(variant);
            return [Rgb(theme.GetProperty("backgroundColor").GetString()!), Rgb(theme.GetProperty("primaryColor").GetString()!),
                Rgb(theme.GetProperty("secondaryColor").GetString()!), Rgb(theme.GetProperty("accentColor").GetString()!)];
        }
    }

    internal static FoliaKeywordRange[][] Match(FoliaSong song, FoliaWordColor[] rules, CancellationToken token)
    {
        using var input = new MemoryStream();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("texts");
            foreach (var line in song.Lines)
            {
                var text = new StringBuilder();
                foreach (var word in line.Words) text.Append(word.Text);
                writer.WriteStringValue(text.ToString());
            }
            writer.WriteEndArray();
            writer.WriteStartArray("colors");
            foreach (var rule in rules)
            {
                writer.WriteStartObject();
                writer.WriteString("word", rule.Word);
                writer.WriteString("color", rule.Hex);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(Execute("match", Encoding.UTF8.GetString(input.GetBuffer(), 0, (int)input.Length), token));
        var result = new FoliaKeywordRange[song.Lines.Length][];
        for (int i = 0; i < result.Length; i++)
        {
            var line = document.RootElement[i];
            result[i] = new FoliaKeywordRange[line.GetArrayLength()];
            int j = 0;
            foreach (var range in line.EnumerateArray()) result[i][j++] = new(range.GetProperty("startOffset").GetInt32(), range.GetProperty("endOffset").GetInt32(), Rgb(range.GetProperty("color").GetString()!));
        }
        return result;
    }

    internal static Vector3 Rgb(string hex)
    {
        int rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Vector3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255;
    }
}
