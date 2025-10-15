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
    }
}
