using System.Threading;

namespace WinExSpectrumTest.Model;

public partial class AppSettings
{
    private static FoliaThemeSettings _foliaTheme = FoliaThemeSettings.Default;
    public static FoliaThemeSettings FoliaTheme
    {
        get => Volatile.Read(ref _foliaTheme);
        set
        {
            var normalized = (value ?? FoliaThemeSettings.Default).Normalize();
            if (normalized == FoliaTheme) return;
            Volatile.Write(ref _foliaTheme, normalized);
            Changed?.Invoke(nameof(FoliaTheme));
        }
    }
}
