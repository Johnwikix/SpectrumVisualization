using System;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
    public string FoliaLyricMode
    {
        get => AppSettings.FoliaLyricMode;
        set => AppSettings.FoliaLyricMode = value;
    }
    public bool FoliaCoverBackground
    {
        get => AppSettings.FoliaCoverBackground;
        set => AppSettings.FoliaCoverBackground = value;
    }
    public string FoliaBackground
    {
        get => AppSettings.FoliaBackground;
        set => AppSettings.FoliaBackground = value;
    }
    public string FoliaImageEffect
    {
        get => AppSettings.FoliaImageEffect;
        set => AppSettings.FoliaImageEffect = value;
    }
    public bool IsFoliaImageBackground => AppSettings.FoliaBackground == "nomand";
    public double FoliaFontScale
    {
        get => AppSettings.FoliaFontScale * 100;
        set { if (double.IsFinite(value)) AppSettings.FoliaFontScale = (float)value / 100; }
    }
    public string FoliaFontFamily
    {
        get => AppSettings.FoliaFontFamily;
        set => AppSettings.FoliaFontFamily = value;
    }
    public double FoliaFontWeight
    {
        get => AppSettings.FoliaFontWeight;
        set { if (double.IsFinite(value)) AppSettings.FoliaFontWeight = (int)Math.Clamp(value, 100, 900); }
    }
    public string FoliaLyricsStatus => new ResourceLoader().GetString("FoliaStatus" + App.FoliaPlayback.Status);

    private bool CanRetryFoliaLyrics => App.FoliaPlayback.IsActive && !Services.FoliaPlaybackService.PreviewEnabled && App.FoliaPlayback.Status != "loading";

    [RelayCommand(CanExecute = nameof(CanRetryFoliaLyrics))]
    private void RetryFoliaLyrics() => App.FoliaPlayback.Retry();

    private void OnFoliaStatusChanged()
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(OnFoliaStatusChanged);
            return;
        }
        OnPropertyChanged(nameof(FoliaLyricsStatus));
        RetryFoliaLyricsCommand.NotifyCanExecuteChanged();
    }

    private void NotifyFoliaSettings()
    {
        OnPropertyChanged(nameof(FoliaLyricMode));
        OnPropertyChanged(nameof(FoliaCoverBackground));
        OnPropertyChanged(nameof(FoliaBackground));
        OnPropertyChanged(nameof(FoliaImageEffect));
        OnPropertyChanged(nameof(IsFoliaImageBackground));
        OnPropertyChanged(nameof(FoliaFontScale));
        OnPropertyChanged(nameof(FoliaFontFamily));
        OnPropertyChanged(nameof(FoliaFontWeight));
    }
}
