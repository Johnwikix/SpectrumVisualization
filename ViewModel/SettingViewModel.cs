using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Windows.UI;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Service;
//using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.ViewModel
{
    public partial class SettingViewModel : ObservableObject
    {
        public string CopyrightNotice => $"© {DateTime.Now.Year} Sennpei Studio";

        private bool _isInitialized = false;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _hdrSaveTimer;

        public bool HdrEnabled
        {
            get => AppSettings.HdrEnabled;
            set => AppSettings.HdrEnabled = value;
        }
        public double HdrWhiteNits
        {
            get => AppSettings.HdrWhiteNits;
            set => AppSettings.HdrWhiteNits = (float)value;
        }
        public double HdrPeakNits
        {
            get => AppSettings.HdrPeakNits;
            set => AppSettings.HdrPeakNits = (float)value;
        }
        public string HdrWhiteLabel => $"{AppSettings.HdrWhiteNits:0} nit";
        public string HdrPeakLabel => $"{AppSettings.HdrPeakNits:0} nit";
        public string HdrStatusText => new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString(
            WinExSpectrumTest.Rendering.HdrStatus.Mode switch
            {
                WinExSpectrumTest.Rendering.HdrOutputMode.Unsupported => "HdrStatusUnsupported",
                WinExSpectrumTest.Rendering.HdrOutputMode.Starting => "HdrStatusStarting",
                WinExSpectrumTest.Rendering.HdrOutputMode.Active => "HdrStatusActive",
                WinExSpectrumTest.Rendering.HdrOutputMode.Unavailable => "HdrStatusUnavailable",
                WinExSpectrumTest.Rendering.HdrOutputMode.Failed => "HdrStatusFailed",
                _ => "HdrStatusDisabled"
            });
        private void OnHdrStatusChanged() => OnPropertyChanged(nameof(HdrStatusText));

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

        private float _fontShadow = 40.0f;
        public float FontShadow
        {
            get => _fontShadow;
            set
            {
                if (SetProperty(ref _fontShadow, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.FontShadow = value;
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
        public int PowCoe
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.PowCoe = value;
                    }
                }
            }
        } = 100;
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
                        App.MainWindow?.SetCustomAppStyle();
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
        public bool ShowDebugOverlay
        {
            get => AppSettings.ShowDebugOverlay;
            set { if (_isInitialized) AppSettings.ShowDebugOverlay = value; }
        }
        private float _refreshRate = 60.0f;
        private float[] _refreshRates = [];
        public string[] RefreshRateChoices { get; private set; } = [];
        public int RefreshRateIndex
        {
            get => Array.IndexOf(_refreshRates, _refreshRate);
            set
            {
                if (_isInitialized && (uint)value < (uint)_refreshRates.Length)
                    RefreshRate = _refreshRates[value];
            }
        }

        private void EnsureRefreshRateChoice(float rate)
        {
            if (Array.IndexOf(_refreshRates, rate) >= 0) return;
            var presets = FrameRateSettings.Presets;
            bool custom = rate != MathF.Truncate(rate) || !presets.Contains((int)rate);
            _refreshRates = new float[presets.Length + (custom ? 1 : 0)];
            var labels = new string[_refreshRates.Length];
            var resources = new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader();
            for (int i = 0; i < presets.Length; i++)
            {
                _refreshRates[i] = presets[i];
                labels[i] = presets[i] == 0 ? resources.GetString("RefreshRateUnlimited") : $"{presets[i]} Hz";
            }
            if (custom)
            {
                _refreshRates[^1] = rate;
                labels[^1] = $"{rate:0.##} Hz";
            }
            RefreshRateChoices = labels;
            OnPropertyChanged(nameof(RefreshRateChoices));
        }

        public float RefreshRate
        {
            get => _refreshRate;
            set
            {
                value = FrameRateSettings.Normalize(value);
                EnsureRefreshRateChoice(value);
                if (SetProperty(ref _refreshRate, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.RefreshRate = value;
                    }
                }
                OnPropertyChanged(nameof(RefreshRateIndex));
            }
        }
        private string _appVersion;
        public string AppVersion
        {
            get => _appVersion;
            set => SetProperty(ref _appVersion, value);
        }

        private readonly WinExSpectrumTest.Audio.SpectrumAnalyzer _analyzer;
        public int SampleRate => _analyzer.SampleRate;
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

        private string _visualEffect = "aurora-ring";
        public string VisualEffect
        {
            get => _visualEffect;
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (SetProperty(ref _visualEffect, value))
                {
                    if (_isInitialized)
                    {
                        App.MainWindow?.SwitchToEffect(value);
                        AppSettings.VisualEffect = value;
                    }
                    // Sonic 参数区仅在 Sonic 页激活时展示，避免误导
                    OnPropertyChanged(nameof(SonicSectionVisibility));
                }
            }
        }

        /// <summary>Sonic Topography 参数区可见性（仅当前效果为 Sonic 时可见）。</summary>
        public Microsoft.UI.Xaml.Visibility SonicSectionVisibility
            => string.Equals(_visualEffect, "sonic-topography", StringComparison.Ordinal)
                ? Microsoft.UI.Xaml.Visibility.Visible
                : Microsoft.UI.Xaml.Visibility.Collapsed;

        private string _sonicTheme = "nocturnal";
        public string SonicTheme
        {
            get => _sonicTheme;
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (SetProperty(ref _sonicTheme, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicTheme = value;
                    }
                }
            }
        }

        private float _sonicAudioIntensity = 1.0f;
        public float SonicAudioIntensity
        {
            get => _sonicAudioIntensity;
            set
            {
                if (SetProperty(ref _sonicAudioIntensity, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicAudioIntensity = value / 100f;
                    }
                }
            }
        }

        private float _sonicResponseRange = 1.0f;
        public float SonicResponseRange
        {
            get => _sonicResponseRange;
            set
            {
                if (SetProperty(ref _sonicResponseRange, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicResponseRange = value / 100f;
                    }
                }
            }
        }

        /// <summary>Gets or sets the stable anti-aliasing mode.</summary>
        public string SonicAntiAliasing
        {
            get => AppSettings.SonicAntiAliasing;
            set
            {
                if (_isInitialized && value is "off" or "fxaa" or "smaa" or "xess" or "fsr" or "dlss") AppSettings.SonicAntiAliasing = value;
            }
        }

        /// <summary>Gets or sets the explicitly requested DLSS model.</summary>
        public int SonicDlssPresetIndex
        {
            get => AppSettings.SonicDlssPreset switch { "j" => 0, "l" => 2, "m" => 3, _ => 1 };
            set
            {
                if (_isInitialized && value is >= 0 and <= 3)
                    AppSettings.SonicDlssPreset = value switch { 0 => "j", 2 => "l", 3 => "m", _ => "k" };
            }
        }

        public bool SonicDlssSelected => AppSettings.SonicAntiAliasing == "dlss";
        public bool SonicXeSSAvailable => Rendering.ReconstructionAvailability.Supports(Rendering.ReconstructionMode.XeSS);
        public bool SonicFsrAvailable => Rendering.ReconstructionAvailability.Supports(Rendering.ReconstructionMode.Fsr);
        public bool SonicDlssAvailable => Rendering.ReconstructionAvailability.Supports(Rendering.ReconstructionMode.Dlss);
        public string SonicReconstructionStatus
        {
            get
            {
                var status = Rendering.ReconstructionAvailability.Status;
                string key = !Rendering.ReconstructionAvailability.Known ? "SonicReconstructionPending" :
                    status.UnsupportedScale ? "SonicReconstructionScaleUnsupported" :
                    status.Requested != status.Active ? "SonicReconstructionFallback" : "SonicReconstructionReady";
                return new Microsoft.Windows.ApplicationModel.Resources.ResourceLoader().GetString(key);
            }
        }

        private void OnReconstructionChanged()
        {
            OnPropertyChanged(nameof(SonicXeSSAvailable));
            OnPropertyChanged(nameof(SonicFsrAvailable));
            OnPropertyChanged(nameof(SonicDlssAvailable));
            OnPropertyChanged(nameof(SonicReconstructionStatus));
        }

        /// <summary>Gets or sets the internal resolution percentage selected in the settings view.</summary>
        public double SonicRenderScale
        {
            get => AppSettings.SonicRenderScalePercent;
            set
            {
                if (_isInitialized && double.IsFinite(value)) AppSettings.SonicRenderScalePercent = (int)Math.Clamp(Math.Round(value), 1, 100);
            }
        }
        public string SonicRenderScaleLabel => $"{AppSettings.SonicRenderScalePercent}%";

        /// <summary>Gets or sets the number of terrain cells per axis.</summary>
        public string SonicGridSize
        {
            get => AppSettings.SonicGridSize.ToString();
            set
            {
                if (_isInitialized && int.TryParse(value, out int gridSize)) AppSettings.SonicGridSize = gridSize;
            }
        }

        private bool _sonicIdleWaveEnabled = true;
        public bool SonicIdleWaveEnabled
        {
            get => _sonicIdleWaveEnabled;
            set
            {
                if (SetProperty(ref _sonicIdleWaveEnabled, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicIdleWaveEnabled = value;
                    }
                }
            }
        }

        private bool _sonicRippleEnabled = true;
        public bool SonicRippleEnabled
        {
            get => _sonicRippleEnabled;
            set
            {
                if (SetProperty(ref _sonicRippleEnabled, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicRippleEnabled = value;
                    }
                }
            }
        }

        private bool _sonicMeteorEnabled = true;
        public bool SonicMeteorEnabled
        {
            get => _sonicMeteorEnabled;
            set
            {
                if (SetProperty(ref _sonicMeteorEnabled, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicMeteorEnabled = value;
                    }
                }
            }
        }

        private bool _sonicAutoRotate = false;
        public bool SonicAutoRotate
        {
            get => _sonicAutoRotate;
            set
            {
                if (SetProperty(ref _sonicAutoRotate, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicAutoRotate = value;
                    }
                }
            }
        }

        private float _sonicRotateSpeed = 10.0f;
        public float SonicRotateSpeed
        {
            get => _sonicRotateSpeed;
            set
            {
                if (SetProperty(ref _sonicRotateSpeed, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicRotateSpeed = value;
                    }
                }
            }
        }

        private bool _sonicPeakColorEnabled = true;
        public bool SonicPeakColorEnabled
        {
            get => _sonicPeakColorEnabled;
            set
            {
                if (SetProperty(ref _sonicPeakColorEnabled, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicPeakColorEnabled = value;
                    }
                }
            }
        }

        private float _sonicPeakIntensity = 1.0f;
        public float SonicPeakIntensity
        {
            get => _sonicPeakIntensity;
            set
            {
                if (SetProperty(ref _sonicPeakIntensity, value))
                {
                    if (_isInitialized)
                    {
                        AppSettings.SonicPeakIntensity = value / 100f;
                    }
                }
            }
        }

        public SettingViewModel(WinExSpectrumTest.Audio.SpectrumAnalyzer analyzer)
        {
            _hdrSaveTimer = _dispatcher.CreateTimer();
            _hdrSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _hdrSaveTimer.IsRepeating = false;
            _hdrSaveTimer.Tick += async (_, _) => await DataJsonService.SaveSettingAsync();
            _analyzer = analyzer;
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _analyzer.SampleRateChanged += () => dispatcher.TryEnqueue(() => OnPropertyChanged(nameof(SampleRate)));
            ReloadSettings();
            AppSettings.Changed += OnSettingsChanged;
            WinExSpectrumTest.Rendering.HdrStatus.Changed += OnHdrStatusChanged;
            Rendering.ReconstructionAvailability.Changed += OnReconstructionChanged;
            try
            {
                AppVersion = $"{Windows.ApplicationModel.Package.Current.Id.Version.Major}.{Windows.ApplicationModel.Package.Current.Id.Version.Minor}.{Windows.ApplicationModel.Package.Current.Id.Version.Build}.{Windows.ApplicationModel.Package.Current.Id.Version.Revision}";
            }
            catch
            {
                string assemblyLocation = Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(assemblyLocation))
                {
                    assemblyLocation = Environment.ProcessPath;
                }
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assemblyLocation);
                AppVersion = $"{fvi.FileMajorPart}.{fvi.FileMinorPart}.{fvi.FileBuildPart}.{fvi.FilePrivatePart}";
            }
            finally {
                _isInitialized = true;
            }
        }

        private void OnSettingsChanged(string name)
        {
            if (!_dispatcher.HasThreadAccess)
            {
                _dispatcher.TryEnqueue(() => OnSettingsChanged(name));
                return;
            }
            if (name is nameof(AppSettings.HdrEnabled) or nameof(AppSettings.HdrWhiteNits) or nameof(AppSettings.HdrPeakNits))
            {
                OnPropertyChanged(name);
                OnPropertyChanged(nameof(HdrWhiteLabel));
                OnPropertyChanged(nameof(HdrPeakLabel));
                _hdrSaveTimer.Stop();
                _hdrSaveTimer.Start();
                return;
            }
            if (name == nameof(AppSettings.ShowDebugOverlay))
            {
                OnPropertyChanged(nameof(ShowDebugOverlay));
                _hdrSaveTimer.Stop();
                _hdrSaveTimer.Start();
                return;
            }
            if (name == nameof(AppSettings.SonicQuality))
            {
                NotifySonicQuality();
                _hdrSaveTimer.Stop();
                _hdrSaveTimer.Start();
                return;
            }
            ReloadSettings();
        }

        private void NotifySonicQuality()
        {
            bool initialized = _isInitialized;
            _isInitialized = false;
            OnPropertyChanged(nameof(SonicAntiAliasing));
            OnPropertyChanged(nameof(SonicRenderScale));
            OnPropertyChanged(nameof(SonicRenderScaleLabel));
            OnPropertyChanged(nameof(SonicGridSize));
            OnPropertyChanged(nameof(SonicDlssPresetIndex));
            OnPropertyChanged(nameof(SonicDlssSelected));
            _isInitialized = initialized;
        }

        private void ReloadSettings()
        {
            bool initialized = _isInitialized;
            _isInitialized = false;
            RotationSpeed = AppSettings.RotationSpeed;
            CoverOpacity = AppSettings.CoverOpacity * 100;
            SpectrumOpacity = AppSettings.SpectrumOpacity * 100;
            FontOpacity = AppSettings.FontOpacity * 100;
            FontShadow = AppSettings.FontShadow;
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
            OnPropertyChanged(nameof(ShowDebugOverlay));
            OnPropertyChanged(nameof(SampleRate));
            BarCount = Math.Clamp(AppSettings.BarCount, 128, 512);
            PowCoe = AppSettings.PowCoe;
            VisualEffect = AppSettings.VisualEffect;
            SonicTheme = AppSettings.SonicTheme;
            SonicAudioIntensity = AppSettings.SonicAudioIntensity * 100f;
            SonicResponseRange = AppSettings.SonicResponseRange * 100f;
            NotifySonicQuality();
            SonicIdleWaveEnabled = AppSettings.SonicIdleWaveEnabled;
            SonicRippleEnabled = AppSettings.SonicRippleEnabled;
            SonicMeteorEnabled = AppSettings.SonicMeteorEnabled;
            SonicAutoRotate = AppSettings.SonicAutoRotate;
            SonicRotateSpeed = AppSettings.SonicRotateSpeed;
            SonicPeakColorEnabled = AppSettings.SonicPeakColorEnabled;
            SonicPeakIntensity = AppSettings.SonicPeakIntensity * 100f;
            CoverPulseEnabled = AppSettings.CoverPulseEnabled;
            SmtcTextAnimation = AppSettings.SmtcTextAnimation;
            _isInitialized = initialized;
        }

        private bool _coverPulseEnabled = true;
        private string _smtcTextAnimation = "none";
        public string SmtcTextAnimation
        {
            get => _smtcTextAnimation;
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (SetProperty(ref _smtcTextAnimation, value) && _isInitialized)
                    AppSettings.SmtcTextAnimation = value;
            }
        }

        public bool CoverPulseEnabled
        {
            get => _coverPulseEnabled;
            set
            {
                if (SetProperty(ref _coverPulseEnabled, value) && _isInitialized)
                    AppSettings.CoverPulseEnabled = value;
            }
        }

        public static bool IsBackdrop(string current, string option) => current == option;

        [RelayCommand]
        private async Task OpenOriginalSoundAsync()
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("originalsoundhqplayer:"),
                new Windows.System.LauncherOptions
                {
                    FallbackUri = new Uri("ms-windows-store://pdp/?ProductId=9NFW1RPPT999")
                });
        }

        [RelayCommand]
        private void ShowSettings() => WinExSpectrumTest.Helper.WindowHelper.OpenWindow<WinExSpectrumTest.View.SettingWindow>();

        [RelayCommand]
        private void NextEffect() => App.MainWindow?.SwitchEffect(1);

        [RelayCommand]
        private void PreviousEffect() => App.MainWindow?.SwitchEffect(-1);

        [RelayCommand]
        private void PersistSettings() => DataJsonService.SaveSettingNow();

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
