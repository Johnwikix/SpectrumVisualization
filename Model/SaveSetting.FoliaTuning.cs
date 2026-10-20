namespace WinExSpectrumTest.Model;
/// <summary>Persists Folia renderer tuning through the generated JSON context.</summary>
public partial class SaveSetting
{
    /// <summary>Gets or sets geometric background.</summary>
    public bool FoliaFumeGeometry { get; set; } = false;
    /// <summary>Gets or sets geometry opacity.</summary>
    public float FoliaFumeGeometryOpacity { get; set; } = .5f;
    /// <summary>Gets or sets glow intensity.</summary>
    public float FoliaFumeGlow { get; set; } = 1;
    /// <summary>Gets or sets soul drift.</summary>
    public bool FoliaDioramaSoul { get; set; } = true;
    /// <summary>Gets or sets drift while singing.</summary>
    public bool FoliaDioramaSoulActive { get; set; } = false;
    /// <summary>Gets or sets soul strength.</summary>
    public float FoliaDioramaSoulStrength { get; set; } = 1;
    /// <summary>Gets or sets singing glow.</summary>
    public bool FoliaDioramaGlow { get; set; } = true;
    /// <summary>Gets or sets glow strength.</summary>
    public float FoliaDioramaGlowStrength { get; set; } = 1;
    /// <summary>Gets or sets progressive lyric color.</summary>
    public bool FoliaDioramaGradient { get; set; } = false;
    /// <summary>Gets or sets particle size.</summary>
    public float FoliaDioramaParticleScale { get; set; } = 1;
    /// <summary>Gets or sets particle glow.</summary>
    public float FoliaDioramaParticleGlow { get; set; } = .65f;
    /// <summary>Gets or sets scene transitions.</summary>
    public bool FoliaTemperaTransitions { get; set; } = true;
    /// <summary>Gets or sets composition blocks.</summary>
    public bool FoliaTemperaBlocks { get; set; } = true;
    /// <summary>Gets or sets decorations.</summary>
    public bool FoliaTemperaDecor { get; set; } = true;
    /// <summary>Gets or sets text inversion.</summary>
    public bool FoliaTemperaInversion { get; set; } = true;
    /// <summary>Gets or sets post processing.</summary>
    public bool FoliaTemperaPostProcess { get; set; } = true;
    /// <summary>Gets or sets grain.</summary>
    public float FoliaTemperaGrain { get; set; } = .2f;
    /// <summary>Gets or sets contrast.</summary>
    public float FoliaTemperaContrast { get; set; } = 0;
    /// <summary>Gets or sets rgb shift.</summary>
    public float FoliaTemperaRgbShift { get; set; } = 0;
    /// <summary>Gets or sets vignette.</summary>
    public float FoliaTemperaVignette { get; set; } = .85f;
    /// <summary>Gets or sets lens curvature.</summary>
    public float FoliaTemperaLens { get; set; } = .3f;
    /// <summary>Gets or sets light intensity.</summary>
    public float FoliaLumiereLight { get; set; } = 1;
    /// <summary>Gets or sets audio response.</summary>
    public float FoliaLumiereAudio { get; set; } = 1;
    /// <summary>Gets or sets fog density.</summary>
    public float FoliaLumiereFog { get; set; } = 1;
    /// <summary>Gets or sets dark field.</summary>
    public float FoliaLumiereDarkField { get; set; } = .75f;
    /// <summary>Gets or sets graphics bloom.</summary>
    public float FoliaLumiereBloom { get; set; } = 1;
    /// <summary>Gets or sets text bloom.</summary>
    public float FoliaLumiereTextBloom { get; set; } = 1;
    /// <summary>Gets or sets unlit text opacity.</summary>
    public float FoliaLumiereUnlitOpacity { get; set; } = .22f;
    /// <summary>Gets or sets mote amount.</summary>
    public float FoliaLumiereMotes { get; set; } = 1;
    /// <summary>Gets or sets background lyric echoes.</summary>
    public float FoliaLumiereEcho { get; set; } = 1;
    /// <summary>Gets or sets trail decay.</summary>
    public float FoliaLumiereDecay { get; set; } = 1;
    /// <summary>Gets or sets line art.</summary>
    public bool FoliaLumiereLineArt { get; set; } = true;
    /// <summary>Gets or sets foreground bokeh.</summary>
    public bool FoliaLumiereFrontBokeh { get; set; } = true;
    /// <summary>Gets or sets text trails.</summary>
    public bool FoliaLumiereTrails { get; set; } = true;
    /// <summary>Gets or sets overlay frame.</summary>
    public bool FoliaLumiereFrame { get; set; } = true;
    /// <summary>Gets or sets text only.</summary>
    public bool FoliaLumiereTextOnly { get; set; } = false;
    /// <summary>Gets or sets theme color mix.</summary>
    public float FoliaLumiereThemeMix { get; set; } = .3f;
    /// <summary>Gets or sets mesh gradient layer.</summary>
    public bool FoliaLatentMesh { get; set; } = true;
    /// <summary>Gets or sets dithering layer.</summary>
    public bool FoliaLatentDither { get; set; } = true;
    /// <summary>Gets or sets enhanced beat response.</summary>
    public bool FoliaLatentBeat { get; set; } = true;
    /// <summary>Gets or sets mesh base speed.</summary>
    public float FoliaLatentMeshSpeed { get; set; } = .3f;
    /// <summary>Gets or sets mesh audio speed.</summary>
    public float FoliaLatentMeshAudioSpeed { get; set; } = 2;
    /// <summary>Gets or sets dithering base speed.</summary>
    public float FoliaLatentDitherSpeed { get; set; } = .1f;
    /// <summary>Gets or sets dithering audio speed.</summary>
    public float FoliaLatentDitherAudioSpeed { get; set; } = 1.2f;
    /// <summary>Gets or sets dithering size.</summary>
    public float FoliaLatentSize { get; set; } = 2.5f;
    /// <summary>Gets or sets dithering opacity.</summary>
    public float FoliaLatentOpacity { get; set; } = .55f;
    /// <summary>Gets or sets mesh distortion.</summary>
    public float FoliaLatentDistortion { get; set; } = .8f;
    /// <summary>Gets or sets mesh swirl.</summary>
    public float FoliaLatentSwirl { get; set; } = .1f;
    /// <summary>Gets or sets dithering size.</summary>
    public float FoliaNomandSize { get; set; } = 3;
    /// <summary>Gets or sets color steps.</summary>
    public float FoliaNomandSteps { get; set; } = 4;
    /// <summary>Gets or sets original colors.</summary>
    public bool FoliaNomandOriginal { get; set; } = false;
    /// <summary>Gets or sets inverted.</summary>
    public bool FoliaNomandInverted { get; set; } = false;
    /// <summary>Gets or sets fluted glass size.</summary>
    public float FoliaNomandGlassSize { get; set; } = .5f;
    /// <summary>Gets or sets fluted glass distortion.</summary>
    public float FoliaNomandGlassDistortion { get; set; } = .5f;
    /// <summary>Gets or sets fluted glass blur.</summary>
    public float FoliaNomandGlassBlur { get; set; } = .08f;
    /// <summary>Gets or sets paper contrast.</summary>
    public float FoliaNomandPaperContrast { get; set; } = .32f;
    /// <summary>Gets or sets paper roughness.</summary>
    public float FoliaNomandPaperRoughness { get; set; } = .42f;
    /// <summary>Gets or sets paper fiber.</summary>
    public float FoliaNomandPaperFiber { get; set; } = .3f;
    /// <summary>Gets or sets halftone size.</summary>
    public float FoliaNomandDotSize { get; set; } = .5f;
    /// <summary>Gets or sets halftone radius.</summary>
    public float FoliaNomandDotRadius { get; set; } = 1.25f;
    /// <summary>Gets or sets halftone contrast.</summary>
    public float FoliaNomandDotContrast { get; set; } = .4f;
    /// <summary>Gets or sets halftone original colors.</summary>
    public bool FoliaNomandDotOriginal { get; set; } = false;
    /// <summary>Gets or sets halftone inverted.</summary>
    public bool FoliaNomandDotInverted { get; set; } = false;
    /// <summary>Gets or sets lens spread.</summary>
    public float FoliaNomandLensSpread { get; set; } = .45f;
    /// <summary>Gets or sets lens bulge.</summary>
    public float FoliaNomandLensBulge { get; set; } = .3f;
    /// <summary>Gets or sets lens dispersion.</summary>
    public float FoliaNomandLensDispersion { get; set; } = .65f;
    /// <summary>Gets or sets background overlay.</summary>
    public bool FoliaNomandOverlay { get; set; } = true;
    /// <summary>Gets or sets overlay opacity.</summary>
    public float FoliaNomandOverlayOpacity { get; set; } = .35f;
}
