using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Windows.ApplicationModel.Resources;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
    [ObservableProperty] public partial bool IsRebuildingShaders { get; set; }
    [ObservableProperty] public partial string ShaderCacheStatus { get; set; } = new ResourceLoader().GetString("ShaderCacheStatusReady");

    [RelayCommand]
    private async Task RebuildShadersAsync()
    {
        var resources = new ResourceLoader();
        IsRebuildingShaders = true;
        ShaderCacheStatus = resources.GetString("ShaderCacheStatusRunning");
        try
        {
            int count = await ShaderCacheMaintenance.RebuildAsync();
            ShaderCacheStatus = string.Format(CultureInfo.CurrentCulture, resources.GetString("ShaderCacheStatusComplete"), count);
        }
        catch (Exception ex)
        {
            ShaderCacheStatus = string.Format(CultureInfo.CurrentCulture, resources.GetString("ShaderCacheStatusFailed"), ex.Message);
            App.WriteCrashLog("Shader cache rebuild", ex.Message, ex);
        }
        finally { IsRebuildingShaders = false; }
    }
}
