using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            AppSettings.RotationSpeed = settings.RotationSpeed;
            AppSettings.CoverOpacity = settings.CoverOpacity;
            AppSettings.SpectrumOpacity = settings.SpectrumOpacity;
            AppSettings.FontOpacity = settings.FontOpacity;
            AppSettings.SmoothingFactor = settings.SmoothingFactor;
            AppSettings.IsDrawPlainSpectrum = settings.IsDrawPlainSpectrum;
            AppSettings.IsDrawRoundSpectrum = settings.IsDrawRoundSpectrum;
            AppSettings.Sensitivity = settings.Sensitivity;
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
        }

        public static async Task SaveSettingAsync()
        {
            var settings = new SaveSetting
            {
                RotationSpeed = AppSettings.RotationSpeed,
                CoverOpacity = AppSettings.CoverOpacity,
                SpectrumOpacity = AppSettings.SpectrumOpacity,
                FontOpacity = AppSettings.FontOpacity,
                SmoothingFactor = AppSettings.SmoothingFactor,
                IsDrawPlainSpectrum = AppSettings.IsDrawPlainSpectrum,
                IsDrawRoundSpectrum = AppSettings.IsDrawRoundSpectrum,
                Sensitivity = AppSettings.Sensitivity,
                AppStyle = AppSettings.AppStyle,
                CustomAcrylicOpacity = AppSettings.CustomAcrylicOpacity,
                CustomColorAlpha = AppSettings.CustomColorAlpha,
                CustomColorRed = AppSettings.CustomColorRed,
                CustomColorGreen = AppSettings.CustomColorGreen,
                CustomColorBlue = AppSettings.CustomColorBlue,
                IsUpdateBackDrop = AppSettings.IsUpdateBackDrop,
                AppTheme = AppSettings.AppTheme,
                elementTheme = AppSettings.elementTheme == Microsoft.UI.Xaml.ElementTheme.Light ? "Light" : AppSettings.elementTheme == Microsoft.UI.Xaml.ElementTheme.Dark ? "Dark" : "Default",
                RefreshRate = AppSettings.RefreshRate
            };
            await SettingManager.SaveSettingsAsync(settings);
        }
    }

}
