using System;
using Microsoft.Windows.ApplicationModel.Resources;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Services.Lyrics;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
    public bool LyricsCacheEnabled
    {
        get => AppSettings.LyricsCacheEnabled;
        set => AppSettings.LyricsCacheEnabled = value;
    }

    public string LyricsCacheFolder
    {
        get => AppSettings.LyricsCacheFolder;
        set => AppSettings.LyricsCacheFolder = value;
    }

    public string LyricsCacheLocation
    {
        get
        {
            try { return LyricsDiskCache.Folder; }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.IOException)
            {
                return new ResourceLoader().GetString("LyricsCacheFolderInvalid");
            }
        }
    }

    private void NotifyLyricsSettings()
    {
        OnPropertyChanged(nameof(LyricsCacheEnabled));
        OnPropertyChanged(nameof(LyricsCacheFolder));
        OnPropertyChanged(nameof(LyricsCacheLocation));
    }
}
