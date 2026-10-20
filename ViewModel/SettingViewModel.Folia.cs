using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Graphics.Canvas.Text;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
    private ObservableCollection<FontChoice>? _foliaFonts;
    public ObservableCollection<FontChoice> FoliaFonts
    {
        get
        {
            if (_foliaFonts is not null) return _foliaFonts;
            _foliaFonts = [];
            try
            {
                var names = CanvasTextFormat.GetSystemFontFamilies();
                // Pass a real array: the compiler's IReadOnlyList collection-literal helper has no NativeAOT CCW.
                string[] cultures = [CultureInfo.CurrentUICulture.Name];
                var displayNames = CanvasTextFormat.GetSystemFontFamilies(cultures);
                var choices = new FontChoice[names.Length];
                for (int i = 0; i < names.Length; i++) choices[i] = new(names[i], i < displayNames.Length ? displayNames[i] : names[i]);
                Array.Sort(choices, static (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
                foreach (var choice in choices) _foliaFonts.Add(choice);
            }
            catch (Exception ex) { App.WriteCrashLog("System lyric fonts", ex.Message, ex); }
            return _foliaFonts;
        }
    }

    public FontChoice SelectedFoliaFont
    {
        get
        {
            foreach (var font in FoliaFonts)
                if (string.Equals(font.Name, AppSettings.FoliaFontFamily, StringComparison.OrdinalIgnoreCase)) return font;
            // Keep an unavailable saved face; opening settings must not silently replace it.
            var saved = new FontChoice(AppSettings.FoliaFontFamily, AppSettings.FoliaFontFamily);
            FoliaFonts.Add(saved);
            return saved;
        }
        set { if (value is not null) AppSettings.FoliaFontFamily = value.Name; }
    }
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
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaSonnetBackgroundMg
    {
        get => AppSettings.FoliaSonnetBackgroundMg;
        set => AppSettings.FoliaSonnetBackgroundMg = value;
    }
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaSonnetFixedGeometry
    {
        get => AppSettings.FoliaSonnetFixedGeometry;
        set => AppSettings.FoliaSonnetFixedGeometry = value;
    }
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaSonnetDecorations
    {
        get => AppSettings.FoliaSonnetDecorations;
        set => AppSettings.FoliaSonnetDecorations = value;
    }
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaSonnetGuides
    {
        get => AppSettings.FoliaSonnetGuides;
        set => AppSettings.FoliaSonnetGuides = value;
    }
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaSonnetFrame
    {
        get => AppSettings.FoliaSonnetFrame;
        set => AppSettings.FoliaSonnetFrame = value;
    }
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaSonnetTransitions
    {
        get => AppSettings.FoliaSonnetTransitions;
        set => AppSettings.FoliaSonnetTransitions = value;
    }
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaSonnetPostProcess
    {
        get => AppSettings.FoliaSonnetPostProcess;
        set => AppSettings.FoliaSonnetPostProcess = value;
    }
    /// <summary>Gets or sets the corresponding native renderer switch.</summary>
    public bool FoliaRealtimeMotion
    {
        get => AppSettings.FoliaRealtimeMotion;
        set => AppSettings.FoliaRealtimeMotion = value;
    }
    /// <summary>Gets or sets the response strength shown by the settings control.</summary>
    public double FoliaSonnetMotionStrength
    {
        get => AppSettings.FoliaSonnetMotionStrength;
        set => AppSettings.FoliaSonnetMotionStrength = (float)value;
    }
    /// <summary>Gets or sets the response strength shown by the settings control.</summary>
    public double FoliaDioramaMotionStrength
    {
        get => AppSettings.FoliaDioramaMotionStrength;
        set => AppSettings.FoliaDioramaMotionStrength = (float)value;
    }
    /// <summary>Gets or sets the response strength shown by the settings control.</summary>
    public double FoliaPendoloMotionStrength
    {
        get => AppSettings.FoliaPendoloMotionStrength;
        set => AppSettings.FoliaPendoloMotionStrength = (float)value;
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
        NotifyFoliaTuningSettings();
        OnPropertyChanged(nameof(FoliaSonnetBackgroundMg));
        OnPropertyChanged(nameof(FoliaSonnetFixedGeometry));
        OnPropertyChanged(nameof(FoliaSonnetDecorations));
        OnPropertyChanged(nameof(FoliaSonnetGuides));
        OnPropertyChanged(nameof(FoliaSonnetFrame));
        OnPropertyChanged(nameof(FoliaSonnetTransitions));
        OnPropertyChanged(nameof(FoliaSonnetPostProcess));
        OnPropertyChanged(nameof(FoliaRealtimeMotion));
        OnPropertyChanged(nameof(FoliaSonnetMotionStrength));
        OnPropertyChanged(nameof(FoliaDioramaMotionStrength));
        OnPropertyChanged(nameof(FoliaPendoloMotionStrength));
        OnPropertyChanged(nameof(FoliaLyricMode));
        OnPropertyChanged(nameof(FoliaCoverBackground));
        OnPropertyChanged(nameof(FoliaBackground));
        OnPropertyChanged(nameof(FoliaImageEffect));
        OnPropertyChanged(nameof(IsFoliaImageBackground));
        OnPropertyChanged(nameof(FoliaFontScale));
        OnPropertyChanged(nameof(FoliaFontFamily));
        OnPropertyChanged(nameof(SelectedFoliaFont));
        OnPropertyChanged(nameof(FoliaFontWeight));
    }
}
