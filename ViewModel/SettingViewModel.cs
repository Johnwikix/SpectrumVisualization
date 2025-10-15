using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Service;
//using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.ViewModel
{
    public partial class SettingViewModel : ObservableObject
    {
        private bool _isInitialized = false;
        
        private float _rotationSpeed = 10.0f;
        public float RotationSpeed
        {
            get => _rotationSpeed;
            set
            {
                if (SetProperty(ref _rotationSpeed, value)) {
                    if (_isInitialized)
                    {
                        AppSettings.RotationSpeed = value;
                    }
                }
            }
        }
        private float _coverOpacity = 100.0f;
        public float CoverOpacity
        {
            get => _coverOpacity;
            set
            {
                if (SetProperty(ref _coverOpacity, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.CoverOpacity = value / 100;
                    }
                }
            }
        }
        private float _spectrumOpacity = 100.0f;
        public float SpectrumOpacity
        {
            get => _spectrumOpacity;
            set
            {
                if (SetProperty(ref _spectrumOpacity, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SpectrumOpacity = value / 100;
                    }
                }
            }
        }
        private float _fontOpacity = 100.0f;
        public float FontOpacity
        {
            get => _fontOpacity;
            set
            {
                if (SetProperty(ref _fontOpacity, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.FontOpacity = value / 100;
                    }
                }
            }
        }
        private float _smoothingFactor = 95f;
        public float SmoothingFactor
        {
            get => _smoothingFactor;
            set
            {
                if (SetProperty(ref _smoothingFactor, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SmoothingFactor = value / 100;
                    }
                }
            }
        }
        private bool _isDrawPlainSpectrum = false;
        public bool IsDrawPlainSpectrum
        {
            get => _isDrawPlainSpectrum;
            set
            {
                if (SetProperty(ref _isDrawPlainSpectrum, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.IsDrawPlainSpectrum = value;
                    }
                }
            }
        }
        private bool _isDrawRoundSpectrum = true;
        public bool IsDrawRoundSpectrum
        {
            get => _isDrawRoundSpectrum;
            set
            {
                if (SetProperty(ref _isDrawRoundSpectrum, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.IsDrawRoundSpectrum = value;
                    }
                }
            }
        }

        private float _sensitivity = 10.0f;
        public float Sensitivity
        {
            get => _sensitivity;
            set
            {
                if (SetProperty(ref _sensitivity, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.Sensitivity = value;
                    }
                }
            }
        }
        private string _backdropType = "TransparentAcrylic";
        public string BackdropType
        {
            get => _backdropType;
            set
            {
                if (SetProperty(ref _backdropType, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.AppStyle = value;
                    }
                }
            }
        }
        private bool _isColorPickerVisible = false;
        public bool IsColorPickerVisible
        {
            get => _isColorPickerVisible;
            set => SetProperty(ref _isColorPickerVisible, value);
        }
        private Color _customColor = Color.FromArgb(255, 128, 128, 128);
        public Color CustomColor
        {
            get => _customColor;
            set
            {
                if (SetProperty(ref _customColor, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.CustomColorAlpha = value.A;
                        AppSettings.CustomColorRed = value.R;
                        AppSettings.CustomColorGreen = value.G;
                        AppSettings.CustomColorBlue = value.B;
                    }
                }
            }
        }
        private float _customOpacity = 50f;
        public float CustomOpacity
        {
            get => _customOpacity;
            set
            {
                if (SetProperty(ref _customOpacity, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.CustomAcrylicOpacity = value / 100;
                        App.MainWindow?.SetCustomAppStyle();
                    }
                }
            }
        }
        private bool _isUpdateBackDrop = false;
        public bool IsUpdateBackDrop
        {
            get => _isUpdateBackDrop;
            set
            {
                if (SetProperty(ref _isUpdateBackDrop, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.IsUpdateBackDrop = value;
                    }
                }
            }
        }
        private float _refreshRate = 60.0f;
        public float RefreshRate
        {
            get => _refreshRate;
            set
            {
                if (SetProperty(ref _refreshRate, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.RefreshRate = value;
                        App.MainWindow?.ChangeRefreshRate();
                    }
                }
            }
        }
        private string _appVersion;
        public string AppVersion
        {
            get => _appVersion;
            set => SetProperty(ref _appVersion, value);
        }

        private int _sampleRate = 12000;
        public int SampleRate
        {
            get => _sampleRate;
            set
            {
                if (SetProperty(ref _sampleRate, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SampleRate = value;
                    }
                }
            }
        }
        private int _barCount = 128;
        public int BarCount
        {
            get => _barCount;
            set
            {
                if (SetProperty(ref _barCount, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.BarCount = value;
                    }
                }
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
            BackdropType = AppSettings.AppStyle;
            if (BackdropType != "CustomAcrylicStyle")
            {
                IsColorPickerVisible = false;
            }
            else
            {
                IsColorPickerVisible = true;
            }
            CustomOpacity = AppSettings.CustomAcrylicOpacity * 100;
            CustomColor = Color.FromArgb(AppSettings.CustomColorAlpha,
                                                 AppSettings.CustomColorRed,
                                                 AppSettings.CustomColorGreen,
                                                 AppSettings.CustomColorBlue);
            IsUpdateBackDrop = AppSettings.IsUpdateBackDrop;
            RefreshRate = AppSettings.RefreshRate;
            SampleRate = AppSettings.SampleRate;
            BarCount = AppSettings.BarCount;
            AppVersion = $"{Windows.ApplicationModel.Package.Current.Id.Version.Major}.{Windows.ApplicationModel.Package.Current.Id.Version.Minor}.{Windows.ApplicationModel.Package.Current.Id.Version.Build}.{Windows.ApplicationModel.Package.Current.Id.Version.Revision}";
            _isInitialized = true;
        }

        public async Task SaveSettings()
        {
            await DataJsonService.SaveSettingAsync();
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
