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
    private FoliaLine[]? _emphasisLines;
    private FoliaEmphasisSpan[][] _emphasis = [];

    internal FoliaSong Resolve(FoliaSong input, FoliaThemeSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (ReferenceEquals(input, _input) && settings == _settings) return _song!;
        Vector3[] palette;
        Vector3[][]? coverThemes = null;
        if (settings.Source != "preset")
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
        FoliaKeywordRange[][] ranges = [];
        if (settings.KeywordColorsEnabled)
        {
            if (!ReferenceEquals(_emphasisLines, input.Lines))
            {
                _emphasis = FoliaSyllableEmphasis.Analyze(input.Lines, token);
                _emphasisLines = input.Lines;
            }
            ranges = _song != null && ReferenceEquals(_input!.Lines, input.Lines) && ReferenceEquals(_song.Palette, palette)
                && _settings!.KeywordColorsEnabled && _settings.Seed == settings.Seed && _input.Title == input.Title && _input.Artist == input.Artist
                ? _song.KeywordRanges : FoliaSyllableEmphasis.Colorize(_emphasis, palette, FoliaThemePlanner.Seed(input, settings), settings.Variant == "light");
        }
        else
        {
            _emphasisLines = null;
            _emphasis = [];
        }
        byte[]? display = _song != null && ReferenceEquals(_input!.CoverRgba, input.CoverRgba) && _song.Palette[0] == palette[0]
            ? _song.DisplayCoverRgba : FoliaCoverPalette.Composite(input.CoverRgba, palette[0], token);
        token.ThrowIfCancellationRequested();
        _song = input with { Palette = palette, DisplayCoverRgba = display,
            KeywordRanges = ranges, IsLightTheme = settings.Variant == "light" };
        _input = input;
        _settings = settings;
        _coverThemes = coverThemes;
        return _song;
    }

    private static FoliaThemeColors Colors(FoliaThemeSettings settings) => settings.Variant == "light" ? FoliaThemeColors.Light : FoliaThemeColors.Dark;

    internal void Clear()
    {
        _input = _song = null;
        _settings = null;
        _coverThemes = null;
        _emphasisLines = null;
        _emphasis = [];
    }
}
