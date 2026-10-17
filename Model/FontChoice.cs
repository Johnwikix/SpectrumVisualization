using Microsoft.UI.Xaml.Media;

namespace WinExSpectrumTest.Model;

public sealed class FontChoice(string name, string displayName)
{
    public string Name { get; } = name;
    public string DisplayName { get; } = displayName;
    public FontFamily Family { get; } = new(name);
}
