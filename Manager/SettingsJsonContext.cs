using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Manager
{
    [JsonSerializable(typeof(SaveSetting))]
    [JsonSerializable(typeof(Services.Lyrics.OriginalSoundPreferences))]
    [JsonSerializable(typeof(Services.Lyrics.LyricsCacheEntry))]
    internal partial class SettingsJsonContext : JsonSerializerContext
    {
    }
}
