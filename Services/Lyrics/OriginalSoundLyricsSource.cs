using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinExSpectrumTest.Effects.Folia;
using WinExSpectrumTest.Manager;

namespace WinExSpectrumTest.Services.Lyrics;

internal sealed record PlayerLyricsResult(LyricsContent? Content, bool AllowOnline = true, bool ShowTranslation = true, int OffsetMs = 0, bool AllowCache = true);

/// <summary>Reads the player's published on-disk contract without constructing any of its services.</summary>
internal sealed class OriginalSoundLyricsSource
{
    private readonly string _documentsRoot;
    private readonly string _packagesRoot;

    internal OriginalSoundLyricsSource(string? documentsRoot = null, string? packagesRoot = null)
    {
        _documentsRoot = documentsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OriginalSoundPlayer");
        _packagesRoot = packagesRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
    }

    internal async Task<PlayerLyricsResult> ReadAsync(LyricsTrack track, string sourceId, CancellationToken token)
    {
        if (!MediaSourceIdentity.IsOriginalSound(sourceId)) return new(null);
        string? packageFolder = null;
        int separator = sourceId.IndexOf('!');
        if (separator > 0)
        {
            string family = sourceId[..separator];
            if (family.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                packageFolder = Path.Combine(_packagesRoot, family, "LocalState");
        }
        string settingsPath = Path.Combine(_documentsRoot, "Settings", "Settings.json");
        string databasePath = Path.Combine(_documentsRoot, "DataBase", "MusicDatabase.db");
        if (!File.Exists(settingsPath) && packageFolder is not null) settingsPath = Path.Combine(packageFolder, "Settings.json");
        if (!File.Exists(databasePath) && packageFolder is not null) databasePath = Path.Combine(packageFolder, "MusicDatabase.db");
        var preferences = new OriginalSoundPreferences();
        try
        {
            if (File.Exists(settingsPath))
                preferences = JsonSerializer.Deserialize(await LyricsTextFile.ReadAsync(settingsPath, token).ConfigureAwait(false),
                    SettingsJsonContext.Default.OriginalSoundPreferences) ?? preferences;
        }
        catch (Exception ex) when (IsReadFailure(ex) || ex is JsonException)
        {
            App.WriteCrashLog("OriginalSound lyrics settings (read-only)", ex.Message, ex);
            // Never guess a priority or re-enable online searches after a torn/locked settings read.
            return new(null, AllowOnline: false);
        }

        var fallback = new PlayerLyricsResult(null, preferences.IsAutoLyricsEnabled, preferences.IsLyricsTranslationEnabled);
        if (!File.Exists(databasePath)) return fallback;
        StoredTrack? stored;
        try { stored = ReadTrack(databasePath, track, token); }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            App.WriteCrashLog("OriginalSound lyrics database (read-only)", ex.Message, ex);
            return fallback;
        }
        if (stored is null) return fallback;
        // Parsing happens after closing the SQLite transaction, so it never extends a reader lock.
        if (!IsUsable(stored.Content, track.Duration) && stored.LegacyFallback is not null)
            stored = stored with { Content = stored.LegacyFallback };
        var result = fallback with { AllowOnline = preferences.IsAutoLyricsEnabled && !stored.UserEdited,
            AllowCache = !stored.UserEdited, OffsetMs = stored.OffsetMs };
        if (preferences.PreferDatabaseLyrics && IsUsable(stored.Content, track.Duration)) return result with { Content = stored.Content };
        // Remote sidecars require the player's credentials/transport. Read its cached database row only.
        if (!stored.Remote)
        {
            try
            {
                var local = await ReadLocalAsync(stored.Path, preferences.LocalLyricsFormatOrder, track.Duration, token).ConfigureAwait(false);
                if (local is not null) return result with { Content = local };
            }
            catch (Exception ex) when (IsReadFailure(ex)) { App.WriteCrashLog("OriginalSound sidecar path", ex.Message, ex); }
        }
        return IsUsable(stored.Content, track.Duration) ? result with { Content = stored.Content } : result;
    }

    private sealed record StoredTrack(string Path, bool Remote, LyricsContent? Content, bool UserEdited, int OffsetMs, LyricsContent? LegacyFallback);

    private static StoredTrack? ReadTrack(string path, LyricsTrack track, CancellationToken token)
    {
        // Artist/album are validated in managed code (SQLite NOCASE only folds ASCII).
        // A missing artist cannot safely identify a library track by title alone.
        if (string.IsNullOrWhiteSpace(track.Artist)) return null;
        using var database = new ReadOnlyLyricsDatabase(path);
        database.Query("BEGIN", token);
        var candidates = database.Query("SELECT Id,Path,Author,Album,SourceId,LyricsOffsetMs FROM Music WHERE trim(Title)=? COLLATE NOCASE LIMIT 65", token, track.Title.Trim());
        if (candidates.Count > 64) return null;
        string?[]? match = null;
        string artist = LyricsTrack.NormalizeArtists(track.Artist), album = LyricsTrack.Normalize(track.Album);
        foreach (var row in candidates)
        {
            if (LyricsTrack.NormalizeArtists(row[2] ?? "") != artist) continue;
            if (album.Length != 0 && LyricsTrack.Normalize(row[3] ?? "") != album) continue;
            // Duplicate library entries can have different edits/offsets; SMTC cannot disambiguate them.
            if (match is not null) return null;
            match = row;
        }
        if (match is null) return null;
        LyricsContent? content = null;
        LyricsContent? legacyFallback = null;
        bool userEdited = false, authoritative = false;
        if (database.HasTable("MusicLyricsV2", token))
        {
            var rows = database.Query("SELECT Lyrics,TranslatedLyrics,SourceKind,SchemaVersion FROM MusicLyricsV2 WHERE MusicId=? LIMIT 1", token, match[0]!);
            if (rows.Count > 0)
            {
                authoritative = true; // Even an explicitly cleared row supersedes legacy lyrics.
                if (rows[0][3] == "2")
                {
                    content = new(rows[0][0] ?? "", rows[0][1]);
                    userEdited = rows[0][2] == "User";
                }
                else userEdited = true; // Unknown schema must not resurrect obsolete data.
            }
        }
        if (!authoritative && database.HasTable("MusicLyrics", token))
        {
            var rows = database.Query("SELECT Krc,TKrc,Lyrics,TranslatedLyrics FROM MusicLyrics WHERE MusicId=? LIMIT 1", token, match[0]!);
            if (rows.Count > 0)
            {
                content = new(rows[0][0] ?? "", rows[0][1]);
                legacyFallback = new(rows[0][2] ?? "", rows[0][3]);
            }
        }
        return new(match[1] ?? "", match[4] is not (null or "0"), content, userEdited,
            int.TryParse(match[5], out int offset) ? offset : 0, legacyFallback);
    }

    private static async Task<LyricsContent?> ReadLocalAsync(string path, string? order, double duration, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(path)) return null;
        string? directory = Path.GetDirectoryName(path);
        if (directory is null) return null;
        string stem = Path.GetFileNameWithoutExtension(path);
        var extensions = new List<string>(4);
        foreach (string item in (order ?? "").Split(','))
        {
            string extension = item.Trim().TrimStart('.').ToLowerInvariant();
            if (extension is "krc" or "qrc" or "lrc" or "ttml" && !extensions.Contains(extension)) extensions.Add(extension);
        }
        foreach (string extension in new[] { "krc", "qrc", "lrc", "ttml" })
            if (!extensions.Contains(extension)) extensions.Add(extension);
        foreach (string extension in extensions)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string file = Path.ChangeExtension(path, extension);
                if (!File.Exists(file)) continue;
                string original = LyricsTextFile.UnwrapQrc(await LyricsTextFile.ReadAsync(file, token).ConfigureAwait(false));
                if (!IsUsable(new(original, null), duration)) continue;
                var translations = new List<string> { "lrc" };
                if (!translations.Contains(extension)) translations.Add(extension);
                foreach (string ext in extensions) if (!translations.Contains(ext)) translations.Add(ext);
                foreach (string ext in translations)
                {
                    string translatedFile = Path.Combine(directory, stem + "_Translated." + ext);
                    if (!File.Exists(translatedFile)) continue;
                    try
                    {
                        string translation = LyricsTextFile.UnwrapQrc(await LyricsTextFile.ReadAsync(translatedFile, token).ConfigureAwait(false));
                        if (IsUsable(new(translation, null), duration)) return new(original, translation);
                    }
                    catch (Exception ex) when (IsReadFailure(ex)) { App.WriteCrashLog("OriginalSound translation read", ex.Message, ex); }
                }
                return new(original, null);
            }
            catch (Exception ex) when (IsReadFailure(ex)) { App.WriteCrashLog("OriginalSound sidecar read", ex.Message, ex); }
        }
        return null;
    }

    internal static bool IsUsable(LyricsContent? content, double duration) => content is not null &&
        content.Original is { Length: <= LyricsTextFile.MaxBytes } && FoliaSong.Parse(content.Original, content.Translation, duration).Length > 0;

    private static bool IsReadFailure(Exception error) => error is IOException or UnauthorizedAccessException or FormatException
        or System.Xml.XmlException or OverflowException or ArgumentException;
}
