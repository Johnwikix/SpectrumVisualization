using System;
using System.Globalization;

namespace WinExSpectrumTest.Model;

/// <summary>Immutable theme options shared by settings and cold scene preparation.</summary>
public sealed record FoliaThemeColors(string Background, string Primary, string Secondary, string Accent)
{
    public static FoliaThemeColors Dark { get; } = new("#09090b", "#f4f4f5", "#71717a", "#f4f4f5");
    public static FoliaThemeColors Light { get; } = new("#f5f5f4", "#1c1917", "#44403c", "#ea580c");

    public static bool TryNormalize(string? value, out string hex)
    {
        hex = "";
        if (value is null) return false;
        var text = value.AsSpan().Trim();
        if (text.Length != 7 || text[0] != '#' || !int.TryParse(text[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)) return false;
        hex = value.Trim().ToLowerInvariant();
        return true;
    }

    internal FoliaThemeColors Normalize(FoliaThemeColors fallback) => new(
        TryNormalize(Background, out var a) ? a : fallback.Background,
        TryNormalize(Primary, out var b) ? b : fallback.Primary,
        TryNormalize(Secondary, out var c) ? c : fallback.Secondary,
        TryNormalize(Accent, out var d) ? d : fallback.Accent);
}

public sealed record FoliaThemeSettings
{
    public static FoliaThemeSettings Default { get; } = new();
    public string Source { get; init; } = "cover";
    public string Variant { get; init; } = "dark";
    public int Seed { get; init; }
    public FoliaThemeColors CustomDark { get; init; } = FoliaThemeColors.Dark;
    public FoliaThemeColors CustomLight { get; init; } = FoliaThemeColors.Light;
    public bool KeywordColorsEnabled { get; init; } = true;
    public string WordColors { get; init; } = "";

    public FoliaThemeSettings Normalize() => this with
    {
        Source = Source is "preset" or "custom" ? Source : "cover",
        Variant = Variant == "light" ? "light" : "dark",
        CustomDark = (CustomDark ?? FoliaThemeColors.Dark).Normalize(FoliaThemeColors.Dark),
        CustomLight = (CustomLight ?? FoliaThemeColors.Light).Normalize(FoliaThemeColors.Light),
        WordColors = WordColors is { Length: <= 16384 } ? WordColors.Replace("\r\n", "\n").Trim() : ""
    };
}
