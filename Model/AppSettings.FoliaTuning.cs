using System;
namespace WinExSpectrumTest.Model;
/// <summary>Contains renderer tuning with Folia-compatible defaults.</summary>
public partial class AppSettings
{
    private static bool _FoliaFumeGeometry = false;
    /// <summary>Gets or sets geometric background.</summary>
    public static bool FoliaFumeGeometry { get => _FoliaFumeGeometry; set => Set(ref _FoliaFumeGeometry, value); }
    private static float _FoliaFumeGeometryOpacity = .5f;
    /// <summary>Gets or sets geometry opacity.</summary>
    public static float FoliaFumeGeometryOpacity { get => _FoliaFumeGeometryOpacity; set => Set(ref _FoliaFumeGeometryOpacity, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .5f); }
    private static float _FoliaFumeGlow = 1;
    /// <summary>Gets or sets glow intensity.</summary>
    public static float FoliaFumeGlow { get => _FoliaFumeGlow; set => Set(ref _FoliaFumeGlow, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static bool _FoliaDioramaSoul = true;
    /// <summary>Gets or sets soul drift.</summary>
    public static bool FoliaDioramaSoul { get => _FoliaDioramaSoul; set => Set(ref _FoliaDioramaSoul, value); }
    private static bool _FoliaDioramaSoulActive = false;
    /// <summary>Gets or sets drift while singing.</summary>
    public static bool FoliaDioramaSoulActive { get => _FoliaDioramaSoulActive; set => Set(ref _FoliaDioramaSoulActive, value); }
    private static float _FoliaDioramaSoulStrength = 1;
    /// <summary>Gets or sets soul strength.</summary>
    public static float FoliaDioramaSoulStrength { get => _FoliaDioramaSoulStrength; set => Set(ref _FoliaDioramaSoulStrength, float.IsFinite(value) ? Math.Clamp(value, 0f, 1.5f) : 1); }
    private static bool _FoliaDioramaGlow = true;
    /// <summary>Gets or sets singing glow.</summary>
    public static bool FoliaDioramaGlow { get => _FoliaDioramaGlow; set => Set(ref _FoliaDioramaGlow, value); }
    private static float _FoliaDioramaGlowStrength = 1;
    /// <summary>Gets or sets glow strength.</summary>
    public static float FoliaDioramaGlowStrength { get => _FoliaDioramaGlowStrength; set => Set(ref _FoliaDioramaGlowStrength, float.IsFinite(value) ? Math.Clamp(value, 0f, 1.5f) : 1); }
    private static bool _FoliaDioramaGradient = false;
    /// <summary>Gets or sets progressive lyric color.</summary>
    public static bool FoliaDioramaGradient { get => _FoliaDioramaGradient; set => Set(ref _FoliaDioramaGradient, value); }
    private static float _FoliaDioramaParticleScale = 1;
    /// <summary>Gets or sets particle size.</summary>
    public static float FoliaDioramaParticleScale { get => _FoliaDioramaParticleScale; set => Set(ref _FoliaDioramaParticleScale, float.IsFinite(value) ? Math.Clamp(value, 0.25f, 3f) : 1); }
    private static float _FoliaDioramaParticleGlow = .65f;
    /// <summary>Gets or sets particle glow.</summary>
    public static float FoliaDioramaParticleGlow { get => _FoliaDioramaParticleGlow; set => Set(ref _FoliaDioramaParticleGlow, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : .65f); }
    private static bool _FoliaTemperaTransitions = true;
    /// <summary>Gets or sets scene transitions.</summary>
    public static bool FoliaTemperaTransitions { get => _FoliaTemperaTransitions; set => Set(ref _FoliaTemperaTransitions, value); }
    private static bool _FoliaTemperaBlocks = true;
    /// <summary>Gets or sets composition blocks.</summary>
    public static bool FoliaTemperaBlocks { get => _FoliaTemperaBlocks; set => Set(ref _FoliaTemperaBlocks, value); }
    private static bool _FoliaTemperaDecor = true;
    /// <summary>Gets or sets decorations.</summary>
    public static bool FoliaTemperaDecor { get => _FoliaTemperaDecor; set => Set(ref _FoliaTemperaDecor, value); }
    private static bool _FoliaTemperaInversion = true;
    /// <summary>Gets or sets text inversion.</summary>
    public static bool FoliaTemperaInversion { get => _FoliaTemperaInversion; set => Set(ref _FoliaTemperaInversion, value); }
    private static bool _FoliaTemperaPostProcess = true;
    /// <summary>Gets or sets post processing.</summary>
    public static bool FoliaTemperaPostProcess { get => _FoliaTemperaPostProcess; set => Set(ref _FoliaTemperaPostProcess, value); }
    private static float _FoliaTemperaGrain = .2f;
    /// <summary>Gets or sets grain.</summary>
    public static float FoliaTemperaGrain { get => _FoliaTemperaGrain; set => Set(ref _FoliaTemperaGrain, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .2f); }
    private static float _FoliaTemperaContrast = 0;
    /// <summary>Gets or sets contrast.</summary>
    public static float FoliaTemperaContrast { get => _FoliaTemperaContrast; set => Set(ref _FoliaTemperaContrast, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0); }
    private static float _FoliaTemperaRgbShift = 0;
    /// <summary>Gets or sets rgb shift.</summary>
    public static float FoliaTemperaRgbShift { get => _FoliaTemperaRgbShift; set => Set(ref _FoliaTemperaRgbShift, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0); }
    private static float _FoliaTemperaVignette = .85f;
    /// <summary>Gets or sets vignette.</summary>
    public static float FoliaTemperaVignette { get => _FoliaTemperaVignette; set => Set(ref _FoliaTemperaVignette, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : .85f); }
    private static float _FoliaTemperaLens = .3f;
    /// <summary>Gets or sets lens curvature.</summary>
    public static float FoliaTemperaLens { get => _FoliaTemperaLens; set => Set(ref _FoliaTemperaLens, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : .3f); }
    private static float _FoliaLumiereLight = 1;
    /// <summary>Gets or sets light intensity.</summary>
    public static float FoliaLumiereLight { get => _FoliaLumiereLight; set => Set(ref _FoliaLumiereLight, float.IsFinite(value) ? Math.Clamp(value, 0.3f, 2f) : 1); }
    private static float _FoliaLumiereAudio = 1;
    /// <summary>Gets or sets audio response.</summary>
    public static float FoliaLumiereAudio { get => _FoliaLumiereAudio; set => Set(ref _FoliaLumiereAudio, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static float _FoliaLumiereFog = 1;
    /// <summary>Gets or sets fog density.</summary>
    public static float FoliaLumiereFog { get => _FoliaLumiereFog; set => Set(ref _FoliaLumiereFog, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static float _FoliaLumiereDarkField = .75f;
    /// <summary>Gets or sets dark field.</summary>
    public static float FoliaLumiereDarkField { get => _FoliaLumiereDarkField; set => Set(ref _FoliaLumiereDarkField, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .75f); }
    private static float _FoliaLumiereBloom = 1;
    /// <summary>Gets or sets graphics bloom.</summary>
    public static float FoliaLumiereBloom { get => _FoliaLumiereBloom; set => Set(ref _FoliaLumiereBloom, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static float _FoliaLumiereTextBloom = 1;
    /// <summary>Gets or sets text bloom.</summary>
    public static float FoliaLumiereTextBloom { get => _FoliaLumiereTextBloom; set => Set(ref _FoliaLumiereTextBloom, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static float _FoliaLumiereUnlitOpacity = .22f;
    /// <summary>Gets or sets unlit text opacity.</summary>
    public static float FoliaLumiereUnlitOpacity { get => _FoliaLumiereUnlitOpacity; set => Set(ref _FoliaLumiereUnlitOpacity, float.IsFinite(value) ? Math.Clamp(value, .05f, .6f) : .22f); }
    private static float _FoliaLumiereMotes = 1;
    /// <summary>Gets or sets mote amount.</summary>
    public static float FoliaLumiereMotes { get => _FoliaLumiereMotes; set => Set(ref _FoliaLumiereMotes, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static float _FoliaLumiereEcho = 1;
    /// <summary>Gets or sets background lyric echoes.</summary>
    public static float FoliaLumiereEcho { get => _FoliaLumiereEcho; set => Set(ref _FoliaLumiereEcho, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static float _FoliaLumiereDecay = 1;
    /// <summary>Gets or sets trail decay.</summary>
    public static float FoliaLumiereDecay { get => _FoliaLumiereDecay; set => Set(ref _FoliaLumiereDecay, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1); }
    private static bool _FoliaLumiereLineArt = true;
    /// <summary>Gets or sets line art.</summary>
    public static bool FoliaLumiereLineArt { get => _FoliaLumiereLineArt; set => Set(ref _FoliaLumiereLineArt, value); }
    private static bool _FoliaLumiereFrontBokeh = true;
    /// <summary>Gets or sets foreground bokeh.</summary>
    public static bool FoliaLumiereFrontBokeh { get => _FoliaLumiereFrontBokeh; set => Set(ref _FoliaLumiereFrontBokeh, value); }
    private static bool _FoliaLumiereTrails = true;
    /// <summary>Gets or sets text trails.</summary>
    public static bool FoliaLumiereTrails { get => _FoliaLumiereTrails; set => Set(ref _FoliaLumiereTrails, value); }
    private static bool _FoliaLumiereFrame = true;
    /// <summary>Gets or sets overlay frame.</summary>
    public static bool FoliaLumiereFrame { get => _FoliaLumiereFrame; set => Set(ref _FoliaLumiereFrame, value); }
    private static bool _FoliaLumiereTextOnly = false;
    /// <summary>Gets or sets text only.</summary>
    public static bool FoliaLumiereTextOnly { get => _FoliaLumiereTextOnly; set => Set(ref _FoliaLumiereTextOnly, value); }
    private static float _FoliaLumiereThemeMix = .3f;
    /// <summary>Gets or sets theme color mix.</summary>
    public static float FoliaLumiereThemeMix { get => _FoliaLumiereThemeMix; set => Set(ref _FoliaLumiereThemeMix, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .3f); }
    private static bool _FoliaLatentMesh = true;
    /// <summary>Gets or sets mesh gradient layer.</summary>
    public static bool FoliaLatentMesh { get => _FoliaLatentMesh; set => Set(ref _FoliaLatentMesh, value); }
    private static bool _FoliaLatentDither = true;
    /// <summary>Gets or sets dithering layer.</summary>
    public static bool FoliaLatentDither { get => _FoliaLatentDither; set => Set(ref _FoliaLatentDither, value); }
    private static bool _FoliaLatentBeat = true;
    /// <summary>Gets or sets enhanced beat response.</summary>
    public static bool FoliaLatentBeat { get => _FoliaLatentBeat; set => Set(ref _FoliaLatentBeat, value); }
    private static float _FoliaLatentMeshSpeed = .3f;
    /// <summary>Gets or sets mesh base speed.</summary>
    public static float FoliaLatentMeshSpeed { get => _FoliaLatentMeshSpeed; set => Set(ref _FoliaLatentMeshSpeed, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : .3f); }
    private static float _FoliaLatentMeshAudioSpeed = 2;
    /// <summary>Gets or sets mesh audio speed.</summary>
    public static float FoliaLatentMeshAudioSpeed { get => _FoliaLatentMeshAudioSpeed; set => Set(ref _FoliaLatentMeshAudioSpeed, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 2); }
    private static float _FoliaLatentDitherSpeed = .1f;
    /// <summary>Gets or sets dithering base speed.</summary>
    public static float FoliaLatentDitherSpeed { get => _FoliaLatentDitherSpeed; set => Set(ref _FoliaLatentDitherSpeed, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : .1f); }
    private static float _FoliaLatentDitherAudioSpeed = 1.2f;
    /// <summary>Gets or sets dithering audio speed.</summary>
    public static float FoliaLatentDitherAudioSpeed { get => _FoliaLatentDitherAudioSpeed; set => Set(ref _FoliaLatentDitherAudioSpeed, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : 1.2f); }
    private static float _FoliaLatentSize = 2.5f;
    /// <summary>Gets or sets dithering size.</summary>
    public static float FoliaLatentSize { get => _FoliaLatentSize; set => Set(ref _FoliaLatentSize, float.IsFinite(value) ? Math.Clamp(value, 0.5f, 8f) : 2.5f); }
    private static float _FoliaLatentOpacity = .55f;
    /// <summary>Gets or sets dithering opacity.</summary>
    public static float FoliaLatentOpacity { get => _FoliaLatentOpacity; set => Set(ref _FoliaLatentOpacity, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .55f); }
    private static float _FoliaLatentDistortion = .8f;
    /// <summary>Gets or sets mesh distortion.</summary>
    public static float FoliaLatentDistortion { get => _FoliaLatentDistortion; set => Set(ref _FoliaLatentDistortion, float.IsFinite(value) ? Math.Clamp(value, 0f, 2f) : .8f); }
    private static float _FoliaLatentSwirl = .1f;
    /// <summary>Gets or sets mesh swirl.</summary>
    public static float FoliaLatentSwirl { get => _FoliaLatentSwirl; set => Set(ref _FoliaLatentSwirl, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .1f); }
    private static float _FoliaNomandSize = 3;
    /// <summary>Gets or sets dithering size.</summary>
    public static float FoliaNomandSize { get => _FoliaNomandSize; set => Set(ref _FoliaNomandSize, float.IsFinite(value) ? Math.Clamp(value, 0.5f, 20f) : 3); }
    private static float _FoliaNomandSteps = 4;
    /// <summary>Gets or sets color steps.</summary>
    public static float FoliaNomandSteps { get => _FoliaNomandSteps; set => Set(ref _FoliaNomandSteps, float.IsFinite(value) ? MathF.Round(Math.Clamp(value, 1f, 7f)) : 4); }
    private static bool _FoliaNomandOriginal = false;
    /// <summary>Gets or sets original colors.</summary>
    public static bool FoliaNomandOriginal { get => _FoliaNomandOriginal; set => Set(ref _FoliaNomandOriginal, value); }
    private static bool _FoliaNomandInverted = false;
    /// <summary>Gets or sets inverted.</summary>
    public static bool FoliaNomandInverted { get => _FoliaNomandInverted; set => Set(ref _FoliaNomandInverted, value); }
    private static float _FoliaNomandGlassSize = .5f;
    /// <summary>Gets or sets fluted glass size.</summary>
    public static float FoliaNomandGlassSize { get => _FoliaNomandGlassSize; set => Set(ref _FoliaNomandGlassSize, float.IsFinite(value) ? Math.Clamp(value, 0.1f, 1f) : .5f); }
    private static float _FoliaNomandGlassDistortion = .5f;
    /// <summary>Gets or sets fluted glass distortion.</summary>
    public static float FoliaNomandGlassDistortion { get => _FoliaNomandGlassDistortion; set => Set(ref _FoliaNomandGlassDistortion, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .5f); }
    private static float _FoliaNomandGlassBlur = .08f;
    /// <summary>Gets or sets fluted glass blur.</summary>
    public static float FoliaNomandGlassBlur { get => _FoliaNomandGlassBlur; set => Set(ref _FoliaNomandGlassBlur, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .08f); }
    private static float _FoliaNomandPaperContrast = .32f;
    /// <summary>Gets or sets paper contrast.</summary>
    public static float FoliaNomandPaperContrast { get => _FoliaNomandPaperContrast; set => Set(ref _FoliaNomandPaperContrast, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .32f); }
    private static float _FoliaNomandPaperRoughness = .42f;
    /// <summary>Gets or sets paper roughness.</summary>
    public static float FoliaNomandPaperRoughness { get => _FoliaNomandPaperRoughness; set => Set(ref _FoliaNomandPaperRoughness, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .42f); }
    private static float _FoliaNomandPaperFiber = .3f;
    /// <summary>Gets or sets paper fiber.</summary>
    public static float FoliaNomandPaperFiber { get => _FoliaNomandPaperFiber; set => Set(ref _FoliaNomandPaperFiber, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .3f); }
    private static float _FoliaNomandDotSize = .5f;
    /// <summary>Gets or sets halftone size.</summary>
    public static float FoliaNomandDotSize { get => _FoliaNomandDotSize; set => Set(ref _FoliaNomandDotSize, float.IsFinite(value) ? Math.Clamp(value, .1f, 1f) : .5f); }
    private static float _FoliaNomandDotRadius = 1.25f;
    /// <summary>Gets or sets halftone radius.</summary>
    public static float FoliaNomandDotRadius { get => _FoliaNomandDotRadius; set => Set(ref _FoliaNomandDotRadius, float.IsFinite(value) ? Math.Clamp(value, .1f, 2f) : 1.25f); }
    private static float _FoliaNomandDotContrast = .4f;
    /// <summary>Gets or sets halftone contrast.</summary>
    public static float FoliaNomandDotContrast { get => _FoliaNomandDotContrast; set => Set(ref _FoliaNomandDotContrast, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .4f); }
    private static bool _FoliaNomandDotOriginal = false;
    /// <summary>Gets or sets halftone original colors.</summary>
    public static bool FoliaNomandDotOriginal { get => _FoliaNomandDotOriginal; set => Set(ref _FoliaNomandDotOriginal, value); }
    private static bool _FoliaNomandDotInverted = false;
    /// <summary>Gets or sets halftone inverted.</summary>
    public static bool FoliaNomandDotInverted { get => _FoliaNomandDotInverted; set => Set(ref _FoliaNomandDotInverted, value); }
    private static float _FoliaNomandLensSpread = .45f;
    /// <summary>Gets or sets lens spread.</summary>
    public static float FoliaNomandLensSpread { get => _FoliaNomandLensSpread; set => Set(ref _FoliaNomandLensSpread, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .45f); }
    private static float _FoliaNomandLensBulge = .3f;
    /// <summary>Gets or sets lens bulge.</summary>
    public static float FoliaNomandLensBulge { get => _FoliaNomandLensBulge; set => Set(ref _FoliaNomandLensBulge, float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : .3f); }
    private static float _FoliaNomandLensDispersion = .65f;
    /// <summary>Gets or sets lens dispersion.</summary>
    public static float FoliaNomandLensDispersion { get => _FoliaNomandLensDispersion; set => Set(ref _FoliaNomandLensDispersion, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .65f); }
    private static bool _FoliaNomandOverlay = true;
    /// <summary>Gets or sets background overlay.</summary>
    public static bool FoliaNomandOverlay { get => _FoliaNomandOverlay; set => Set(ref _FoliaNomandOverlay, value); }
    private static float _FoliaNomandOverlayOpacity = .35f;
    /// <summary>Gets or sets overlay opacity.</summary>
    public static float FoliaNomandOverlayOpacity { get => _FoliaNomandOverlayOpacity; set => Set(ref _FoliaNomandOverlayOpacity, float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : .35f); }
}
