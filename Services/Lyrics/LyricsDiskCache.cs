using WinExSpectrumTest.Service;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using WinExSpectrumTest.Manager;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Services.Lyrics;

/// <summary>Only successful online lyrics are cached. Player data is never copied back or modified.</summary>
internal static class LyricsDiskCache
{
    private static readonly SemaphoreSlim IoGate = new(1, 1);
    private static readonly Lazy<string> DefaultFolder = new(ResolveDefaultFolder);
    internal static string Folder
    {
        get
        {
            string configured = AppSettings.LyricsCacheFolder;
            if (string.IsNullOrWhiteSpace(configured)) return DefaultFolder.Value;
            if (!Path.IsPathFullyQualified(configured)) throw new ArgumentException("The lyrics cache folder must be an absolute path.");
            return Path.GetFullPath(configured);
        }
    }

    private static string ResolveDefaultFolder()
    {
        if (FoliaPlaybackService.PreviewEnabled) return Path.Combine(AppContext.BaseDirectory, "LyricsCache");
        try { return Path.Combine(ApplicationData.Current.LocalFolder.Path, "LyricsCache"); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            AppLog.Caught(ex);
            // Unpackaged builds have no package ApplicationData. Resolving a UI label must never break x:Bind.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectrum", "LyricsCache");
        }
    }

    internal static async Task<LyricsContent?> ReadAsync(LyricsTrack track, CancellationToken token)
    {
        if (!AppSettings.LyricsCacheEnabled) return null;
        try
        {
            string file = Path.Combine(Folder, track.CacheKey + ".lyrics.json");
            if (!File.Exists(file)) return null;
            var entry = JsonSerializer.Deserialize(await LyricsTextFile.ReadAsync(file, token).ConfigureAwait(false),
                SettingsJsonContext.Default.LyricsCacheEntry);
            if (entry is null || entry.Version != 1 || entry.Title is null || entry.Artist is null || entry.Album is null || entry.Original is null ||
                new LyricsTrack(entry.Title, entry.Artist, entry.Album, 0).CacheKey != track.CacheKey) return null;
            var content = new LyricsContent(entry.Original, entry.Translation);
            return OriginalSoundLyricsSource.IsUsable(content, track.Duration) ? content : null;
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            App.WriteCrashLog("Lyrics cache read", ex.Message, ex);
            return null;
        }
    }

    internal static async Task WriteAsync(LyricsTrack track, LyricsContent content, CancellationToken token)
    {
        if (!AppSettings.LyricsCacheEnabled) return;
        await IoGate.WaitAsync(token).ConfigureAwait(false);
        string? temporary = null;
        try
        {
            if (!AppSettings.LyricsCacheEnabled) return;
            string folder = Folder;
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, track.CacheKey + ".lyrics.json");
            temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var entry = new LyricsCacheEntry
            {
                Title = track.Title, Artist = track.Artist, Album = track.Album,
                Original = content.Original, Translation = content.Translation
            };
            string json = JsonSerializer.Serialize(entry, SettingsJsonContext.Default.LyricsCacheEntry);
            if (Encoding.UTF8.GetByteCount(json) > LyricsTextFile.MaxBytes) return;
            await File.WriteAllTextAsync(temporary, json, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!AppSettings.LyricsCacheEnabled || !string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase)) return;
            File.Move(temporary, file, true);
            temporary = null;
        }
        catch (Exception ex) when (IsCacheFailure(ex)) { App.WriteCrashLog("Lyrics cache write", ex.Message, ex); }
        finally
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (IsCacheFailure(ex)) { App.WriteCrashLog("Lyrics cache cleanup", ex.Message, ex); }
            }
            IoGate.Release();
        }
    }

    private static bool IsCacheFailure(Exception error) => error is IOException or UnauthorizedAccessException
        or JsonException or ArgumentException or NotSupportedException or FormatException;
}
