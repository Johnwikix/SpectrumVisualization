
namespace WinExSpectrumTest.Model
{
    public class SaveSetting
    {
        public bool CoverPulseEnabled { get; set; } = true;
        public string SmtcTextAnimation { get; set; } = "none";
        public bool WallpaperEnabled { get; set; } = false;
        public int Id { get; set; } = 1;
        public bool IsDrawPlainSpectrum { get; set; } = false;
        public bool IsDrawRoundSpectrum { get; set; } = true;
        public float RotationSpeed { get; set; } = 10.0f;
        public float CoverOpacity { get; set; } = 1.0f;
        public float SpectrumOpacity { get; set; } = 1.0f;
        public float FontOpacity { get; set; } = 1.0f;
        public float FontShadow { get; set; } = 40.0f;
        public float SmoothingFactor { get; set; } = 0.9f;
        public float Sensitivity { get; set; } = 10.0f;
        public int PowCoe { get; set; } = 100;
        public string AppStyle { get; set; } = "Acrylic";
        public float CustomAcrylicOpacity { get; set; } = 0.5f;
        public byte CustomColorAlpha { get; set; } = 255;
        public byte CustomColorRed { get; set; } = 128;
        public byte CustomColorGreen { get; set; } = 128;
        public byte CustomColorBlue { get; set; } = 128;
        public bool IsUpdateBackDrop { get; set; } = false;
        public string AppTheme { get; set; } = "Default";
        public string elementTheme { get; set; } = "Default";
        public float RefreshRate { get; set; } = 60.0f;
        public int SampleRate { get; set; } = 12000;
        public int BarCount { get; set; } = 512;
        public string VisualEffect { get; set; } = "aurora-ring";
        public bool HdrEnabled { get; set; } = false;
        public float HdrWhiteNits { get; set; } = 200f;
        public float HdrPeakNits { get; set; } = 1000f;
        public string SonicTheme { get; set; } = "nocturnal";
        public float SonicAudioIntensity { get; set; } = 1.0f;
        public float SonicResponseRange { get; set; } = 1.0f;
        public int SonicGridSize { get; set; } = 160;
        public int SonicRenderScalePercent { get; set; } = 100;
        public string SonicAntiAliasing { get; set; } = "off";
        public string SonicUpscaleQuality { get; set; } = "quality";
        public bool SonicIdleWaveEnabled { get; set; } = true;
        public bool SonicRippleEnabled { get; set; } = true;
        public bool SonicMeteorEnabled { get; set; } = true;
        public bool SonicAutoRotate { get; set; } = false;
        public float SonicRotateSpeed { get; set; } = 10.0f;
        public bool SonicPeakColorEnabled { get; set; } = true;
        public float SonicPeakIntensity { get; set; } = 1.0f;
    }
}
