using CommunityToolkit.Mvvm.Input;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
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
    public bool IsFoliaCoverTheme => FoliaThemeSource == "cover";
    public bool FoliaKeywordColorsEnabled
    {
        get => AppSettings.FoliaTheme.KeywordColorsEnabled;
        set => AppSettings.FoliaTheme = AppSettings.FoliaTheme with { KeywordColorsEnabled = value };
    }

    [RelayCommand]
    private void RegenerateFoliaTheme() => AppSettings.FoliaTheme = AppSettings.FoliaTheme with { Seed = unchecked(AppSettings.FoliaTheme.Seed + 1) };

    private void NotifyFoliaThemeSettings()
    {
        OnPropertyChanged(nameof(FoliaThemeSource));
        OnPropertyChanged(nameof(FoliaThemeVariant));
        OnPropertyChanged(nameof(IsFoliaCoverTheme));
        OnPropertyChanged(nameof(FoliaKeywordColorsEnabled));
    }
}
