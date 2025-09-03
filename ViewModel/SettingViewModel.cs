using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.ViewModel
{
    public partial class SettingViewModel: ObservableObject
    {
        private bool _isInitialized = false;
        [ObservableProperty]
        private float _rotationSpeed = 10.0f;
        partial void OnRotationSpeedChanged(float value)
        {
            AppSettings.RotationSpeed = value;
            if (_isInitialized) {
                _ = DataService.SaveSettingAsync();
            }
        }
        [ObservableProperty]
        private float _coverOpacity = 100.0f;
        partial void OnCoverOpacityChanged(float value)
        {
            AppSettings.CoverOpacity = value/100;
            if (_isInitialized)
            {
                _ = DataService.SaveSettingAsync();
            }
        }
        [ObservableProperty]
        private float _spectrumOpacity = 100.0f;
        partial void OnSpectrumOpacityChanged(float value) { 
            AppSettings.SpectrumOpacity = value / 100;
            if (_isInitialized)
            {
                _ = DataService.SaveSettingAsync();
            }
        }
        [ObservableProperty]
        private float _fontOpacity = 100.0f;
        partial void OnFontOpacityChanged(float value) { 
            AppSettings.FontOpacity = value / 100;
            if (_isInitialized)
            {
                _ = DataService.SaveSettingAsync();
            }
        }
        [ObservableProperty]
        private float _smoothingFactor = 95f;
        partial void OnSmoothingFactorChanged(float value) { 
            AppSettings.SmoothingFactor = value/100;
            if (_isInitialized)
            {
                _ = DataService.SaveSettingAsync();
            }
        }
        [ObservableProperty]
        private bool _isDrawPlainSpectrum = false;
        partial void OnIsDrawPlainSpectrumChanged(bool value) { 
            AppSettings.IsDrawPlainSpectrum = value;
            if (_isInitialized)
            {
                _ = DataService.SaveSettingAsync();
            }
        }
        [ObservableProperty]
        private bool _isDrawRoundSpectrum = true;
        partial void OnIsDrawRoundSpectrumChanged(bool value) { 
            AppSettings.IsDrawRoundSpectrum = value;
            if (_isInitialized)
            {
                _ = DataService.SaveSettingAsync();
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
            _isInitialized = true;
        }
    }
}
