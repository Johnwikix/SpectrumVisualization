using System;
using System.Numerics;
using System.Threading;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Single-song CPU cache. Late lyrics and light/dark changes reuse independent theme work.</summary>
internal sealed class FoliaThemeResolver
{
    private FoliaSong? _input, _song;
    private FoliaThemeSettings? _settings;
    private Vector3[][]? _coverThemes;

    internal FoliaSong Resolve(FoliaSong input, FoliaThemeSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ReferenceEquals(input, _input) && settings == _settings) return _song!;
        Vector3[] palette;
        Vector3[][]? coverThemes = null;
        if (settings.Source == "cover")
        {
            bool sameCover = _settings?.Source == "cover" && _settings.Seed == settings.Seed
                && _input!.Title == input.Title && _input.Artist == input.Artist
                && ReferenceEquals(_input.CoverRgba, input.CoverRgba)
                && _input.CoverWidth == input.CoverWidth && _input.CoverHeight == input.CoverHeight;
            coverThemes = sameCover ? _coverThemes! : FoliaThemePlanner.Generate(input, settings, token);
            palette = coverThemes[settings.Variant == "light" ? 1 : 0];
        }
        else
        {
            var colors = Colors(settings);
            palette = _song != null && _settings?.Source != "cover" && Colors(_settings!) == colors ? _song.Palette
                : [FoliaThemePlanner.Rgb(colors.Background), FoliaThemePlanner.Rgb(colors.Primary),
                    FoliaThemePlanner.Rgb(colors.Secondary), FoliaThemePlanner.Rgb(colors.Accent)];
        }
        string signature = settings.KeywordColorsEnabled ? settings.WordColors : "";
        FoliaKeywordRange[][] ranges = [];
        if (_song != null && ReferenceEquals(_input!.Lines, input.Lines) && _song.KeywordSignature == signature)
            ranges = _song.KeywordRanges;
        else if (signature.Length > 0 && FoliaThemePlanner.TryParseRules(signature, out var rules, out _))
            ranges = FoliaThemePlanner.Match(input, rules, token);
        byte[]? display = _song != null && ReferenceEquals(_input!.CoverRgba, input.CoverRgba) && _song.Palette[0] == palette[0]
            ? _song.DisplayCoverRgba : FoliaCoverPalette.Composite(input.CoverRgba, palette[0], token);
        token.ThrowIfCancellationRequested();
        _song = input with { Palette = palette, DisplayCoverRgba = display, KeywordSignature = signature,
            KeywordRanges = ranges, IsLightTheme = settings.Variant == "light" };
        _input = input;
        _settings = settings;
        _coverThemes = coverThemes;
        return _song;
    }

    private static FoliaThemeColors Colors(FoliaThemeSettings settings) => settings.Source == "custom"
        ? settings.Variant == "light" ? settings.CustomLight : settings.CustomDark
        : settings.Variant == "light" ? FoliaThemeColors.Light : FoliaThemeColors.Dark;

    internal void Clear()
    {
        _input = _song = null;
        _settings = null;
        _coverThemes = null;
    }
}
