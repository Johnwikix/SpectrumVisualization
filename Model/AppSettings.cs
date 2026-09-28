using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace WinExSpectrumTest.Model
{
    public class AppSettings
    {
        public static event Action<string>? Changed;

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
        private static float _RefreshRate = 60.0f;
        public static float RefreshRate { get => _RefreshRate; set => Set(ref _RefreshRate, value); }
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
                float white = float.IsFinite(value) ? Math.Clamp(value, 80f, 500f) : 200f;
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

        // Sonic Topography effect settings.
        private static string _SonicTheme = "nocturnal";
        public static string SonicTheme { get => _SonicTheme; set => Set(ref _SonicTheme, value); }
        private static float _SonicAudioIntensity = 1.0f;
        public static float SonicAudioIntensity { get => _SonicAudioIntensity; set => Set(ref _SonicAudioIntensity, value); }
        private static float _SonicResponseRange = 1.0f;
        public static float SonicResponseRange { get => _SonicResponseRange; set => Set(ref _SonicResponseRange, value); }
        private static SonicQualitySettings _sonicQuality = SonicQualitySettings.Default;
        /// <summary>Gets or sets an entire quality profile before publishing a single change.</summary>
        public static SonicQualitySettings SonicQuality
        {
            get => _sonicQuality;
            set => Set(ref _sonicQuality, value.Normalize());
        }
        /// <summary>Gets or sets the number of terrain cells on each axis.</summary>
        public static int SonicGridSize { get => _sonicQuality.GridSize; set => SonicQuality = _sonicQuality with { GridSize = value }; }
        /// <summary>Gets or sets the scene resolution as a percentage of output dimensions.</summary>
        public static int SonicRenderScalePercent { get => _sonicQuality.RenderScalePercent; set => SonicQuality = _sonicQuality with { RenderScalePercent = value }; }
        /// <summary>Gets or sets the stable anti-aliasing mode identifier.</summary>
        public static string SonicAntiAliasing { get => _sonicQuality.AntiAliasing; set => SonicQuality = _sonicQuality with { AntiAliasing = value }; }
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
