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
            var settings = await SettingManager.LoadSettingsAsync();
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
            AppSettings.SampleRate = settings.SampleRate;
            AppSettings.BarCount = Math.Clamp(settings.BarCount, 128, 512);
            AppSettings.VisualEffect = settings.VisualEffect ?? "aurora-ring";
            AppSettings.SonicTheme = settings.SonicTheme ?? "nocturnal";
            AppSettings.SonicAudioIntensity = settings.SonicAudioIntensity;
            AppSettings.SonicResponseRange = settings.SonicResponseRange;
            AppSettings.SonicQuality = new SonicQualitySettings(settings.SonicRenderScalePercent, settings.SonicGridSize, settings.SonicAntiAliasing);
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
                SampleRate = AppSettings.SampleRate,
                BarCount = AppSettings.BarCount,
                VisualEffect = AppSettings.VisualEffect,
                SonicTheme = AppSettings.SonicTheme,
                SonicAudioIntensity = AppSettings.SonicAudioIntensity,
                SonicResponseRange = AppSettings.SonicResponseRange,
                SonicGridSize = AppSettings.SonicGridSize,
                SonicRenderScalePercent = AppSettings.SonicRenderScalePercent,
                SonicAntiAliasing = AppSettings.SonicAntiAliasing,
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
