using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Service;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
    public bool IsRunningBackend
    {
        get => AppSettings.IsRunningBackend;
        set => AppSettings.IsRunningBackend = value;
    }

    public string LogDirectory => AppLog.DirectoryPath;

    [RelayCommand]
    private async Task OpenLogDirectoryAsync()
    {
        try
        {
            System.IO.Directory.CreateDirectory(LogDirectory);
            if (!await Windows.System.Launcher.LaunchFolderPathAsync(LogDirectory))
                throw new InvalidOperationException("Windows did not open the log folder.");
        }
        catch (Exception error) { AppLog.Caught(error, "Opening the log folder failed."); }
    }
}
