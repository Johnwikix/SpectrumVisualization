using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Lyricify.Lyrics.Helpers;
using Lyricify.Lyrics.Models;
using Lyricify.Lyrics.Providers.Web;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;
using KrcDecrypter = Lyricify.Lyrics.Decrypter.Krc.Decrypter;

namespace WinExSpectrumTest.Services.Lyrics;

internal sealed partial class LyricsOnlineSearch
{
    private const int DurationToleranceMs = 3500;
    private static readonly string[] ArtistSeparators = [", ", " / ", "、", "，", ";", "；", " & "];
    private static readonly Regex VersionTag = new(
        @"(?:[(\[（【]\s*|\s[-–—]\s+|^)(?:\d{4}\s+)?(?<tag>taylor['’]s version|live\b|acoustic\b|instrumental\b|karaoke\b|remaster(?:ed)?\b|remix\b|radio edit\b|extended\b|demo\b|re[- ]?record(?:ed|ing)?\b|现场|現場|伴奏|纯音乐|純音樂|重制|重製|重录|重錄)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly QQMusicSearcher QqSearcher = new(preferMobile: true);
    private static readonly NeteaseSearcher NeteaseSearcher = new(preferNew: true);

    private static async Task<LyricsSearchResult> LookupAsync(LyricsTrack track, LyricsProvider provider, CancellationToken token)
    {
        var query = new TrackMultiArtistMetadata
        {
            Title = track.Title,
            Album = track.Album,
            Artists = [.. SplitArtists(track.Artist)],
            DurationMs = track.Duration is > 0 and <= int.MaxValue / 1000d ? (int)(track.Duration * 1000) : null,
        };
        if (provider == LyricsProvider.LrcLibLine)
            return await LookupLrcLibAsync(query, token).ConfigureAwait(false);

        Searcher searcher = provider switch
        {
            LyricsProvider.QqWord => QqSearcher,
            LyricsProvider.KugouWord => SearcherHelper.KugouSearcher,
            LyricsProvider.NeteaseLine => NeteaseSearcher,
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
        var minimum = provider == LyricsProvider.NeteaseLine ? CompareHelper.MatchType.Medium : CompareHelper.MatchType.PrettyHigh;
        var match = await searcher.SearchForResult(query, minimum, candidate =>
            MatchesLyricsTrack(query, candidate.Title, candidate.Artists, candidate.DurationMs, (candidate as QQMusicSearchResult)?.Subtitle), token)
            .ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        switch (match)
        {
            case QQMusicSearchResult qq:
                var qrc = await ProviderHelper.QQMusicApi.GetLyricsAsync(qq.Id, token).ConfigureAwait(false);
                return qrc is null ? new(LyricsSearchStatus.NetworkError) : CreateResult(qrc.Lyrics, qrc.Trans);
            case KugouSearchResult kugou:
                return await DownloadKugouAsync(kugou, query.DurationMs, token).ConfigureAwait(false);
            case NeteaseSearchResult netease:
                var lrc = await ProviderHelper.NeteaseApi.GetLyric(netease.Id, token).ConfigureAwait(false);
                return lrc is null ? new(LyricsSearchStatus.NetworkError) : CreateResult(lrc.Lrc?.Lyric, lrc.Tlyric?.Lyric);
            default:
                return new(LyricsSearchStatus.NoResult);
        }
    }

    private static async Task<LyricsSearchResult> DownloadKugouAsync(KugouSearchResult track, int? duration, CancellationToken token)
    {
        var api = ProviderHelper.KugouApi;
        var response = await api.GetSearchLyrics($"{string.Join(", ", track.Artists)} - {track.Title}", track.DurationMs, track.Hash, token)
            .ConfigureAwait(false);
        if (response is null || response.Status is not (200 or 1) || response.ErrorCode is not (0 or 200))
            return new(LyricsSearchStatus.NetworkError);

        var candidate = response.Candidates?.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.AccessKey) &&
            !HasDurationConflict(duration, item.Duration));
        if (candidate is null) return new(LyricsSearchStatus.NoResult);

        var download = await api.DownloadLyrics(candidate.Id, candidate.AccessKey, token).ConfigureAwait(false);
        if (download is null || download.Status is not (200 or 1) || download.ErrorCode != 0)
            return new(LyricsSearchStatus.NetworkError);
        if (string.IsNullOrWhiteSpace(download.Content)) return new(LyricsSearchStatus.NoResult);

        token.ThrowIfCancellationRequested();
        return CreateResult(KrcDecrypter.DecryptLyrics(download.Content, LyricsTextFile.MaxBytes * 3 + 3));
    }

    private static async Task<LyricsSearchResult> LookupLrcLibAsync(TrackMultiArtistMetadata query, CancellationToken token)
    {
        string url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(query.Title ?? "")}&artist_name={Uri.EscapeDataString(query.Artist ?? "")}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("SpectrumVisualization");
        using var response = await BaseApi.HttpClient.SendAsync(request, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));

        int bestAlbumScore = -1;
        double closestDuration = double.MaxValue;
        string? bestLyrics = null;
        foreach (var item in json.RootElement.EnumerateArray())
        {
            token.ThrowIfCancellationRequested();
            if (!item.TryGetProperty("syncedLyrics", out var synced) || synced.ValueKind != JsonValueKind.String)
                continue;
            string? lyrics = synced.GetString();
            if (string.IsNullOrWhiteSpace(lyrics) || lyrics.Length > LyricsTextFile.MaxBytes)
                continue;

            string? title = item.GetProperty("trackName").GetString();
            string[] artists = SplitArtists(item.GetProperty("artistName").GetString());
            string? album = item.GetProperty("albumName").GetString();
            double milliseconds = item.GetProperty("duration").GetDouble() * 1000;
            int? duration = milliseconds is > 0 and <= int.MaxValue ? (int)milliseconds : null;
            if (!MatchesLyricsTrack(query, title, artists, duration, null))
                continue;

            int albumScore = CompareHelper.CompareName(query.Album, album).GetMatchScore();
            double difference = query.DurationMs is > 0 && duration is > 0
                ? Math.Abs(query.DurationMs.Value - duration.Value)
                : double.MaxValue;
            if (albumScore < bestAlbumScore || (albumScore == bestAlbumScore && difference >= closestDuration))
                continue;

            bestLyrics = lyrics;
            bestAlbumScore = albumScore;
            closestDuration = difference;
        }
        return CreateResult(bestLyrics);
    }

    private static LyricsSearchResult CreateResult(string? original, string? translation = null) => string.IsNullOrWhiteSpace(original)
        ? new(LyricsSearchStatus.NoResult)
        : new(LyricsSearchStatus.Found, new(original, translation));

    private static string[] SplitArtists(string? artist)
        => artist?.Split(ArtistSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    private static bool MatchesLyricsTrack(TrackMultiArtistMetadata query, string? title,
        string[] artists, int? duration, string? subtitle)
    {
        if (HasDurationConflict(query.DurationMs, duration) ||
            GetVersionTag(query.Title) != (GetVersionTag(title) ?? GetVersionTag(subtitle)))
            return false;

        int titleScore = CompareHelper.CompareName(query.Title, title).GetMatchScore();
        int artistScore = CompareHelper.CompareArtist(query.Artists, artists).GetMatchScore();
        return query.DurationMs is > 0 && duration is > 0
            ? titleScore >= 4 && artistScore >= 4
            : titleScore >= 6 && artistScore >= 5;
    }

    private static bool HasDurationConflict(int? expected, int? actual)
        => expected is > 0 && actual is > 0 && Math.Abs(expected.Value - actual.Value) >= DurationToleranceMs;

    private static string? GetVersionTag(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;
        var match = VersionTag.Match(title);
        return match.Success ? match.Groups["tag"].Value.ToLowerInvariant() switch
        {
            "现场" or "現場" => "live",
            "伴奏" or "纯音乐" or "純音樂" or "karaoke" => "instrumental",
            "remastered" or "重制" or "重製" => "remaster",
            "重录" or "重錄" or "re-recorded" or "re-recording" or "rerecorded" or "rerecording" => "rerecord",
            "taylor’s version" => "taylor's version",
            string tag => tag,
        } : null;
    }
}
