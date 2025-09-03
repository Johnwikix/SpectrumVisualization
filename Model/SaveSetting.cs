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
        public float RotationSpeed { get; set; } = 10.0f;
        public float CoverOpacity { get; set; } = 1.0f;
        public float SpectrumOpacity { get; set; } = 1.0f;
        public float FontOpacity { get; set; } = 1.0f;
    }
}
