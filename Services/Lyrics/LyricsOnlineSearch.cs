using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

namespace WinExSpectrumTest.Services.Lyrics;

internal enum LyricsSearchStatus { Found, NoResult, NetworkError }
internal sealed record LyricsSearchResult(LyricsSearchStatus Status, LyricsContent? Content = null);

/// <summary>Serialized QQ-word → Netease-line → QQ-line lookup with per-provider negative results and cooling.</summary>
internal sealed class LyricsOnlineSearch
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, long> _misses = [];
    private readonly Circuit[] _circuits = [new(), new(), new()];
    private readonly Func<LyricsTrack, int, CancellationToken, Task<LyricsSearchResult>> _lookup;

    internal LyricsOnlineSearch(Func<LyricsTrack, int, CancellationToken, Task<LyricsSearchResult>>? lookup = null) =>
        _lookup = lookup ?? LookupAsync;

    internal async Task<LyricsSearchResult> SearchAsync(LyricsTrack track, bool force, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            bool failed = false;
            for (int channel = 0; channel < _circuits.Length; channel++)
            {
                token.ThrowIfCancellationRequested();
                string key = track.CacheKey + ":" + (long)(track.Duration * 1000) + ":" + channel;
                long now = Environment.TickCount64;
                if (!force && _misses.TryGetValue(key, out long until) && now < until) continue;
                if (!force && now < _circuits[channel].CooldownUntil) { failed = true; continue; }
                LyricsSearchResult result;
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(7));
                    result = await _lookup(track, channel, attempt.Token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (result.Status == LyricsSearchStatus.Found && !OriginalSoundLyricsSource.IsUsable(result.Content, track.Duration))
                        result = new(LyricsSearchStatus.NetworkError);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    App.WriteCrashLog("Lyrics provider " + channel, ex.Message, ex);
                    result = new(LyricsSearchStatus.NetworkError);
                }
                // Forced retries bypass cooling/misses, but still update provider health.
                _circuits[channel].Complete(result.Status, Environment.TickCount64);
                if (result.Status == LyricsSearchStatus.Found)
                {
                    _misses.Remove(key);
                    return result;
                }
                if (result.Status == LyricsSearchStatus.NoResult)
                {
                    if (_misses.Count >= 900) _misses.Clear();
                    _misses[key] = Environment.TickCount64 + 600_000;
                }
                else failed = true;
            }
            return new(failed ? LyricsSearchStatus.NetworkError : LyricsSearchStatus.NoResult);
        }
        finally { _gate.Release(); }
    }

    private sealed class Circuit
    {
        private int _failures;
        internal long CooldownUntil { get; private set; }
        internal void Complete(LyricsSearchStatus outcome, long now)
        {
            if (outcome == LyricsSearchStatus.NetworkError)
            {
                if (++_failures >= 3) CooldownUntil = now + 600_000;
            }
            else { _failures = 0; CooldownUntil = 0; }
        }
    }

    private static async Task<LyricsSearchResult> LookupAsync(LyricsTrack track, int channel, CancellationToken token)
    {
        var metadata = new TrackMultiArtistMetadata
        {
            Title = track.Title, Album = track.Album,
            Artists = [.. track.Artist.Split([" / ", ";", ", ", "、", " & "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            DurationMs = track.Duration > 0 ? (int)Math.Min(int.MaxValue, track.Duration * 1000) : null
        };
        var match = await SearchHelper.Search(metadata, channel == 1 ? Searchers.Netease : Searchers.QQMusic,
            channel == 0 ? CompareHelper.MatchType.Medium : CompareHelper.MatchType.Low, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (match is null) return new(LyricsSearchStatus.NoResult);
        string original, translation;
        if (channel == 0 && match is QQMusicSearchResult wordMatch)
        {
            var lyrics = await ProviderHelper.QQMusicApi.GetLyricsAsync(wordMatch.Id, token).ConfigureAwait(false);
            if (lyrics is null) return new(LyricsSearchStatus.NetworkError);
            original = lyrics.Lyrics ?? "";
            translation = lyrics.Trans ?? "";
        }
        else if (match is NeteaseSearchResult netease)
        {
            var lyrics = await ProviderHelper.NeteaseApi.GetLyric(netease.Id, token).ConfigureAwait(false);
            if (lyrics is null) return new(LyricsSearchStatus.NetworkError);
            original = lyrics.Lrc?.Lyric ?? "";
            translation = lyrics.Tlyric?.Lyric ?? "";
        }
        else if (match is QQMusicSearchResult qq)
        {
            var lyrics = await ProviderHelper.QQMusicApi.GetLyric(qq.Mid, token).ConfigureAwait(false);
            if (lyrics is null) return new(LyricsSearchStatus.NetworkError);
            original = lyrics.Lyric ?? "";
            translation = lyrics.Trans ?? "";
        }
        else return new(LyricsSearchStatus.NoResult);
        return string.IsNullOrWhiteSpace(original) ? new(LyricsSearchStatus.NoResult) :
            new(LyricsSearchStatus.Found, new(original, translation));
    }
}
