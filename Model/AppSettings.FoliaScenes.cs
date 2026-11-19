namespace WinExSpectrumTest.Model;

public partial class AppSettings
{
    private static string _foliaNebbiaBehind = "night";
    public static string FoliaNebbiaBehind
    {
        get => _foliaNebbiaBehind;
        set => Set(ref _foliaNebbiaBehind, value is "aquarium" or "backdrop" ? value : "night");
    }

    private static string _foliaNebbiaGlass = "rain";
    public static string FoliaNebbiaGlass
    {
        get => _foliaNebbiaGlass;
        set => Set(ref _foliaNebbiaGlass, value is "dew" or "snow" ? value : "rain");
    }
}
