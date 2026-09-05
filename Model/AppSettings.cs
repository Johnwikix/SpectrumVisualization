using Microsoft.UI.Xaml;

namespace WinExSpectrumTest.Model
{
    public class AppSettings
    {
        public static bool IsLocked { get; set; } = false;
        public static bool IsDrawPlainSpectrum { get; set; } = false;
        public static bool IsDrawRoundSpectrum { get; set; } = true;
        public static float RotationSpeed { get; set; } = 10.0f;
        public static float CoverOpacity { get; set; } = 1.0f;
        public static float SpectrumOpacity { get; set; } = 1.0f;
        public static float FontOpacity { get; set; } = 1.0f;
        public static float SmoothingFactor { get; set; } = 0.9f;
        public static float Sensitivity { get; set; } = 10.0f;
        public static int PowCoe { get; set; } = 100;
        public static string AppStyle { get; set; } = "Acrylic";
        public static float CustomAcrylicOpacity { get; set; } = 0.5f;
        public static byte CustomColorAlpha { get; set; } = 255;
        public static byte CustomColorRed { get; set; } = 128;
        public static byte CustomColorGreen { get; set; } = 128;
        public static byte CustomColorBlue { get; set; } = 128;
        public static bool IsUpdateBackDrop { get; set; } = false;
        public static string AppTheme { get; set; } = "Default";
        public static ElementTheme elementTheme { get; set; } = ElementTheme.Default;
        public static float RefreshRate { get; set; } = 60.0f;
        public static int SampleRate { get; set; } = 12000;
        public static int BarCount { get; set; } = 512;

        /// <summary>Active visualizer effect page id (see EffectRegistry).</summary>
        public static string VisualEffect { get; set; } = "aurora-ring";

        // Sonic Topography effect settings.
        public static string SonicTheme { get; set; } = "nocturnal";
        public static float SonicAudioIntensity { get; set; } = 1.0f;
        public static float SonicResponseRange { get; set; } = 1.0f;
        public static int SonicGridSize { get; set; } = 160;
        public static bool SonicIdleWaveEnabled { get; set; } = true;
        public static bool SonicRippleEnabled { get; set; } = true;
        public static bool SonicMeteorEnabled { get; set; } = true;
        public static bool SonicAutoRotate { get; set; } = false;
        public static float SonicRotateSpeed { get; set; } = 10.0f;
        public static bool SonicPeakColorEnabled { get; set; } = true;
        public static float SonicPeakIntensity { get; set; } = 1.0f;

        /// <summary>Whether the window is docked to the desktop wallpaper layer.</summary>
        public static bool WallpaperMode { get; set; } = false;
    }
}
