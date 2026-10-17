using System;

namespace WinExSpectrumTest.Model;

/// <summary>Persistent identifiers follow Folia's mode registry, independent of display names.</summary>
public static class FoliaSettings
{
    private static readonly string[] Modes = ["classic", "cadenza", "partita", "fume", "cappella", "tilt", "claddagh", "monet", "diorama", "pendolo", "sonnet", "tempera", "lumiere"];
    private static readonly string[] Backgrounds = ["common", "latent", "monet", "nomand", "sora"];
    private static readonly string[] ImageModes = ["dithering", "fluted-glass", "paper-texture", "halftone-dots", "lens-distortion"];
    public static ReadOnlySpan<string> LyricModes => Modes;
    public static ReadOnlySpan<string> BackgroundModes => Backgrounds;
    public static ReadOnlySpan<string> ImageEffects => ImageModes;
    public static string NormalizeLyricMode(string? value) => IndexOf(LyricModes, value) is int index && index >= 0 ? LyricModes[index] : "classic";
    public static string NormalizeBackground(string? value) => IndexOf(BackgroundModes, value) is int index && index >= 0 ? BackgroundModes[index] : "common";
    public static string NormalizeImageEffect(string? value) => IndexOf(ImageEffects, value) is int index && index >= 0 ? ImageEffects[index] : "dithering";
    public static int IndexOf(ReadOnlySpan<string> values, string? value)
    {
        for (int i = 0; i < values.Length; i++) if (values[i] == value) return i;
        return -1;
    }
}
