using WinExSpectrumTest.Model;
namespace WinExSpectrumTest.ViewModel;
/// <summary>Exposes localized Folia tuning controls backed by the shared application settings.</summary>
public partial class SettingViewModel
{
    /// <summary>Gets or sets geometric background.</summary>
    public bool FoliaFumeGeometry { get => AppSettings.FoliaFumeGeometry; set => AppSettings.FoliaFumeGeometry = value; }
    /// <summary>Gets or sets geometry opacity.</summary>
    public double FoliaFumeGeometryOpacity { get => AppSettings.FoliaFumeGeometryOpacity; set => AppSettings.FoliaFumeGeometryOpacity = (float)value; }
    /// <summary>Gets or sets glow intensity.</summary>
    public double FoliaFumeGlow { get => AppSettings.FoliaFumeGlow; set => AppSettings.FoliaFumeGlow = (float)value; }
    /// <summary>Gets or sets soul drift.</summary>
    public bool FoliaDioramaSoul { get => AppSettings.FoliaDioramaSoul; set => AppSettings.FoliaDioramaSoul = value; }
    /// <summary>Gets or sets drift while singing.</summary>
    public bool FoliaDioramaSoulActive { get => AppSettings.FoliaDioramaSoulActive; set => AppSettings.FoliaDioramaSoulActive = value; }
    /// <summary>Gets or sets soul strength.</summary>
    public double FoliaDioramaSoulStrength { get => AppSettings.FoliaDioramaSoulStrength; set => AppSettings.FoliaDioramaSoulStrength = (float)value; }
    /// <summary>Gets or sets singing glow.</summary>
    public bool FoliaDioramaGlow { get => AppSettings.FoliaDioramaGlow; set => AppSettings.FoliaDioramaGlow = value; }
    /// <summary>Gets or sets glow strength.</summary>
    public double FoliaDioramaGlowStrength { get => AppSettings.FoliaDioramaGlowStrength; set => AppSettings.FoliaDioramaGlowStrength = (float)value; }
    /// <summary>Gets or sets progressive lyric color.</summary>
    public bool FoliaDioramaGradient { get => AppSettings.FoliaDioramaGradient; set => AppSettings.FoliaDioramaGradient = value; }
    /// <summary>Gets or sets particle size.</summary>
    public double FoliaDioramaParticleScale { get => AppSettings.FoliaDioramaParticleScale; set => AppSettings.FoliaDioramaParticleScale = (float)value; }
    /// <summary>Gets or sets particle glow.</summary>
    public double FoliaDioramaParticleGlow { get => AppSettings.FoliaDioramaParticleGlow; set => AppSettings.FoliaDioramaParticleGlow = (float)value; }
    /// <summary>Gets or sets scene transitions.</summary>
    public bool FoliaTemperaTransitions { get => AppSettings.FoliaTemperaTransitions; set => AppSettings.FoliaTemperaTransitions = value; }
    /// <summary>Gets or sets composition blocks.</summary>
    public bool FoliaTemperaBlocks { get => AppSettings.FoliaTemperaBlocks; set => AppSettings.FoliaTemperaBlocks = value; }
    /// <summary>Gets or sets decorations.</summary>
    public bool FoliaTemperaDecor { get => AppSettings.FoliaTemperaDecor; set => AppSettings.FoliaTemperaDecor = value; }
    /// <summary>Gets or sets text inversion.</summary>
    public bool FoliaTemperaInversion { get => AppSettings.FoliaTemperaInversion; set => AppSettings.FoliaTemperaInversion = value; }
    /// <summary>Gets or sets post processing.</summary>
    public bool FoliaTemperaPostProcess { get => AppSettings.FoliaTemperaPostProcess; set => AppSettings.FoliaTemperaPostProcess = value; }
    /// <summary>Gets or sets grain.</summary>
    public double FoliaTemperaGrain { get => AppSettings.FoliaTemperaGrain; set => AppSettings.FoliaTemperaGrain = (float)value; }
    /// <summary>Gets or sets contrast.</summary>
    public double FoliaTemperaContrast { get => AppSettings.FoliaTemperaContrast; set => AppSettings.FoliaTemperaContrast = (float)value; }
    /// <summary>Gets or sets rgb shift.</summary>
    public double FoliaTemperaRgbShift { get => AppSettings.FoliaTemperaRgbShift; set => AppSettings.FoliaTemperaRgbShift = (float)value; }
    /// <summary>Gets or sets vignette.</summary>
    public double FoliaTemperaVignette { get => AppSettings.FoliaTemperaVignette; set => AppSettings.FoliaTemperaVignette = (float)value; }
    /// <summary>Gets or sets lens curvature.</summary>
    public double FoliaTemperaLens { get => AppSettings.FoliaTemperaLens; set => AppSettings.FoliaTemperaLens = (float)value; }
    /// <summary>Gets or sets light intensity.</summary>
    public double FoliaLumiereLight { get => AppSettings.FoliaLumiereLight; set => AppSettings.FoliaLumiereLight = (float)value; }
    /// <summary>Gets or sets audio response.</summary>
    public double FoliaLumiereAudio { get => AppSettings.FoliaLumiereAudio; set => AppSettings.FoliaLumiereAudio = (float)value; }
    /// <summary>Gets or sets fog density.</summary>
    public double FoliaLumiereFog { get => AppSettings.FoliaLumiereFog; set => AppSettings.FoliaLumiereFog = (float)value; }
    /// <summary>Gets or sets dark field.</summary>
    public double FoliaLumiereDarkField { get => AppSettings.FoliaLumiereDarkField; set => AppSettings.FoliaLumiereDarkField = (float)value; }
    /// <summary>Gets or sets graphics bloom.</summary>
    public double FoliaLumiereBloom { get => AppSettings.FoliaLumiereBloom; set => AppSettings.FoliaLumiereBloom = (float)value; }
    /// <summary>Gets or sets text bloom.</summary>
    public double FoliaLumiereTextBloom { get => AppSettings.FoliaLumiereTextBloom; set => AppSettings.FoliaLumiereTextBloom = (float)value; }
    /// <summary>Gets or sets unlit text opacity.</summary>
    public double FoliaLumiereUnlitOpacity { get => AppSettings.FoliaLumiereUnlitOpacity; set => AppSettings.FoliaLumiereUnlitOpacity = (float)value; }
    /// <summary>Gets or sets mote amount.</summary>
    public double FoliaLumiereMotes { get => AppSettings.FoliaLumiereMotes; set => AppSettings.FoliaLumiereMotes = (float)value; }
    /// <summary>Gets or sets background lyric echoes.</summary>
    public double FoliaLumiereEcho { get => AppSettings.FoliaLumiereEcho; set => AppSettings.FoliaLumiereEcho = (float)value; }
    /// <summary>Gets or sets trail decay.</summary>
    public double FoliaLumiereDecay { get => AppSettings.FoliaLumiereDecay; set => AppSettings.FoliaLumiereDecay = (float)value; }
    /// <summary>Gets or sets line art.</summary>
    public bool FoliaLumiereLineArt { get => AppSettings.FoliaLumiereLineArt; set => AppSettings.FoliaLumiereLineArt = value; }
    /// <summary>Gets or sets foreground bokeh.</summary>
    public bool FoliaLumiereFrontBokeh { get => AppSettings.FoliaLumiereFrontBokeh; set => AppSettings.FoliaLumiereFrontBokeh = value; }
    /// <summary>Gets or sets text trails.</summary>
    public bool FoliaLumiereTrails { get => AppSettings.FoliaLumiereTrails; set => AppSettings.FoliaLumiereTrails = value; }
    /// <summary>Gets or sets overlay frame.</summary>
    public bool FoliaLumiereFrame { get => AppSettings.FoliaLumiereFrame; set => AppSettings.FoliaLumiereFrame = value; }
    /// <summary>Gets or sets text only.</summary>
    public bool FoliaLumiereTextOnly { get => AppSettings.FoliaLumiereTextOnly; set => AppSettings.FoliaLumiereTextOnly = value; }
    /// <summary>Gets or sets theme color mix.</summary>
    public double FoliaLumiereThemeMix { get => AppSettings.FoliaLumiereThemeMix; set => AppSettings.FoliaLumiereThemeMix = (float)value; }
    /// <summary>Gets or sets mesh gradient layer.</summary>
    public bool FoliaLatentMesh { get => AppSettings.FoliaLatentMesh; set => AppSettings.FoliaLatentMesh = value; }
    /// <summary>Gets or sets dithering layer.</summary>
    public bool FoliaLatentDither { get => AppSettings.FoliaLatentDither; set => AppSettings.FoliaLatentDither = value; }
    /// <summary>Gets or sets enhanced beat response.</summary>
    public bool FoliaLatentBeat { get => AppSettings.FoliaLatentBeat; set => AppSettings.FoliaLatentBeat = value; }
    /// <summary>Gets or sets mesh base speed.</summary>
    public double FoliaLatentMeshSpeed { get => AppSettings.FoliaLatentMeshSpeed; set => AppSettings.FoliaLatentMeshSpeed = (float)value; }
    /// <summary>Gets or sets mesh audio speed.</summary>
    public double FoliaLatentMeshAudioSpeed { get => AppSettings.FoliaLatentMeshAudioSpeed; set => AppSettings.FoliaLatentMeshAudioSpeed = (float)value; }
    /// <summary>Gets or sets dithering base speed.</summary>
    public double FoliaLatentDitherSpeed { get => AppSettings.FoliaLatentDitherSpeed; set => AppSettings.FoliaLatentDitherSpeed = (float)value; }
    /// <summary>Gets or sets dithering audio speed.</summary>
    public double FoliaLatentDitherAudioSpeed { get => AppSettings.FoliaLatentDitherAudioSpeed; set => AppSettings.FoliaLatentDitherAudioSpeed = (float)value; }
    /// <summary>Gets or sets dithering size.</summary>
    public double FoliaLatentSize { get => AppSettings.FoliaLatentSize; set => AppSettings.FoliaLatentSize = (float)value; }
    /// <summary>Gets or sets dithering opacity.</summary>
    public double FoliaLatentOpacity { get => AppSettings.FoliaLatentOpacity; set => AppSettings.FoliaLatentOpacity = (float)value; }
    /// <summary>Gets or sets mesh distortion.</summary>
    public double FoliaLatentDistortion { get => AppSettings.FoliaLatentDistortion; set => AppSettings.FoliaLatentDistortion = (float)value; }
    /// <summary>Gets or sets mesh swirl.</summary>
    public double FoliaLatentSwirl { get => AppSettings.FoliaLatentSwirl; set => AppSettings.FoliaLatentSwirl = (float)value; }
    /// <summary>Gets or sets dithering size.</summary>
    public double FoliaNomandSize { get => AppSettings.FoliaNomandSize; set => AppSettings.FoliaNomandSize = (float)value; }
    /// <summary>Gets or sets color steps.</summary>
    public double FoliaNomandSteps { get => AppSettings.FoliaNomandSteps; set => AppSettings.FoliaNomandSteps = (float)value; }
    /// <summary>Gets or sets original colors.</summary>
    public bool FoliaNomandOriginal { get => AppSettings.FoliaNomandOriginal; set => AppSettings.FoliaNomandOriginal = value; }
    /// <summary>Gets or sets inverted.</summary>
    public bool FoliaNomandInverted { get => AppSettings.FoliaNomandInverted; set => AppSettings.FoliaNomandInverted = value; }
    /// <summary>Gets or sets fluted glass size.</summary>
    public double FoliaNomandGlassSize { get => AppSettings.FoliaNomandGlassSize; set => AppSettings.FoliaNomandGlassSize = (float)value; }
    /// <summary>Gets or sets fluted glass distortion.</summary>
    public double FoliaNomandGlassDistortion { get => AppSettings.FoliaNomandGlassDistortion; set => AppSettings.FoliaNomandGlassDistortion = (float)value; }
    /// <summary>Gets or sets fluted glass blur.</summary>
    public double FoliaNomandGlassBlur { get => AppSettings.FoliaNomandGlassBlur; set => AppSettings.FoliaNomandGlassBlur = (float)value; }
    /// <summary>Gets or sets paper contrast.</summary>
    public double FoliaNomandPaperContrast { get => AppSettings.FoliaNomandPaperContrast; set => AppSettings.FoliaNomandPaperContrast = (float)value; }
    /// <summary>Gets or sets paper roughness.</summary>
    public double FoliaNomandPaperRoughness { get => AppSettings.FoliaNomandPaperRoughness; set => AppSettings.FoliaNomandPaperRoughness = (float)value; }
    /// <summary>Gets or sets paper fiber.</summary>
    public double FoliaNomandPaperFiber { get => AppSettings.FoliaNomandPaperFiber; set => AppSettings.FoliaNomandPaperFiber = (float)value; }
    /// <summary>Gets or sets halftone size.</summary>
    public double FoliaNomandDotSize { get => AppSettings.FoliaNomandDotSize; set => AppSettings.FoliaNomandDotSize = (float)value; }
    /// <summary>Gets or sets halftone radius.</summary>
    public double FoliaNomandDotRadius { get => AppSettings.FoliaNomandDotRadius; set => AppSettings.FoliaNomandDotRadius = (float)value; }
    /// <summary>Gets or sets halftone contrast.</summary>
    public double FoliaNomandDotContrast { get => AppSettings.FoliaNomandDotContrast; set => AppSettings.FoliaNomandDotContrast = (float)value; }
    /// <summary>Gets or sets halftone original colors.</summary>
    public bool FoliaNomandDotOriginal { get => AppSettings.FoliaNomandDotOriginal; set => AppSettings.FoliaNomandDotOriginal = value; }
    /// <summary>Gets or sets halftone inverted.</summary>
    public bool FoliaNomandDotInverted { get => AppSettings.FoliaNomandDotInverted; set => AppSettings.FoliaNomandDotInverted = value; }
    /// <summary>Gets or sets lens spread.</summary>
    public double FoliaNomandLensSpread { get => AppSettings.FoliaNomandLensSpread; set => AppSettings.FoliaNomandLensSpread = (float)value; }
    /// <summary>Gets or sets lens bulge.</summary>
    public double FoliaNomandLensBulge { get => AppSettings.FoliaNomandLensBulge; set => AppSettings.FoliaNomandLensBulge = (float)value; }
    /// <summary>Gets or sets lens dispersion.</summary>
    public double FoliaNomandLensDispersion { get => AppSettings.FoliaNomandLensDispersion; set => AppSettings.FoliaNomandLensDispersion = (float)value; }
    /// <summary>Gets or sets background overlay.</summary>
    public bool FoliaNomandOverlay { get => AppSettings.FoliaNomandOverlay; set => AppSettings.FoliaNomandOverlay = value; }
    /// <summary>Gets or sets overlay opacity.</summary>
    public double FoliaNomandOverlayOpacity { get => AppSettings.FoliaNomandOverlayOpacity; set => AppSettings.FoliaNomandOverlayOpacity = (float)value; }
    private void NotifyFoliaTuningSettings()
    {
        OnPropertyChanged(nameof(FoliaFumeGeometry));
        OnPropertyChanged(nameof(FoliaFumeGeometryOpacity));
        OnPropertyChanged(nameof(FoliaFumeGlow));
        OnPropertyChanged(nameof(FoliaDioramaSoul));
        OnPropertyChanged(nameof(FoliaDioramaSoulActive));
        OnPropertyChanged(nameof(FoliaDioramaSoulStrength));
        OnPropertyChanged(nameof(FoliaDioramaGlow));
        OnPropertyChanged(nameof(FoliaDioramaGlowStrength));
        OnPropertyChanged(nameof(FoliaDioramaGradient));
        OnPropertyChanged(nameof(FoliaDioramaParticleScale));
        OnPropertyChanged(nameof(FoliaDioramaParticleGlow));
        OnPropertyChanged(nameof(FoliaTemperaTransitions));
        OnPropertyChanged(nameof(FoliaTemperaBlocks));
        OnPropertyChanged(nameof(FoliaTemperaDecor));
        OnPropertyChanged(nameof(FoliaTemperaInversion));
        OnPropertyChanged(nameof(FoliaTemperaPostProcess));
        OnPropertyChanged(nameof(FoliaTemperaGrain));
        OnPropertyChanged(nameof(FoliaTemperaContrast));
        OnPropertyChanged(nameof(FoliaTemperaRgbShift));
        OnPropertyChanged(nameof(FoliaTemperaVignette));
        OnPropertyChanged(nameof(FoliaTemperaLens));
        OnPropertyChanged(nameof(FoliaLumiereLight));
        OnPropertyChanged(nameof(FoliaLumiereAudio));
        OnPropertyChanged(nameof(FoliaLumiereFog));
        OnPropertyChanged(nameof(FoliaLumiereDarkField));
        OnPropertyChanged(nameof(FoliaLumiereBloom));
        OnPropertyChanged(nameof(FoliaLumiereTextBloom));
        OnPropertyChanged(nameof(FoliaLumiereUnlitOpacity));
        OnPropertyChanged(nameof(FoliaLumiereMotes));
        OnPropertyChanged(nameof(FoliaLumiereEcho));
        OnPropertyChanged(nameof(FoliaLumiereDecay));
        OnPropertyChanged(nameof(FoliaLumiereLineArt));
        OnPropertyChanged(nameof(FoliaLumiereFrontBokeh));
        OnPropertyChanged(nameof(FoliaLumiereTrails));
        OnPropertyChanged(nameof(FoliaLumiereFrame));
        OnPropertyChanged(nameof(FoliaLumiereTextOnly));
        OnPropertyChanged(nameof(FoliaLumiereThemeMix));
        OnPropertyChanged(nameof(FoliaLatentMesh));
        OnPropertyChanged(nameof(FoliaLatentDither));
        OnPropertyChanged(nameof(FoliaLatentBeat));
        OnPropertyChanged(nameof(FoliaLatentMeshSpeed));
        OnPropertyChanged(nameof(FoliaLatentMeshAudioSpeed));
        OnPropertyChanged(nameof(FoliaLatentDitherSpeed));
        OnPropertyChanged(nameof(FoliaLatentDitherAudioSpeed));
        OnPropertyChanged(nameof(FoliaLatentSize));
        OnPropertyChanged(nameof(FoliaLatentOpacity));
        OnPropertyChanged(nameof(FoliaLatentDistortion));
        OnPropertyChanged(nameof(FoliaLatentSwirl));
        OnPropertyChanged(nameof(FoliaNomandSize));
        OnPropertyChanged(nameof(FoliaNomandSteps));
        OnPropertyChanged(nameof(FoliaNomandOriginal));
        OnPropertyChanged(nameof(FoliaNomandInverted));
        OnPropertyChanged(nameof(FoliaNomandGlassSize));
        OnPropertyChanged(nameof(FoliaNomandGlassDistortion));
        OnPropertyChanged(nameof(FoliaNomandGlassBlur));
        OnPropertyChanged(nameof(FoliaNomandPaperContrast));
        OnPropertyChanged(nameof(FoliaNomandPaperRoughness));
        OnPropertyChanged(nameof(FoliaNomandPaperFiber));
        OnPropertyChanged(nameof(FoliaNomandDotSize));
        OnPropertyChanged(nameof(FoliaNomandDotRadius));
        OnPropertyChanged(nameof(FoliaNomandDotContrast));
        OnPropertyChanged(nameof(FoliaNomandDotOriginal));
        OnPropertyChanged(nameof(FoliaNomandDotInverted));
        OnPropertyChanged(nameof(FoliaNomandLensSpread));
        OnPropertyChanged(nameof(FoliaNomandLensBulge));
        OnPropertyChanged(nameof(FoliaNomandLensDispersion));
        OnPropertyChanged(nameof(FoliaNomandOverlay));
        OnPropertyChanged(nameof(FoliaNomandOverlayOpacity));
    }
}
