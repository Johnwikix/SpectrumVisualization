using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.ViewModel;

public partial class SettingViewModel
{
    public string FoliaNebbiaBehind
    {
        get => AppSettings.FoliaNebbiaBehind;
        set => AppSettings.FoliaNebbiaBehind = value;
    }

    public string FoliaNebbiaGlass
    {
        get => AppSettings.FoliaNebbiaGlass;
        set => AppSettings.FoliaNebbiaGlass = value;
    }
}
