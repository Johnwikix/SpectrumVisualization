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
        set { if (value != null && (value != "machi" || MachiAvailable)) AppSettings.FoliaLyricMode = value; }
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
    public string FoliaMachiScene
    {
        get => AppSettings.FoliaMachiScene;
        set { if (value != null) AppSettings.FoliaMachiScene = value; }
    }

    public string FoliaMachiTimeOfDay
    {
        get => AppSettings.FoliaMachiTimeOfDay;
        set { if (value != null) AppSettings.FoliaMachiTimeOfDay = value; }
    }

    public string FoliaMachiWeather
    {
        get => AppSettings.FoliaMachiWeather;
        set { if (value != null) AppSettings.FoliaMachiWeather = value; }
    }

    public int FoliaMachiDenoiserIndex
    {
        get => AppSettings.FoliaMachiDenoiser switch { "dlss-rr" => 1, "none" => 2, _ => 0 };
        set
        {
            if (_isInitialized && value is >= 0 and <= 2)
                AppSettings.FoliaMachiDenoiser = value switch { 1 => "dlss-rr", 2 => "none", _ => "nrd" };
        }
    }

    public bool FoliaMachiChromaticAberration
    {
        get => AppSettings.FoliaMachiChromaticAberration;
        set => AppSettings.FoliaMachiChromaticAberration = value;
    }

    public double FoliaMachiSamplesPerPixel
    {
        get => AppSettings.FoliaMachiSamplesPerPixel;
        set { if (_isInitialized && double.IsFinite(value)) AppSettings.FoliaMachiSamplesPerPixel = (int)Math.Round(value); }
    }

    public double FoliaMachiMaxBounces
    {
        get => AppSettings.FoliaMachiMaxBounces;
        set { if (_isInitialized && double.IsFinite(value)) AppSettings.FoliaMachiMaxBounces = (int)Math.Round(value); }
    }

    public bool FoliaMachiDepthOfField
    {
        get => AppSettings.FoliaMachiDepthOfField;
        set => AppSettings.FoliaMachiDepthOfField = value;
    }

    public bool FoliaMachiBloom
    {
        get => AppSettings.FoliaMachiBloom;
        set => AppSettings.FoliaMachiBloom = value;
    }

    public double FoliaMachiNeonStrength
    {
        get => AppSettings.FoliaMachiNeonStrength;
        set => AppSettings.FoliaMachiNeonStrength = (float)value;
    }

    public bool FoliaMachiGpuQualityOverride
    {
        get => AppSettings.FoliaMachiGpuQualityOverride;
        set { if (_isInitialized) AppSettings.FoliaMachiGpuQualityOverride = value; }
    }

    public int FoliaMachiAntiAliasingIndex
    {
        get => GetGpuAntiAliasingIndex(AppSettings.EffectiveFoliaMachiGpuQuality.AntiAliasing);
        set
        {
            if (_isInitialized && FoliaMachiGpuQualityOverride && value is >= 0 and <= 5)
                AppSettings.FoliaMachiGpuQuality = AppSettings.FoliaMachiGpuQuality with { AntiAliasing = GetGpuAntiAliasingMode(value) };
        }
    }

    public double FoliaMachiRenderScale
    {
        get => AppSettings.EffectiveFoliaMachiGpuQuality.RenderScalePercent;
        set
        {
            if (_isInitialized && FoliaMachiGpuQualityOverride && double.IsFinite(value))
                AppSettings.FoliaMachiGpuQuality = AppSettings.FoliaMachiGpuQuality with { RenderScalePercent = (int)Math.Clamp(Math.Round(value), 1, 100) };
        }
    }

    public string FoliaMachiRenderScaleLabel => $"{FoliaMachiRenderScale}%";

    private void NotifyFoliaMachiQuality()
    {
        bool initialized = _isInitialized;
        _isInitialized = false;
        OnPropertyChanged(nameof(FoliaMachiGpuQualityOverride));
        OnPropertyChanged(nameof(FoliaMachiAntiAliasingIndex));
        OnPropertyChanged(nameof(FoliaMachiRenderScale));
        OnPropertyChanged(nameof(FoliaMachiRenderScaleLabel));
        OnPropertyChanged(nameof(GpuDlssSelected));
        _isInitialized = initialized;
    }

    public bool FoliaMachiMotion
    {
        get => AppSettings.FoliaMachiMotion;
        set => AppSettings.FoliaMachiMotion = value;
    }

    public double FoliaMachiFocalLengthMm
    {
        get => AppSettings.FoliaMachiFocalLengthMm;
        set { if (_isInitialized && double.IsFinite(value)) AppSettings.FoliaMachiFocalLengthMm = (int)Math.Round(value); }
    }

    public bool MachiAvailable => !Rendering.ReconstructionAvailability.Known || Rendering.ReconstructionAvailability.Status.HardwareRaytracing;

    public string MachiRendererStatus
    {
        get
        {
            var status = Rendering.ReconstructionAvailability.Status;
            string? upscaler = status.Active switch
            {
                Rendering.ReconstructionMode.Dlss => "DLSS Super Resolution",
                Rendering.ReconstructionMode.Fsr => "FSR",
                Rendering.ReconstructionMode.XeSS => "XeSS",
                _ => null
            };
            string key = !Rendering.ReconstructionAvailability.Known ? "MachiCapabilityPending" :
                !status.HardwareRaytracing ? "MachiRequiresDxr" : status.Machi switch
                {
                    Rendering.MachiRenderStatus.Nrd when status.UnsupportedScale => "GpuReconstructionScaleUnsupported",
                    Rendering.MachiRenderStatus.Nrd when status.Requested != status.Active => "GpuReconstructionFallback",
                    Rendering.MachiRenderStatus.Nrd => "MachiNrdActive",
                    Rendering.MachiRenderStatus.NrdSuperResolution => "MachiNrdSuperResolutionActive",
                    Rendering.MachiRenderStatus.Raw when status.UnsupportedScale => "MachiRawScaleUnsupported",
                    Rendering.MachiRenderStatus.Raw when status.Requested != status.Active => "MachiRawFallback",
                    Rendering.MachiRenderStatus.Raw => "MachiRawActive",
                    Rendering.MachiRenderStatus.RawSuperResolution => "MachiRawSuperResolutionActive",
                    Rendering.MachiRenderStatus.RayReconstruction => "MachiRrActive",
                    Rendering.MachiRenderStatus.NrdFallback when upscaler != null => "MachiRrFallbackSuperResolution",
                    Rendering.MachiRenderStatus.NrdFallback => "MachiRrFallback",
                    Rendering.MachiRenderStatus.Unavailable => "MachiStageFailed",
                    _ => "MachiDxrAvailable"
                };
            string message = new ResourceLoader().GetString(key);
            return upscaler == null ? message : string.Format(CultureInfo.CurrentCulture, message, upscaler);
        }
    }

    public bool FoliaMetaphorNegative
    {
        get => AppSettings.FoliaMetaphorNegative;
        set => AppSettings.FoliaMetaphorNegative = value;
    }

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
        NotifyFoliaThemeSettings();
        NotifyFoliaTuningSettings();
        OnPropertyChanged(nameof(FoliaSonnetBackgroundMg));
        OnPropertyChanged(nameof(FoliaSonnetFixedGeometry));
        OnPropertyChanged(nameof(FoliaSonnetDecorations));
        OnPropertyChanged(nameof(FoliaSonnetGuides));
        OnPropertyChanged(nameof(FoliaSonnetFrame));
        OnPropertyChanged(nameof(FoliaSonnetTransitions));
        OnPropertyChanged(nameof(FoliaSonnetPostProcess));
        OnPropertyChanged(nameof(FoliaMachiTimeOfDay));
        OnPropertyChanged(nameof(FoliaMachiWeather));
        OnPropertyChanged(nameof(FoliaMachiScene));
        OnPropertyChanged(nameof(FoliaMachiMotion));
        OnPropertyChanged(nameof(FoliaMachiFocalLengthMm));
        OnPropertyChanged(nameof(FoliaMachiDenoiserIndex));
        OnPropertyChanged(nameof(FoliaMachiSamplesPerPixel));
        OnPropertyChanged(nameof(FoliaMachiMaxBounces));
        OnPropertyChanged(nameof(FoliaMachiChromaticAberration));
        OnPropertyChanged(nameof(FoliaMachiDepthOfField));
        OnPropertyChanged(nameof(FoliaMachiBloom));
        OnPropertyChanged(nameof(FoliaMachiNeonStrength));
        OnPropertyChanged(nameof(MachiAvailable));
        OnPropertyChanged(nameof(MachiRendererStatus));
        OnPropertyChanged(nameof(FoliaMetaphorNegative));
        OnPropertyChanged(nameof(FoliaNebbiaBehind));
        OnPropertyChanged(nameof(FoliaNebbiaGlass));
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
