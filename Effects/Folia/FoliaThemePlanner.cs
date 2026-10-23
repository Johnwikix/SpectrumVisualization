using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Threading;
using Jint;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaKeywordRange(int Start, int End, Vector3 Color);

/// <summary>Original non-AI theme generation, executed only on the cancellable CPU worker.</summary>
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

    internal static FoliaSong Resolve(FoliaSong song, FoliaThemeSettings settings, CancellationToken token)
        => new FoliaThemeResolver().Resolve(song, settings, token);

    internal static Vector3[][] Generate(FoliaSong song, FoliaThemeSettings settings, CancellationToken token)
    {
        byte[] pixels = song.CoverWidth > 0 ? FoliaCoverSampling.PalettePixels(song.CoverRgba, song.CoverWidth, song.CoverHeight) : [];
        using var input = new MemoryStream();
        using (var writer = new Utf8JsonWriter(input))
        {
            writer.WriteStartObject();
            writer.WriteNumber("seed", Seed(song, settings));
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

    internal static Vector3 Rgb(string hex)
    {
        int rgb = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Vector3((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255) / 255;
    }

    internal static uint Seed(FoliaSong song, FoliaThemeSettings settings)
    {
        uint seed = unchecked((uint)settings.Seed) ^ 2166136261;
        foreach (char c in song.Title) seed = unchecked((seed ^ c) * 16777619);
        foreach (char c in song.Artist) seed = unchecked((seed ^ c) * 16777619);
        return seed;
    }
}
