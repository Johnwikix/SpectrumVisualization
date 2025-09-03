using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinExSpectrumTest.Model
{
    public class AppSettings
    {
        public static bool IsLocked { get; set; } = false;
        public static float RotationSpeed { get; set; } = 10.0f;
        public static float CoverOpacity { get; set; } = 1.0f;
        public static float SpectrumOpacity { get; set; } = 1.0f;
        public static float FontOpacity { get; set; } = 1.0f;
    }
}
