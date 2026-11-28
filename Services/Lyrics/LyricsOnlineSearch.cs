using WinExSpectrumTest.Service;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WinExSpectrumTest.Effects.Folia;

namespace WinExSpectrumTest.Services.Lyrics;

internal enum LyricsSearchStatus { Found, NoResult, NetworkError }
internal enum LyricsProvider { QqWord, KugouWord, NeteaseLine, LrcLibLine }
internal sealed record LyricsSearchResult(LyricsSearchStatus Status, LyricsContent? Content = null);

/// <summary>Serialized online lookup with per-provider negative results and cooling.</summary>
internal sealed partial class LyricsOnlineSearch
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, long> _misses = [];
    private static readonly LyricsProvider[] Providers =
        [LyricsProvider.QqWord, LyricsProvider.KugouWord, LyricsProvider.NeteaseLine, LyricsProvider.LrcLibLine];
    private readonly Circuit[] _circuits = Array.ConvertAll(Providers, static _ => new Circuit());
    private readonly Func<LyricsTrack, LyricsProvider, CancellationToken, Task<LyricsSearchResult>> _lookup;

    internal LyricsOnlineSearch(Func<LyricsTrack, LyricsProvider, CancellationToken, Task<LyricsSearchResult>>? lookup = null) =>
        _lookup = lookup ?? LookupAsync;

    internal async Task<LyricsSearchResult> SearchAsync(LyricsTrack track, bool force, bool preferChineseTranslation, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            bool failed = false;
            LyricsSearchResult? fallback = null;
            string trackKey = track.CacheKey + ":" + (long)(track.Duration * 1000);
            for (int channel = 0; channel < Providers.Length; channel++)
            {
                token.ThrowIfCancellationRequested();
                var provider = Providers[channel];
                if (provider == LyricsProvider.LrcLibLine && fallback is not null)
                    return fallback; // LRCLIB supplies only original lyrics.
                string key = trackKey + ":" + channel;
                long now = Environment.TickCount64;
                if (!force && _misses.TryGetValue(key, out long until) && now < until) continue;
                if (!force && now < _circuits[channel].CooldownUntil) { failed = true; continue; }
                LyricsSearchResult result;
                bool preferred = false;
                try
                {
                    using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
                    attempt.CancelAfter(TimeSpan.FromSeconds(6));
                    result = await _lookup(track, provider, attempt.Token).ConfigureAwait(false);
                    attempt.Token.ThrowIfCancellationRequested();
                    if (result.Status == LyricsSearchStatus.Found)
                    {
                        var lines = result.Content is { } content
                            ? FoliaSong.Parse(content.Original, content.Translation, track.Duration) : [];
                        if (lines.Length == 0)
                            result = new(LyricsSearchStatus.NetworkError);
                        else if ((provider is LyricsProvider.QqWord or LyricsProvider.KugouWord) &&
                            !Array.Exists(lines, static line => line.WordSynced))
                            result = new(LyricsSearchStatus.NoResult);
                        else
                            preferred = !preferChineseTranslation || provider == LyricsProvider.LrcLibLine || HasChineseTranslation(lines);
                        attempt.Token.ThrowIfCancellationRequested();
                    }
                }
                catch (OperationCanceledException caughtException) when (token.IsCancellationRequested) { AppLog.Caught(caughtException); throw; }
                catch (Exception ex)
                {
                    App.WriteCrashLog("Lyrics provider " + provider, ex.Message, ex);
                    result = new(LyricsSearchStatus.NetworkError);
                }
                token.ThrowIfCancellationRequested();
                // Forced retries bypass cooling/misses, but still update provider health.
                _circuits[channel].Complete(result.Status, Environment.TickCount64);
                if (result.Status == LyricsSearchStatus.Found)
                {
                    _misses.Remove(key);
                    if (preferred) return result;
                    fallback ??= result;
                }
                if (result.Status == LyricsSearchStatus.NoResult)
                {
                    if (_misses.Count >= 900) _misses.Clear();
                    _misses[key] = Environment.TickCount64 + 600_000;
                }
                else failed = true;
            }
            token.ThrowIfCancellationRequested();
            return fallback ?? new(failed ? LyricsSearchStatus.NetworkError : LyricsSearchStatus.NoResult);
        }
        finally { _gate.Release(); }
    }

    private static bool HasChineseTranslation(FoliaLine[] lines)
    {
        bool hasHan = false;
        foreach (var line in lines)
        {
            var translation = line.Translation.AsSpan().Trim();
            if (translation.SequenceEqual(line.Text.AsSpan().Trim())) continue;
            foreach (char character in translation)
            {
                if (character is >= '\u3040' and <= '\u30ff' or >= '\u31f0' and <= '\u31ff'
                    or >= '\u1100' and <= '\u11ff' or >= '\u3130' and <= '\u318f' or >= '\uac00' and <= '\ud7af')
                    return false;
                hasHan |= character is >= '\u3400' and <= '\u4dbf' or >= '\u4e00' and <= '\u9fff' or >= '\uf900' and <= '\ufaff';
            }
        }
        return hasHan;
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
}
