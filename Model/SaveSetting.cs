using Microsoft.UI.Xaml;
using SQLite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinExSpectrumTest.Model
{
    public class SaveSetting
    {
        [PrimaryKey]
        public int Id { get; set; } = 1;
        public bool IsDrawPlainSpectrum { get; set; } = false;
        public bool IsDrawRoundSpectrum { get; set; } = true;
        public float RotationSpeed { get; set; } = 10.0f;
        public float CoverOpacity { get; set; } = 1.0f;
        public float SpectrumOpacity { get; set; } = 1.0f;
        public float FontOpacity { get; set; } = 1.0f;
        public float SmoothingFactor { get; set; } = 0.95f;
        public float Sensitivity { get; set; } = 10.0f;
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
    }
}
