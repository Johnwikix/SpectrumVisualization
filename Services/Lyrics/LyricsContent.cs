using System;
using System.Security.Cryptography;
using System.Text;

namespace WinExSpectrumTest.Services.Lyrics;

internal sealed record LyricsContent(string Original, string? Translation);

internal sealed record LyricsTrack(string Title, string Artist, string Album, double Duration)
{
    // Length prefixes keep field boundaries unambiguous; duration is deliberately not part of the disk key.
    internal string CacheKey
    {
        get
        {
            string title = Normalize(Title), artist = NormalizeArtists(Artist), album = Normalize(Album);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{title.Length}:{title}{artist.Length}:{artist}{album.Length}:{album}")));
        }
    }

    internal static string Normalize(string value) => value.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();

    internal static string NormalizeArtists(string value)
    {
        var artists = value.Split([" / ", ";", ", ", "、", " & "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < artists.Length; i++) artists[i] = Normalize(artists[i]);
        Array.Sort(artists, StringComparer.Ordinal);
        return string.Join('\n', artists);
    }
}

internal static class MediaSourceIdentity
{
    // Like BetterLyrics' PlayerIdHelper, identify the source from SMTC's package identity or executable name.
    internal static bool IsOriginalSound(string? id) => id is not null &&
        (id.StartsWith("SennpaiStudio.528762A6196EF_", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(System.IO.Path.GetFileName(id), "OriginalSound HIFI Player.exe", StringComparison.OrdinalIgnoreCase));
}

internal sealed class OriginalSoundPreferences
{
    public string LocalLyricsFormatOrder { get; set; } = "krc,qrc,lrc,ttml";
    public bool PreferDatabaseLyrics { get; set; }
    public bool IsAutoLyricsEnabled { get; set; } = true;
    public bool IsLyricsTranslationEnabled { get; set; } = true;
}

internal sealed class LyricsCacheEntry
{
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Original { get; set; } = "";
    public string? Translation { get; set; }
}
