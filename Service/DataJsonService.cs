using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using WinExSpectrumTest.Manager;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Service
{
    public class DataJsonService
    {
        public static async Task LoadSettingAsync()
        {
            ApplySettings(await SettingManager.LoadSettingsAsync());
        }

        /// <summary>恢复全部持久化设置为默认值（SaveSetting 的属性初始值即默认值）并落盘。</summary>
        public static async Task ResetSettingAsync()
        {
            var defaults = new SaveSetting();
            ApplySettings(defaults);
            await SettingManager.SaveSettingsAsync(defaults);
        }

        /// <summary>把 SaveSetting 快照写入运行时 AppSettings；每项赋值都会发布 Changed 通知。</summary>
        private static void ApplySettings(SaveSetting settings)
        {
            AppSettings.FoliaTheme = settings.FoliaTheme;
            AppSettings.LyricsCacheEnabled = settings.LyricsCacheEnabled;
            AppSettings.LyricsCacheFolder = settings.LyricsCacheFolder;
            AppSettings.FoliaFumeGeometry = settings.FoliaFumeGeometry;
            AppSettings.FoliaFumeGeometryOpacity = settings.FoliaFumeGeometryOpacity;
            AppSettings.FoliaFumeGlow = settings.FoliaFumeGlow;
            AppSettings.FoliaDioramaSoul = settings.FoliaDioramaSoul;
            AppSettings.FoliaDioramaSoulActive = settings.FoliaDioramaSoulActive;
            AppSettings.FoliaDioramaSoulStrength = settings.FoliaDioramaSoulStrength;
            AppSettings.FoliaDioramaGlow = settings.FoliaDioramaGlow;
            AppSettings.FoliaDioramaGlowStrength = settings.FoliaDioramaGlowStrength;
            AppSettings.FoliaDioramaGradient = settings.FoliaDioramaGradient;
            AppSettings.FoliaDioramaParticleScale = settings.FoliaDioramaParticleScale;
            AppSettings.FoliaDioramaParticleGlow = settings.FoliaDioramaParticleGlow;
            AppSettings.FoliaTemperaTransitions = settings.FoliaTemperaTransitions;
            AppSettings.FoliaTemperaBlocks = settings.FoliaTemperaBlocks;
            AppSettings.FoliaTemperaDecor = settings.FoliaTemperaDecor;
            AppSettings.FoliaTemperaInversion = settings.FoliaTemperaInversion;
            AppSettings.FoliaTemperaPostProcess = settings.FoliaTemperaPostProcess;
            AppSettings.FoliaTemperaGrain = settings.FoliaTemperaGrain;
            AppSettings.FoliaTemperaContrast = settings.FoliaTemperaContrast;
            AppSettings.FoliaTemperaRgbShift = settings.FoliaTemperaRgbShift;
            AppSettings.FoliaTemperaVignette = settings.FoliaTemperaVignette;
            AppSettings.FoliaTemperaLens = settings.FoliaTemperaLens;
            AppSettings.FoliaLumiereLight = settings.FoliaLumiereLight;
            AppSettings.FoliaLumiereAudio = settings.FoliaLumiereAudio;
            AppSettings.FoliaLumiereFog = settings.FoliaLumiereFog;
            AppSettings.FoliaLumiereDarkField = settings.FoliaLumiereDarkField;
            AppSettings.FoliaLumiereBloom = settings.FoliaLumiereBloom;
            AppSettings.FoliaLumiereTextBloom = settings.FoliaLumiereTextBloom;
            AppSettings.FoliaLumiereUnlitOpacity = settings.FoliaLumiereUnlitOpacity;
            AppSettings.FoliaLumiereMotes = settings.FoliaLumiereMotes;
            AppSettings.FoliaLumiereEcho = settings.FoliaLumiereEcho;
            AppSettings.FoliaLumiereDecay = settings.FoliaLumiereDecay;
            AppSettings.FoliaLumiereLineArt = settings.FoliaLumiereLineArt;
            AppSettings.FoliaLumiereFrontBokeh = settings.FoliaLumiereFrontBokeh;
            AppSettings.FoliaLumiereTrails = settings.FoliaLumiereTrails;
            AppSettings.FoliaLumiereFrame = settings.FoliaLumiereFrame;
            AppSettings.FoliaLumiereTextOnly = settings.FoliaLumiereTextOnly;
            AppSettings.FoliaLumiereThemeMix = settings.FoliaLumiereThemeMix;
            AppSettings.FoliaLatentMesh = settings.FoliaLatentMesh;
            AppSettings.FoliaLatentDither = settings.FoliaLatentDither;
            AppSettings.FoliaLatentBeat = settings.FoliaLatentBeat;
            AppSettings.FoliaLatentMeshSpeed = settings.FoliaLatentMeshSpeed;
            AppSettings.FoliaLatentMeshAudioSpeed = settings.FoliaLatentMeshAudioSpeed;
            AppSettings.FoliaLatentDitherSpeed = settings.FoliaLatentDitherSpeed;
            AppSettings.FoliaLatentDitherAudioSpeed = settings.FoliaLatentDitherAudioSpeed;
            AppSettings.FoliaLatentSize = settings.FoliaLatentSize;
            AppSettings.FoliaLatentOpacity = settings.FoliaLatentOpacity;
            AppSettings.FoliaLatentDistortion = settings.FoliaLatentDistortion;
            AppSettings.FoliaLatentSwirl = settings.FoliaLatentSwirl;
            AppSettings.FoliaNomandSize = settings.FoliaNomandSize;
            AppSettings.FoliaNomandSteps = settings.FoliaNomandSteps;
            AppSettings.FoliaNomandOriginal = settings.FoliaNomandOriginal;
            AppSettings.FoliaNomandInverted = settings.FoliaNomandInverted;
            AppSettings.FoliaNomandGlassSize = settings.FoliaNomandGlassSize;
            AppSettings.FoliaNomandGlassDistortion = settings.FoliaNomandGlassDistortion;
            AppSettings.FoliaNomandGlassBlur = settings.FoliaNomandGlassBlur;
            AppSettings.FoliaNomandPaperContrast = settings.FoliaNomandPaperContrast;
            AppSettings.FoliaNomandPaperRoughness = settings.FoliaNomandPaperRoughness;
            AppSettings.FoliaNomandPaperFiber = settings.FoliaNomandPaperFiber;
            AppSettings.FoliaNomandDotSize = settings.FoliaNomandDotSize;
            AppSettings.FoliaNomandDotRadius = settings.FoliaNomandDotRadius;
            AppSettings.FoliaNomandDotContrast = settings.FoliaNomandDotContrast;
            AppSettings.FoliaNomandDotOriginal = settings.FoliaNomandDotOriginal;
            AppSettings.FoliaNomandDotInverted = settings.FoliaNomandDotInverted;
            AppSettings.FoliaNomandLensSpread = settings.FoliaNomandLensSpread;
            AppSettings.FoliaNomandLensBulge = settings.FoliaNomandLensBulge;
            AppSettings.FoliaNomandLensDispersion = settings.FoliaNomandLensDispersion;
            AppSettings.FoliaNomandOverlay = settings.FoliaNomandOverlay;
            AppSettings.FoliaNomandOverlayOpacity = settings.FoliaNomandOverlayOpacity;
            AppSettings.FoliaSonnetBackgroundMg = settings.FoliaSonnetBackgroundMg;
            AppSettings.FoliaSonnetFixedGeometry = settings.FoliaSonnetFixedGeometry;
            AppSettings.FoliaSonnetDecorations = settings.FoliaSonnetDecorations;
            AppSettings.FoliaSonnetGuides = settings.FoliaSonnetGuides;
            AppSettings.FoliaSonnetFrame = settings.FoliaSonnetFrame;
            AppSettings.FoliaSonnetTransitions = settings.FoliaSonnetTransitions;
            AppSettings.FoliaSonnetPostProcess = settings.FoliaSonnetPostProcess;
            AppSettings.FoliaRealtimeMotion = settings.FoliaRealtimeMotion;
            AppSettings.FoliaSonnetMotionStrength = settings.FoliaSonnetMotionStrength;
            AppSettings.FoliaDioramaMotionStrength = settings.FoliaDioramaMotionStrength;
            AppSettings.FoliaPendoloMotionStrength = settings.FoliaPendoloMotionStrength;
            AppSettings.FoliaLyricMode = settings.FoliaLyricMode;
            AppSettings.FoliaCoverBackground = settings.FoliaCoverBackground;
            AppSettings.FoliaBackground = settings.FoliaBackground;
            AppSettings.FoliaImageEffect = settings.FoliaImageEffect;
            AppSettings.FoliaFontScale = settings.FoliaFontScale;
            AppSettings.FoliaFontFamily = settings.FoliaFontFamily;
            AppSettings.FoliaFontWeight = settings.FoliaFontWeight;
            AppSettings.HdrPeakNits = settings.HdrPeakNits;
            AppSettings.HdrWhiteNits = settings.HdrWhiteNits;
            AppSettings.HdrEnabled = settings.HdrEnabled;
            AppSettings.CoverPulseEnabled = settings.CoverPulseEnabled;
            AppSettings.SmtcTextAnimation = Effects.AnimatedTrackText.NormalizeEffect(settings.SmtcTextAnimation);
            AppSettings.WallpaperEnabled = settings.WallpaperEnabled;
            AppSettings.RotationSpeed = settings.RotationSpeed;
            AppSettings.CoverOpacity = settings.CoverOpacity;
            AppSettings.SpectrumOpacity = settings.SpectrumOpacity;
            AppSettings.FontOpacity = settings.FontOpacity;
            AppSettings.FontShadow = settings.FontShadow;
            AppSettings.SmoothingFactor = settings.SmoothingFactor;
            AppSettings.IsDrawPlainSpectrum = settings.IsDrawPlainSpectrum;
            AppSettings.IsDrawRoundSpectrum = settings.IsDrawRoundSpectrum;
            AppSettings.Sensitivity = settings.Sensitivity;
            AppSettings.PowCoe = settings.PowCoe;
            AppSettings.AppStyle = settings.AppStyle ?? "Acrylic";
            AppSettings.CustomAcrylicOpacity = settings.CustomAcrylicOpacity;
            AppSettings.CustomColorAlpha = settings.CustomColorAlpha;
            AppSettings.CustomColorRed = settings.CustomColorRed;
            AppSettings.CustomColorGreen = settings.CustomColorGreen;
            AppSettings.CustomColorBlue = settings.CustomColorBlue;
            AppSettings.IsUpdateBackDrop = settings.IsUpdateBackDrop;
            AppSettings.AppTheme = settings.AppTheme ?? "Default";
            AppSettings.elementTheme = settings.elementTheme == "Light" ? Microsoft.UI.Xaml.ElementTheme.Light : settings.elementTheme == "Dark" ? Microsoft.UI.Xaml.ElementTheme.Dark : Microsoft.UI.Xaml.ElementTheme.Default;
            AppSettings.RefreshRate = settings.RefreshRate;
            AppSettings.ShowDebugOverlay = settings.ShowDebugOverlay;
            AppSettings.SampleRate = settings.SampleRate;
            AppSettings.BarCount = Math.Clamp(settings.BarCount, 128, 512);
            AppSettings.VisualEffect = settings.VisualEffect ?? "aurora-ring";
            AppSettings.SonicTheme = settings.SonicTheme ?? "nocturnal";
            AppSettings.SonicAudioIntensity = settings.SonicAudioIntensity;
            AppSettings.SonicResponseRange = settings.SonicResponseRange;
            AppSettings.GpuQuality = GpuQualitySettings.FromSaved(settings);
            AppSettings.SonicGridSize = settings.SonicGridSize;
            AppSettings.SonicIdleWaveEnabled = settings.SonicIdleWaveEnabled;
            AppSettings.SonicRippleEnabled = settings.SonicRippleEnabled;
            AppSettings.SonicMeteorEnabled = settings.SonicMeteorEnabled;
            AppSettings.SonicAutoRotate = settings.SonicAutoRotate;
            AppSettings.SonicRotateSpeed = settings.SonicRotateSpeed;
            AppSettings.SonicPeakColorEnabled = settings.SonicPeakColorEnabled;
            AppSettings.SonicPeakIntensity = settings.SonicPeakIntensity;
        }

        /// <summary>把 AppSettings 快照成可序列化的 SaveSetting。</summary>
        private static SaveSetting CreateSaveSetting()
        {
            return new SaveSetting
            {
                FoliaTheme = AppSettings.FoliaTheme,
                LyricsCacheEnabled = AppSettings.LyricsCacheEnabled,
                LyricsCacheFolder = AppSettings.LyricsCacheFolder,
                FoliaFumeGeometry = AppSettings.FoliaFumeGeometry,
                FoliaFumeGeometryOpacity = AppSettings.FoliaFumeGeometryOpacity,
                FoliaFumeGlow = AppSettings.FoliaFumeGlow,
                FoliaDioramaSoul = AppSettings.FoliaDioramaSoul,
                FoliaDioramaSoulActive = AppSettings.FoliaDioramaSoulActive,
                FoliaDioramaSoulStrength = AppSettings.FoliaDioramaSoulStrength,
                FoliaDioramaGlow = AppSettings.FoliaDioramaGlow,
                FoliaDioramaGlowStrength = AppSettings.FoliaDioramaGlowStrength,
                FoliaDioramaGradient = AppSettings.FoliaDioramaGradient,
                FoliaDioramaParticleScale = AppSettings.FoliaDioramaParticleScale,
                FoliaDioramaParticleGlow = AppSettings.FoliaDioramaParticleGlow,
                FoliaTemperaTransitions = AppSettings.FoliaTemperaTransitions,
                FoliaTemperaBlocks = AppSettings.FoliaTemperaBlocks,
                FoliaTemperaDecor = AppSettings.FoliaTemperaDecor,
                FoliaTemperaInversion = AppSettings.FoliaTemperaInversion,
                FoliaTemperaPostProcess = AppSettings.FoliaTemperaPostProcess,
                FoliaTemperaGrain = AppSettings.FoliaTemperaGrain,
                FoliaTemperaContrast = AppSettings.FoliaTemperaContrast,
                FoliaTemperaRgbShift = AppSettings.FoliaTemperaRgbShift,
                FoliaTemperaVignette = AppSettings.FoliaTemperaVignette,
                FoliaTemperaLens = AppSettings.FoliaTemperaLens,
                FoliaLumiereLight = AppSettings.FoliaLumiereLight,
                FoliaLumiereAudio = AppSettings.FoliaLumiereAudio,
                FoliaLumiereFog = AppSettings.FoliaLumiereFog,
                FoliaLumiereDarkField = AppSettings.FoliaLumiereDarkField,
                FoliaLumiereBloom = AppSettings.FoliaLumiereBloom,
                FoliaLumiereTextBloom = AppSettings.FoliaLumiereTextBloom,
                FoliaLumiereUnlitOpacity = AppSettings.FoliaLumiereUnlitOpacity,
                FoliaLumiereMotes = AppSettings.FoliaLumiereMotes,
                FoliaLumiereEcho = AppSettings.FoliaLumiereEcho,
                FoliaLumiereDecay = AppSettings.FoliaLumiereDecay,
                FoliaLumiereLineArt = AppSettings.FoliaLumiereLineArt,
                FoliaLumiereFrontBokeh = AppSettings.FoliaLumiereFrontBokeh,
                FoliaLumiereTrails = AppSettings.FoliaLumiereTrails,
                FoliaLumiereFrame = AppSettings.FoliaLumiereFrame,
                FoliaLumiereTextOnly = AppSettings.FoliaLumiereTextOnly,
                FoliaLumiereThemeMix = AppSettings.FoliaLumiereThemeMix,
                FoliaLatentMesh = AppSettings.FoliaLatentMesh,
                FoliaLatentDither = AppSettings.FoliaLatentDither,
                FoliaLatentBeat = AppSettings.FoliaLatentBeat,
                FoliaLatentMeshSpeed = AppSettings.FoliaLatentMeshSpeed,
                FoliaLatentMeshAudioSpeed = AppSettings.FoliaLatentMeshAudioSpeed,
                FoliaLatentDitherSpeed = AppSettings.FoliaLatentDitherSpeed,
                FoliaLatentDitherAudioSpeed = AppSettings.FoliaLatentDitherAudioSpeed,
                FoliaLatentSize = AppSettings.FoliaLatentSize,
                FoliaLatentOpacity = AppSettings.FoliaLatentOpacity,
                FoliaLatentDistortion = AppSettings.FoliaLatentDistortion,
                FoliaLatentSwirl = AppSettings.FoliaLatentSwirl,
                FoliaNomandSize = AppSettings.FoliaNomandSize,
                FoliaNomandSteps = AppSettings.FoliaNomandSteps,
                FoliaNomandOriginal = AppSettings.FoliaNomandOriginal,
                FoliaNomandInverted = AppSettings.FoliaNomandInverted,
                FoliaNomandGlassSize = AppSettings.FoliaNomandGlassSize,
                FoliaNomandGlassDistortion = AppSettings.FoliaNomandGlassDistortion,
                FoliaNomandGlassBlur = AppSettings.FoliaNomandGlassBlur,
                FoliaNomandPaperContrast = AppSettings.FoliaNomandPaperContrast,
                FoliaNomandPaperRoughness = AppSettings.FoliaNomandPaperRoughness,
                FoliaNomandPaperFiber = AppSettings.FoliaNomandPaperFiber,
                FoliaNomandDotSize = AppSettings.FoliaNomandDotSize,
                FoliaNomandDotRadius = AppSettings.FoliaNomandDotRadius,
                FoliaNomandDotContrast = AppSettings.FoliaNomandDotContrast,
                FoliaNomandDotOriginal = AppSettings.FoliaNomandDotOriginal,
                FoliaNomandDotInverted = AppSettings.FoliaNomandDotInverted,
                FoliaNomandLensSpread = AppSettings.FoliaNomandLensSpread,
                FoliaNomandLensBulge = AppSettings.FoliaNomandLensBulge,
                FoliaNomandLensDispersion = AppSettings.FoliaNomandLensDispersion,
                FoliaNomandOverlay = AppSettings.FoliaNomandOverlay,
                FoliaNomandOverlayOpacity = AppSettings.FoliaNomandOverlayOpacity,
                FoliaSonnetBackgroundMg = AppSettings.FoliaSonnetBackgroundMg,
                FoliaSonnetFixedGeometry = AppSettings.FoliaSonnetFixedGeometry,
                FoliaSonnetDecorations = AppSettings.FoliaSonnetDecorations,
                FoliaSonnetGuides = AppSettings.FoliaSonnetGuides,
                FoliaSonnetFrame = AppSettings.FoliaSonnetFrame,
                FoliaSonnetTransitions = AppSettings.FoliaSonnetTransitions,
                FoliaSonnetPostProcess = AppSettings.FoliaSonnetPostProcess,
                FoliaRealtimeMotion = AppSettings.FoliaRealtimeMotion,
                FoliaSonnetMotionStrength = AppSettings.FoliaSonnetMotionStrength,
                FoliaDioramaMotionStrength = AppSettings.FoliaDioramaMotionStrength,
                FoliaPendoloMotionStrength = AppSettings.FoliaPendoloMotionStrength,
                FoliaLyricMode = AppSettings.FoliaLyricMode,
                FoliaCoverBackground = AppSettings.FoliaCoverBackground,
                FoliaBackground = AppSettings.FoliaBackground,
                FoliaImageEffect = AppSettings.FoliaImageEffect,
                FoliaFontScale = AppSettings.FoliaFontScale,
                FoliaFontFamily = AppSettings.FoliaFontFamily,
                FoliaFontWeight = AppSettings.FoliaFontWeight,
                HdrEnabled = AppSettings.HdrEnabled,
                HdrWhiteNits = AppSettings.HdrWhiteNits,
                HdrPeakNits = AppSettings.HdrPeakNits,
                CoverPulseEnabled = AppSettings.CoverPulseEnabled,
                SmtcTextAnimation = AppSettings.SmtcTextAnimation,
                WallpaperEnabled = AppSettings.WallpaperEnabled,
                RotationSpeed = AppSettings.RotationSpeed,
                CoverOpacity = AppSettings.CoverOpacity,
                SpectrumOpacity = AppSettings.SpectrumOpacity,
                FontOpacity = AppSettings.FontOpacity,
                FontShadow = AppSettings.FontShadow,
                SmoothingFactor = AppSettings.SmoothingFactor,
                IsDrawPlainSpectrum = AppSettings.IsDrawPlainSpectrum,
                IsDrawRoundSpectrum = AppSettings.IsDrawRoundSpectrum,
                Sensitivity = AppSettings.Sensitivity,
                PowCoe = AppSettings.PowCoe,
                AppStyle = AppSettings.AppStyle,
                CustomAcrylicOpacity = AppSettings.CustomAcrylicOpacity,
                CustomColorAlpha = AppSettings.CustomColorAlpha,
                CustomColorRed = AppSettings.CustomColorRed,
                CustomColorGreen = AppSettings.CustomColorGreen,
                CustomColorBlue = AppSettings.CustomColorBlue,
                IsUpdateBackDrop = AppSettings.IsUpdateBackDrop,
                AppTheme = AppSettings.AppTheme,
                elementTheme = AppSettings.elementTheme == Microsoft.UI.Xaml.ElementTheme.Light ? "Light" : AppSettings.elementTheme == Microsoft.UI.Xaml.ElementTheme.Dark ? "Dark" : "Default",
                RefreshRate = AppSettings.RefreshRate,
                ShowDebugOverlay = AppSettings.ShowDebugOverlay,
                SampleRate = AppSettings.SampleRate,
                BarCount = AppSettings.BarCount,
                VisualEffect = AppSettings.VisualEffect,
                SonicTheme = AppSettings.SonicTheme,
                SonicAudioIntensity = AppSettings.SonicAudioIntensity,
                SonicResponseRange = AppSettings.SonicResponseRange,
                SonicGridSize = AppSettings.SonicGridSize,
                SonicRenderScalePercent = AppSettings.GpuRenderScalePercent,
                SonicAntiAliasing = AppSettings.GpuAntiAliasing,
                SonicResolutionVersion = 1,
                SonicDlssPreset = AppSettings.GpuDlssPreset,
                SonicIdleWaveEnabled = AppSettings.SonicIdleWaveEnabled,
                SonicRippleEnabled = AppSettings.SonicRippleEnabled,
                SonicMeteorEnabled = AppSettings.SonicMeteorEnabled,
                SonicAutoRotate = AppSettings.SonicAutoRotate,
                SonicRotateSpeed = AppSettings.SonicRotateSpeed,
                SonicPeakColorEnabled = AppSettings.SonicPeakColorEnabled,
                SonicPeakIntensity = AppSettings.SonicPeakIntensity
            };
        }

        public static async Task SaveSettingAsync()
        {
            await SettingManager.SaveSettingsAsync(CreateSaveSetting());
        }

        /// <summary>
        /// 同步落盘当前设置。必须在进程退出路径（Environment.Exit 之前）调用：
        /// 异步写入会被退出直接终止，表现为"设置丢失"。
        /// </summary>
        public static void SaveSettingNow()
        {
            SettingManager.SaveSettingsNow(CreateSaveSetting());
        }
    }
}
