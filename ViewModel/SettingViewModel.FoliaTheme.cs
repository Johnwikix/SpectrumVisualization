using System;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.UI;
using WinExSpectrumTest.Effects.Folia;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
    private string? _foliaWordColorsDraft;
    private bool _foliaWordColorsDirty;
    private string _foliaKeywordError = "";
    public string FoliaThemeSource
    {
        get => AppSettings.FoliaTheme.Source;
        set => AppSettings.FoliaTheme = AppSettings.FoliaTheme with { Source = value };
    }
    public string FoliaThemeVariant
    {
        get => AppSettings.FoliaTheme.Variant;
        set => AppSettings.FoliaTheme = AppSettings.FoliaTheme with { Variant = value };
    }
    public bool IsFoliaCustomTheme => FoliaThemeSource == "custom";
    public bool IsFoliaCoverTheme => FoliaThemeSource == "cover";
    public bool FoliaKeywordColorsEnabled
    {
        get => AppSettings.FoliaTheme.KeywordColorsEnabled;
        set => AppSettings.FoliaTheme = AppSettings.FoliaTheme with { KeywordColorsEnabled = value };
    }
    private FoliaThemeColors EditableFoliaColors => FoliaThemeVariant == "light" ? AppSettings.FoliaTheme.CustomLight : AppSettings.FoliaTheme.CustomDark;
    private void SetFoliaColors(FoliaThemeColors colors)
    {
        var theme = AppSettings.FoliaTheme;
        AppSettings.FoliaTheme = theme.Variant == "light" ? theme with { CustomLight = colors } : theme with { CustomDark = colors };
    }
    public Color FoliaThemeBackground { get => ToColor(EditableFoliaColors.Background); set => SetFoliaColors(EditableFoliaColors with { Background = ToHex(value) }); }
    public Color FoliaThemePrimary { get => ToColor(EditableFoliaColors.Primary); set => SetFoliaColors(EditableFoliaColors with { Primary = ToHex(value) }); }
    public Color FoliaThemeSecondary { get => ToColor(EditableFoliaColors.Secondary); set => SetFoliaColors(EditableFoliaColors with { Secondary = ToHex(value) }); }
    public Color FoliaThemeAccent { get => ToColor(EditableFoliaColors.Accent); set => SetFoliaColors(EditableFoliaColors with { Accent = ToHex(value) }); }
    public string FoliaThemeBackgroundHex => EditableFoliaColors.Background;
    public string FoliaThemePrimaryHex => EditableFoliaColors.Primary;
    public string FoliaThemeSecondaryHex => EditableFoliaColors.Secondary;
    public string FoliaThemeAccentHex => EditableFoliaColors.Accent;
    private static Color ToColor(string hex)
    {
        int rgb = Convert.ToInt32(hex.AsSpan(1).ToString(), 16);
        return Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    private static string ToHex(Color value) => $"#{value.R:x2}{value.G:x2}{value.B:x2}";
    public string FoliaWordColorsDraft
    {
        get => _foliaWordColorsDraft ?? AppSettings.FoliaTheme.WordColors;
        set
        {
            if (SetProperty(ref _foliaWordColorsDraft, value)) _foliaWordColorsDirty = true;
        }
    }
    public string FoliaKeywordError => _foliaKeywordError;
    public bool HasFoliaKeywordError => _foliaKeywordError.Length != 0;

    [RelayCommand]
    private void ApplyFoliaWordColors()
    {
        if (!FoliaThemePlanner.TryParseRules(FoliaWordColorsDraft, out _, out int errorLine))
            _foliaKeywordError = string.Format(new ResourceLoader().GetString("FoliaKeywordInvalid"), errorLine);
        else
        {
            _foliaKeywordError = "";
            _foliaWordColorsDirty = false;
            AppSettings.FoliaTheme = AppSettings.FoliaTheme with { WordColors = FoliaWordColorsDraft };
        }
        OnPropertyChanged(nameof(FoliaKeywordError));
        OnPropertyChanged(nameof(HasFoliaKeywordError));
    }

    [RelayCommand]
    private void RegenerateFoliaTheme() => AppSettings.FoliaTheme = AppSettings.FoliaTheme with { Seed = unchecked(AppSettings.FoliaTheme.Seed + 1) };

    private void NotifyFoliaThemeSettings()
    {
        if (!_foliaWordColorsDirty) _foliaWordColorsDraft = AppSettings.FoliaTheme.WordColors;
        OnPropertyChanged(nameof(FoliaThemeSource));
        OnPropertyChanged(nameof(FoliaThemeVariant));
        OnPropertyChanged(nameof(IsFoliaCustomTheme));
        OnPropertyChanged(nameof(IsFoliaCoverTheme));
        OnPropertyChanged(nameof(FoliaThemeBackground));
        OnPropertyChanged(nameof(FoliaThemePrimary));
        OnPropertyChanged(nameof(FoliaThemeSecondary));
        OnPropertyChanged(nameof(FoliaThemeAccent));
        OnPropertyChanged(nameof(FoliaThemeBackgroundHex));
        OnPropertyChanged(nameof(FoliaThemePrimaryHex));
        OnPropertyChanged(nameof(FoliaThemeSecondaryHex));
        OnPropertyChanged(nameof(FoliaThemeAccentHex));
        OnPropertyChanged(nameof(FoliaKeywordColorsEnabled));
        OnPropertyChanged(nameof(FoliaWordColorsDraft));
    }
}
