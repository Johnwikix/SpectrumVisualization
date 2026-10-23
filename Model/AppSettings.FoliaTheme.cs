using System.Threading;

namespace WinExSpectrumTest.Model;

public partial class AppSettings
{
    private static FoliaThemeSettings _foliaTheme = FoliaThemeSettings.Default;
    private static FoliaThemeSettings _foliaResolvedTheme = FoliaThemeSettings.Default;
    private static readonly object FoliaThemeGate = new();
    private static bool _foliaSystemLight;
    internal static FoliaThemeSettings FoliaResolvedTheme => Volatile.Read(ref _foliaResolvedTheme);
    public static FoliaThemeSettings FoliaTheme
    {
        get => Volatile.Read(ref _foliaTheme);
        set
        {
            var normalized = (value ?? FoliaThemeSettings.Default).Normalize();
            lock (FoliaThemeGate)
            {
                if (normalized == _foliaTheme) return;
                Volatile.Write(ref _foliaTheme, normalized);
                ResolveFoliaTheme();
            }
            Changed?.Invoke(nameof(FoliaTheme));
        }
    }

    internal static void UpdateFoliaSystemTheme(bool light)
    {
        lock (FoliaThemeGate)
        {
            if (_foliaSystemLight == light) return;
            _foliaSystemLight = light;
            if (_foliaTheme.Variant != "system") return;
            ResolveFoliaTheme();
        }
        Changed?.Invoke(nameof(FoliaTheme));
    }

    private static void ResolveFoliaTheme() => Volatile.Write(ref _foliaResolvedTheme,
        _foliaTheme.Variant == "system" ? _foliaTheme with { Variant = _foliaSystemLight ? "light" : "dark" } : _foliaTheme);
}
