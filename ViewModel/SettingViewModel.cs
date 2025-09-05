using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.ViewModel
{
    public partial class SettingViewModel : ObservableObject
    {
        private bool _isInitialized = false;
        [ObservableProperty]
        private float _rotationSpeed = 10.0f;
        partial void OnRotationSpeedChanged(float value)
        {            
            if (_isInitialized)
            {
                AppSettings.RotationSpeed = value;
            }
        }
        [ObservableProperty]
        private float _coverOpacity = 100.0f;
        partial void OnCoverOpacityChanged(float value)
        {
            AppSettings.CoverOpacity = value / 100;
        }
        [ObservableProperty]
        private float _spectrumOpacity = 100.0f;
        partial void OnSpectrumOpacityChanged(float value)
        {
            AppSettings.SpectrumOpacity = value / 100;
        }
        [ObservableProperty]
        private float _fontOpacity = 100.0f;
        partial void OnFontOpacityChanged(float value)
        {
            AppSettings.FontOpacity = value / 100;
        }
        [ObservableProperty]
        private float _smoothingFactor = 95f;
        partial void OnSmoothingFactorChanged(float value)
        {
            AppSettings.SmoothingFactor = value / 100;
        }
        [ObservableProperty]
        private bool _isDrawPlainSpectrum = false;
        partial void OnIsDrawPlainSpectrumChanged(bool value)
        {
            AppSettings.IsDrawPlainSpectrum = value;
        }
        [ObservableProperty]
        private bool _isDrawRoundSpectrum = true;
        partial void OnIsDrawRoundSpectrumChanged(bool value)
        {
            AppSettings.IsDrawRoundSpectrum = value;

        }
        [ObservableProperty]
        private float _sensitivity = 10.0f;
        partial void OnSensitivityChanged(float value)
        {
            AppSettings.Sensitivity = value;
        }
        [ObservableProperty]
        private string _backdropType = "TransparentAcrylic";
        partial void OnBackdropTypeChanged(string value)
        {
            if (_isInitialized)
            {
                AppSettings.AppStyle = value;
            }            
        }
        [ObservableProperty]
        private bool _isColorPickerVisible = false;
        [ObservableProperty]
        private Color _customColor = Color.FromArgb(255, 128, 128, 128);
        partial void OnCustomColorChanged(Color value) {
            if (_isInitialized)
            {
                AppSettings.CustomColorAlpha = value.A;
                AppSettings.CustomColorRed = value.R;
                AppSettings.CustomColorGreen = value.G;
                AppSettings.CustomColorBlue = value.B;
            }
        }
        [ObservableProperty]
        private float _customOpacity = 50f;
        partial void OnCustomOpacityChanged(float value) {
            if (_isInitialized)
            {
                AppSettings.CustomAcrylicOpacity = value / 100;
                App.MainWindow?.SetCustomAppStyle();
            }
        }
        [ObservableProperty]
        private bool _isUpdateBackDrop = false;
        partial void OnIsUpdateBackDropChanged(bool value) {
            if (_isInitialized)
            {
                AppSettings.IsUpdateBackDrop = value;
            }
        }
        public SettingViewModel()
        {
            _isInitialized = false;
            RotationSpeed = AppSettings.RotationSpeed;
            CoverOpacity = AppSettings.CoverOpacity * 100;
            SpectrumOpacity = AppSettings.SpectrumOpacity * 100;
            FontOpacity = AppSettings.FontOpacity * 100;
            SmoothingFactor = AppSettings.SmoothingFactor * 100;
            IsDrawPlainSpectrum = AppSettings.IsDrawPlainSpectrum;
            IsDrawRoundSpectrum = AppSettings.IsDrawRoundSpectrum;
            Sensitivity = AppSettings.Sensitivity;
            _isInitialized = true;
        }

        public void SaveSettings()
        {
            _ = DataService.SaveSettingAsync();
        }


        [RelayCommand]
        private void ChangeBackdropType(string type)
        {
            try
            {
                switch (type)
                {
                    case "Acrylic":
                        AppSettings.AppStyle = "Acrylic";
                        IsColorPickerVisible = false;
                        break;
                    case "TransparentAcrylic":
                        AppSettings.AppStyle = "TransparentAcrylic";
                        IsColorPickerVisible = false;
                        break;
                    case "Mica":
                        AppSettings.AppStyle = "Mica";
                        IsColorPickerVisible = false;
                        break;
                    case "TransparentTint":
                        AppSettings.AppStyle = "TransparentTint";
                        IsColorPickerVisible = false;
                        break;
                    case "CustomAcrylicStyle":
                        AppSettings.AppStyle = "CustomAcrylicStyle";
                        IsColorPickerVisible = true;
                        break;
                }
                App.MainWindow?.SetAppStyle();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting backdrop type: {ex.Message}");
            }
        }
    }
}
