using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WinExSpectrumTest.Model
{
    public partial class AppSettings
    {
        public static event Action<string>? Changed;

        private static bool _lyricsCacheEnabled = true;
        public static bool LyricsCacheEnabled { get => _lyricsCacheEnabled; set => Set(ref _lyricsCacheEnabled, value); }
        private static string _lyricsCacheFolder = "";
        public static string LyricsCacheFolder { get => _lyricsCacheFolder; set => Set(ref _lyricsCacheFolder, value?.Trim() ?? ""); }

        private static string _foliaLyricMode = "classic";
        private static bool _foliaCoverBackground;
        public static bool FoliaCoverBackground { get => _foliaCoverBackground; set => Set(ref _foliaCoverBackground, value); }
        public static string FoliaLyricMode { get => _foliaLyricMode; set => Set(ref _foliaLyricMode, FoliaSettings.NormalizeLyricMode(value)); }
        private static string _foliaBackground = "common";
        public static string FoliaBackground { get => _foliaBackground; set => Set(ref _foliaBackground, FoliaSettings.NormalizeBackground(value)); }
        private static string _foliaImageEffect = "dithering";
        public static string FoliaImageEffect { get => _foliaImageEffect; set => Set(ref _foliaImageEffect, FoliaSettings.NormalizeImageEffect(value)); }
        private static float _foliaFontScale = 1;
        public static float FoliaFontScale { get => _foliaFontScale; set => Set(ref _foliaFontScale, float.IsFinite(value) ? Math.Clamp(value, .5f, 2) : 1); }
        private static string _foliaFontFamily = "Microsoft YaHei";
        public static string FoliaFontFamily { get => _foliaFontFamily; set => Set(ref _foliaFontFamily, string.IsNullOrWhiteSpace(value) ? "Microsoft YaHei" : value.Trim()); }
        private static int _foliaFontWeight = 300;
        public static int FoliaFontWeight { get => _foliaFontWeight; set => Set(ref _foliaFontWeight, Math.Clamp(value, 100, 900)); }

        private static bool _FoliaSonnetBackgroundMg = true;
        /// <summary>Gets or sets whether sonnet: geometry and hud is enabled.</summary>
        public static bool FoliaSonnetBackgroundMg { get => _FoliaSonnetBackgroundMg; set => Set(ref _FoliaSonnetBackgroundMg, value); }
        private static bool _FoliaSonnetFixedGeometry = true;
        /// <summary>Gets or sets whether sonnet: fixed geometry is enabled.</summary>
        public static bool FoliaSonnetFixedGeometry { get => _FoliaSonnetFixedGeometry; set => Set(ref _FoliaSonnetFixedGeometry, value); }
        private static bool _FoliaSonnetDecorations = true;
        /// <summary>Gets or sets whether sonnet: floating decorations is enabled.</summary>
        public static bool FoliaSonnetDecorations { get => _FoliaSonnetDecorations; set => Set(ref _FoliaSonnetDecorations, value); }
        private static bool _FoliaSonnetGuides = true;
        /// <summary>Gets or sets whether sonnet: text guides is enabled.</summary>
        public static bool FoliaSonnetGuides { get => _FoliaSonnetGuides; set => Set(ref _FoliaSonnetGuides, value); }
        private static bool _FoliaSonnetFrame = true;
        /// <summary>Gets or sets whether sonnet: viewport frame is enabled.</summary>
        public static bool FoliaSonnetFrame { get => _FoliaSonnetFrame; set => Set(ref _FoliaSonnetFrame, value); }
        private static bool _FoliaSonnetTransitions = true;
        /// <summary>Gets or sets whether sonnet: shot and paragraph transitions is enabled.</summary>
        public static bool FoliaSonnetTransitions { get => _FoliaSonnetTransitions; set => Set(ref _FoliaSonnetTransitions, value); }
        private static bool _FoliaSonnetPostProcess = false;
        /// <summary>Gets or sets whether sonnet: film and lens post processing is enabled.</summary>
        public static bool FoliaSonnetPostProcess { get => _FoliaSonnetPostProcess; set => Set(ref _FoliaSonnetPostProcess, value); }
        private static bool _FoliaRealtimeMotion = false;
        /// <summary>Gets or sets whether realtime motion is enabled.</summary>
        public static bool FoliaRealtimeMotion { get => _FoliaRealtimeMotion; set => Set(ref _FoliaRealtimeMotion, value); }
        private static float _FoliaSonnetMotionStrength = 1;
        /// <summary>Gets or sets the live response strength, from zero to two.</summary>
        public static float FoliaSonnetMotionStrength { get => _FoliaSonnetMotionStrength; set => Set(ref _FoliaSonnetMotionStrength, float.IsFinite(value) ? Math.Clamp(value, 0, 2) : 1); }
        private static float _FoliaDioramaMotionStrength = 1;
        /// <summary>Gets or sets the live response strength, from zero to two.</summary>
        public static float FoliaDioramaMotionStrength { get => _FoliaDioramaMotionStrength; set => Set(ref _FoliaDioramaMotionStrength, float.IsFinite(value) ? Math.Clamp(value, 0, 2) : 1); }
        private static float _FoliaPendoloMotionStrength = 1;
        /// <summary>Gets or sets the live response strength, from zero to two.</summary>
        public static float FoliaPendoloMotionStrength { get => _FoliaPendoloMotionStrength; set => Set(ref _FoliaPendoloMotionStrength, float.IsFinite(value) ? Math.Clamp(value, 0, 2) : 1); }

        private static void Set<T>(ref T storage, T value, [CallerMemberName] string name = "")
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return;
            storage = value;
            Changed?.Invoke(name);
        }

        private static bool _CoverPulseEnabled = true;
        public static bool CoverPulseEnabled { get => _CoverPulseEnabled; set => Set(ref _CoverPulseEnabled, value); }
        private static string _SmtcTextAnimation = "none";
        public static string SmtcTextAnimation { get => _SmtcTextAnimation; set => Set(ref _SmtcTextAnimation, value); }
        private static bool _IsLocked = false;
        public static bool IsLocked { get => _IsLocked; set => Set(ref _IsLocked, value); }
        private static bool _WallpaperEnabled;
        public static bool WallpaperEnabled { get => _WallpaperEnabled; set => Set(ref _WallpaperEnabled, value); }
        private static bool _isRunningBackend = true;
        public static bool IsRunningBackend { get => _isRunningBackend; set => Set(ref _isRunningBackend, value); }
        private static bool _IsDrawPlainSpectrum = false;
        public static bool IsDrawPlainSpectrum { get => _IsDrawPlainSpectrum; set => Set(ref _IsDrawPlainSpectrum, value); }
        private static bool _IsDrawRoundSpectrum = true;
        public static bool IsDrawRoundSpectrum { get => _IsDrawRoundSpectrum; set => Set(ref _IsDrawRoundSpectrum, value); }
        private static float _RotationSpeed = 10.0f;
        public static float RotationSpeed { get => _RotationSpeed; set => Set(ref _RotationSpeed, value); }
        private static float _CoverOpacity = 1.0f;
        public static float CoverOpacity { get => _CoverOpacity; set => Set(ref _CoverOpacity, value); }
        private static float _SpectrumOpacity = 1.0f;
        public static float SpectrumOpacity { get => _SpectrumOpacity; set => Set(ref _SpectrumOpacity, value); }
        private static float _FontOpacity = 1.0f;
        public static float FontOpacity { get => _FontOpacity; set => Set(ref _FontOpacity, value); }
        /// <summary>文字软阴影强度（0-100，0 = 关闭；阴影色 = 文字色反相）。</summary>
        private static float _FontShadow = 40.0f;
        public static float FontShadow { get => _FontShadow; set => Set(ref _FontShadow, value); }
        private static float _SmoothingFactor = 0.9f;
        public static float SmoothingFactor { get => _SmoothingFactor; set => Set(ref _SmoothingFactor, value); }
        private static float _Sensitivity = 10.0f;
        public static float Sensitivity { get => _Sensitivity; set => Set(ref _Sensitivity, value); }
        private static int _PowCoe = 100;
        public static int PowCoe { get => _PowCoe; set => Set(ref _PowCoe, value); }
        private static string _AppStyle = "Acrylic";
        public static string AppStyle { get => _AppStyle; set => Set(ref _AppStyle, value); }
        private static float _CustomAcrylicOpacity = 0.5f;
        public static float CustomAcrylicOpacity { get => _CustomAcrylicOpacity; set => Set(ref _CustomAcrylicOpacity, value); }
        private static byte _CustomColorAlpha = 255;
        public static byte CustomColorAlpha { get => _CustomColorAlpha; set => Set(ref _CustomColorAlpha, value); }
        private static byte _CustomColorRed = 128;
        public static byte CustomColorRed { get => _CustomColorRed; set => Set(ref _CustomColorRed, value); }
        private static byte _CustomColorGreen = 128;
        public static byte CustomColorGreen { get => _CustomColorGreen; set => Set(ref _CustomColorGreen, value); }
        private static byte _CustomColorBlue = 128;
        public static byte CustomColorBlue { get => _CustomColorBlue; set => Set(ref _CustomColorBlue, value); }
        private static bool _IsUpdateBackDrop = false;
        public static bool IsUpdateBackDrop { get => _IsUpdateBackDrop; set => Set(ref _IsUpdateBackDrop, value); }
        private static string _AppTheme = "Default";
        public static string AppTheme { get => _AppTheme; set => Set(ref _AppTheme, value); }
        private static ElementTheme _elementTheme = ElementTheme.Default;
        public static ElementTheme elementTheme { get => _elementTheme; set => Set(ref _elementTheme, value); }
        private static bool _showDebugOverlay;
        public static bool ShowDebugOverlay { get => _showDebugOverlay; set => Set(ref _showDebugOverlay, value); }
        private static float _RefreshRate = 60.0f;
        public static float RefreshRate { get => _RefreshRate; set => Set(ref _RefreshRate, FrameRateSettings.Normalize(value)); }
        private static int _SampleRate = 12000;
        public static int SampleRate { get => _SampleRate; set => Set(ref _SampleRate, value); }
        private static int _BarCount = 512;
        public static int BarCount { get => _BarCount; set => Set(ref _BarCount, value); }

        /// <summary>Active visualizer effect page id (see EffectRegistry).</summary>
        private static string _VisualEffect = "aurora-ring";
        public static string VisualEffect { get => _VisualEffect; set => Set(ref _VisualEffect, value); }

        private static bool _hdrEnabled;
        public static bool HdrEnabled { get => _hdrEnabled; set => Set(ref _hdrEnabled, value); }
        private static float _hdrWhiteNits = 200f;
        public static float HdrWhiteNits
        {
            get => _hdrWhiteNits;
            set
            {
                float white = float.IsFinite(value) ? Math.Clamp(value, 80f, 1000f) : 200f;
                // Publish a valid pair even to synchronous Changed subscribers.
                bool peakChanged = _hdrPeakNits < white;
                if (peakChanged) _hdrPeakNits = white;
                Set(ref _hdrWhiteNits, white);
                if (peakChanged) Changed?.Invoke(nameof(HdrPeakNits));
            }
        }
        private static float _hdrPeakNits = 1000f;
        public static float HdrPeakNits
        {
            get => _hdrPeakNits;
            set
            {
                float peak = float.IsFinite(value) ? Math.Clamp(value, 80f, 4000f) : 1000f;
                bool whiteChanged = _hdrWhiteNits > peak;
                if (whiteChanged) _hdrWhiteNits = peak;
                Set(ref _hdrPeakNits, peak);
                if (whiteChanged) Changed?.Invoke(nameof(HdrWhiteNits));
            }
        }

        // Shared quality for swap-chain effects; Win2D owns its own render path.
        private static GpuQualitySettings _gpuQuality = GpuQualitySettings.Default;
        /// <summary>Gets or sets an entire quality profile before publishing a single change.</summary>
        public static GpuQualitySettings GpuQuality
        {
            get => _gpuQuality;
            set => Set(ref _gpuQuality, value.Normalize());
        }
        /// <summary>Gets or sets the scene resolution as a percentage of output dimensions.</summary>
        public static int GpuRenderScalePercent { get => _gpuQuality.RenderScalePercent; set => GpuQuality = _gpuQuality with { RenderScalePercent = value }; }
        /// <summary>Gets or sets the stable anti-aliasing mode identifier.</summary>
        public static string GpuAntiAliasing { get => _gpuQuality.AntiAliasing; set => GpuQuality = _gpuQuality with { AntiAliasing = value }; }
        /// <summary>Gets or sets the explicit DLSS model preset independently of resolution.</summary>
        public static string GpuDlssPreset { get => _gpuQuality.DlssPreset; set => GpuQuality = _gpuQuality with { DlssPreset = value }; }

        // Sonic Topography effect settings.
        private static string _SonicTheme = "nocturnal";
        public static string SonicTheme { get => _SonicTheme; set => Set(ref _SonicTheme, value); }
        private static float _SonicAudioIntensity = 1.0f;
        public static float SonicAudioIntensity { get => _SonicAudioIntensity; set => Set(ref _SonicAudioIntensity, value); }
        private static float _SonicResponseRange = 1.0f;
        public static float SonicResponseRange { get => _SonicResponseRange; set => Set(ref _SonicResponseRange, value); }
        private static int _sonicGridSize = 160;
        /// <summary>Gets or sets the number of terrain cells on each axis.</summary>
        public static int SonicGridSize { get => _sonicGridSize; set => Set(ref _sonicGridSize, Math.Clamp(value, 80, 320)); }
        private static bool _SonicIdleWaveEnabled = true;
        public static bool SonicIdleWaveEnabled { get => _SonicIdleWaveEnabled; set => Set(ref _SonicIdleWaveEnabled, value); }
        private static bool _SonicRippleEnabled = true;
        public static bool SonicRippleEnabled { get => _SonicRippleEnabled; set => Set(ref _SonicRippleEnabled, value); }
        private static bool _SonicMeteorEnabled = true;
        public static bool SonicMeteorEnabled { get => _SonicMeteorEnabled; set => Set(ref _SonicMeteorEnabled, value); }
        private static bool _SonicAutoRotate = false;
        public static bool SonicAutoRotate { get => _SonicAutoRotate; set => Set(ref _SonicAutoRotate, value); }
        private static float _SonicRotateSpeed = 10.0f;
        public static float SonicRotateSpeed { get => _SonicRotateSpeed; set => Set(ref _SonicRotateSpeed, value); }
        private static bool _SonicPeakColorEnabled = true;
        public static bool SonicPeakColorEnabled { get => _SonicPeakColorEnabled; set => Set(ref _SonicPeakColorEnabled, value); }
        private static float _SonicPeakIntensity = 1.0f;
        public static float SonicPeakIntensity { get => _SonicPeakIntensity; set => Set(ref _SonicPeakIntensity, value); }
    }
}
