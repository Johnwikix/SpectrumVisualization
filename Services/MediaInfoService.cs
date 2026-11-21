using WinExSpectrumTest.Service;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinExSpectrumTest.Services.Lyrics;

namespace WinExSpectrumTest.Services
{
    /// <summary>
    /// Shared SMTC (System Media Transport Controls) watcher. Exposes the current
    /// session's track text, thumbnail and playback/timeline state to every effect.
    /// Events are raised on background threads; subscribers must marshal to the UI
    /// queue themselves (usually via DispatcherQueue.TryEnqueue).
    /// </summary>
    public sealed class MediaInfoService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private volatile GlobalSystemMediaTransportControlsSession? _session;

        // 串行化 UpdateSessionAsync（SessionsChanged / PlaybackInfoChanged / 健康检查并发触发）
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _snapshotGate = new();
        // 空枚举需连续两次（间隔 500ms）才认定"真的没有会话"——WinRT 在会话增删
        // 瞬间可能短暂返回空列表，单次就清空并解绑会让文字永久消失
        private int _emptyEnumerations;
        private int _healthChecksStarted;
        private readonly CancellationTokenSource _lifetime = new();
        private Task? _initialization;
        private Task? _watchdog;
        private volatile bool _stopping;

        /// <summary>最近一次的媒体信息快照。效果页切换后重建时从这里回放，
        /// 否则 SMTC 只在新曲目属性变化时推送（表现为切回 Aurora 后信息丢失）。</summary>
        public sealed record TrackSnapshot(string? Title, string? Artist, string? Album, string? SourceAppId, IRandomAccessStreamReference? Thumbnail);
        private static readonly TrackSnapshot EmptyTrack = new(null, null, null, null, null);
        private TrackSnapshot _currentTrack = EmptyTrack;
        public TrackSnapshot CurrentTrack => Volatile.Read(ref _currentTrack);
        public string? CurrentTitle => CurrentTrack.Title;
        public string? CurrentArtist => CurrentTrack.Artist;
        public string? CurrentAlbum => CurrentTrack.Album;
        public string? CurrentSourceAppId => CurrentTrack.SourceAppId;
        public IRandomAccessStreamReference? CurrentThumbnail => CurrentTrack.Thumbnail;
        private long _mediaRequestVersion;
        private Task? _mediaRefresh;
        private DateTimeOffset _timelineUpdated;
        private bool _hasTimeline;
        private readonly record struct PlaybackTimeline(bool Playing, TimeSpan Position, TimeSpan Duration, DateTimeOffset Updated, double Rate = 1);
        private MediaPlaybackClock _clock = MediaPlaybackClock.Empty;
        internal MediaPlaybackClock Clock => Volatile.Read(ref _clock);

        public event Action<string?, string?, IRandomAccessStreamReference?>? MediaTextChanged;
        public event Action<bool>? PlaybackChanged;
        public event Action<TimeSpan, TimeSpan>? TimelineChanged;

        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; private set; }
        public TimeSpan Duration { get; private set; }

        public Task InitializeAsync()
        {
            lock (_snapshotGate)
                return _stopping ? Task.CompletedTask : _initialization ??= Task.Run(InitializeCoreAsync);
        }

        private async Task InitializeCoreAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_stopping) return;
                if (_manager == null) return;
                _manager.SessionsChanged += OnSessionsChanged;
                await UpdateSessionAsync();

                // 启动竞态：RequestAsync 返回时系统的会话枚举可能尚未就绪（概率性
                // 拿不到任何会话）。轮询等待第一个会话出现，最长约 5 秒。
                for (int i = 0; i < 10 && _session == null; i++)
                {
                    await Task.Delay(500, _lifetime.Token);
                    await UpdateSessionAsync();
                }

                StartHealthWatchdog();
            }
            catch (Exception caughtException)
            {
                AppLog.Caught(caughtException);
            }
        }

        /// <summary>2s 健康检查：错过 SessionsChanged（或被瞬态空枚举清绑）时自愈重绑。
        /// 会话正常时该调用在 AUMID 判同后空转，开销可忽略。</summary>
        private void StartHealthWatchdog()
        {
            if (_stopping || Interlocked.Exchange(ref _healthChecksStarted, 1) == 1) return;
            _watchdog = Task.Run(async () =>
            {
                while (!_stopping)
                {
                    try
                    {
                        await Task.Delay(2000, _lifetime.Token).ConfigureAwait(false);
                        await UpdateSessionAsync().ConfigureAwait(false);
                    }
                    catch (Exception caughtException2)
                    {
                        AppLog.Caught(caughtException2);
                    }
                }
            });
        }

        private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            await UpdateSessionAsync();
        }

        private async Task UpdateSessionAsync()
        {
            // 串行化：SessionsChanged / PlaybackInfoChanged / 健康检查会并发进入
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_stopping || _manager == null) return;

                var sessions = _manager.GetSessions();
                if (sessions.Count == 0)
                {
                    // 瞬态空枚举不清空（WinRT 在会话增删瞬间可能短暂返回空列表）：
                    // 连续两次空读才认定真的没有会话；健康检查 2s 后会给第二次确认，
                    // 之后一旦会话回来也能重绑自愈。
                    if (Interlocked.Increment(ref _emptyEnumerations) < 2)
                    {
                        return;
                    }
                    var previous = _session;
                    if (previous != null)
                    {
                        App.WriteCrashLog("SMTC", $"session list empty, unbinding (was {previous.SourceAppUserModelId})", null);
                        previous.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                        previous.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                        previous.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                    }
                    lock (_snapshotGate)
                    {
                        if (_stopping) return;
                        _session = null;
                        _mediaRequestVersion++;
                        PublishSnapshot(EmptyTrack, default(PlaybackTimeline));
                    }
                    return;
                }
                Interlocked.Exchange(ref _emptyEnumerations, 0);

                // 会话选择策略（GetSessions 的顺序不代表活跃度，任何应用注册/注销
                // 会话都会触发重选——固定取 sessions[0] 会被无媒体的会话顶掉）：
                // 1) 首选 HQPlayer 包——但仅在其正在播放时（空闲空标题的会话不抢占，
                //    否则别的应用播放时文字恒为空）；2) 正在播放的会话；3) 维持当前
                // 已选会话；4) 兜底第一个。
                GlobalSystemMediaTransportControlsSession? selected = null;
                for (int i = 0; i < sessions.Count; i++)
                {
                    if (MediaSourceIdentity.IsOriginalSound(sessions[i].SourceAppUserModelId))
                    {
                        try
                        {
                            var info = sessions[i].GetPlaybackInfo();
                            if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            {
                                selected = sessions[i];
                            }
                        }
                        catch (Exception caughtException3)
                        {
                            AppLog.Caught(caughtException3);
                        }
                        break;
                    }
                }
                if (selected == null)
                {
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        try
                        {
                            var info = sessions[i].GetPlaybackInfo();
                            if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            {
                                selected = sessions[i];
                                break;
                            }
                        }
                        catch (Exception caughtException4)
                        {
                            AppLog.Caught(caughtException4);
                        }
                    }
                }
                if (selected == null && _session != null)
                {
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        if (sessions[i].SourceAppUserModelId == _session.SourceAppUserModelId)
                        {
                            selected = _session;
                            break;
                        }
                    }
                }
                selected ??= sessions[0];

                // 会话没变（同一来源应用的包装对象在枚举间可能不同，按 AUMID 判同）——
                // 跳过重绑与重推，避免把正在显示的信息清掉重闪。
                if (_session != null && selected.SourceAppUserModelId == _session.SourceAppUserModelId)
                {
                    // A player can publish the artist or a provisional timeline before its title/duration.
                    if (string.IsNullOrWhiteSpace(CurrentTitle))
                        await RequestMediaRefresh().ConfigureAwait(false);
                    else if (Clock.Duration <= 0)
                        RefreshPlaybackAndTimeline();
                    return;
                }

                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                lock (_snapshotGate)
                {
                    if (_stopping) return;
                    _session = selected;
                    _hasTimeline = false;
                    _timelineUpdated = default;
                    Volatile.Write(ref _clock, MediaPlaybackClock.Empty);
                }
                selected.MediaPropertiesChanged += OnMediaPropertiesChanged;
                selected.PlaybackInfoChanged += OnPlaybackInfoChanged;
                selected.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                App.WriteCrashLog("SMTC", $"bound session {selected.SourceAppUserModelId}", null);
                await RequestMediaRefresh().ConfigureAwait(false);
            }
            catch (Exception caughtException5)
            {
                AppLog.Caught(caughtException5);
            }
            finally
            {
                _gate.Release();
            }
        }

        private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            if (IsCurrentSession(sender)) _ = RequestMediaRefresh();
        }

        private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            if (!IsCurrentSession(sender)) return;
            bool playbackChanged = RefreshPlaybackAndTimeline();
            // Some players report metadata on the first playback state change.
            if (playbackChanged || string.IsNullOrEmpty(CurrentTitle) && string.IsNullOrEmpty(CurrentArtist))
                _ = RequestMediaRefresh();
            // 当前会话停止/暂停而别的会话可能开始播放：重跑选择策略
            if (!IsPlaying && playbackChanged) await UpdateSessionAsync().ConfigureAwait(false);
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            if (!IsCurrentSession(sender)) return;
            RefreshPlaybackAndTimeline();
        }

        private bool IsCurrentSession(GlobalSystemMediaTransportControlsSession sender)
        {
            // WinRT may project another wrapper for the same session. Use the same source identity as selection.
            try { return !_stopping && _session is { } current && (ReferenceEquals(sender, current) || sender.SourceAppUserModelId == current.SourceAppUserModelId); }
            catch (Exception caughtException7) { AppLog.Caught(caughtException7); return false; }
        }

        private Task RequestMediaRefresh()
        {
            lock (_snapshotGate)
            {
                if (_stopping || _session == null) return Task.CompletedTask;
                _mediaRequestVersion++;
                return _mediaRefresh ??= Task.Run(RefreshMediaPropertiesAsync);
            }
        }

        private async Task RefreshMediaPropertiesAsync()
        {
            // One WinRT read at a time. A burst arriving during the read needs only
            // one follow-up, and the obsolete result cannot update the track or clock.
            while (true)
            {
                GlobalSystemMediaTransportControlsSession session;
                long version;
                lock (_snapshotGate)
                {
                    if (_stopping || _session is not { } active)
                    {
                        _mediaRefresh = null;
                        return;
                    }
                    session = active;
                    version = _mediaRequestVersion;
                }
                try
                {
                    var props = await session.TryGetMediaPropertiesAsync().AsTask(_lifetime.Token).ConfigureAwait(false);
                    bool current;
                    lock (_snapshotGate) current = MatchesMediaRequest(session, version);
                    if (current)
                    {
                        // An empty snapshot can arrive momentarily while a player switches tracks.
                        TrackSnapshot? track = props == null || string.IsNullOrEmpty(props.Title) && string.IsNullOrEmpty(props.Artist)
                            ? null : new(props.Title, props.Artist, props.AlbumTitle, session.SourceAppUserModelId, props.Thumbnail);
                        var timeline = ReadPlaybackTimeline(session);
                        lock (_snapshotGate)
                            if (MatchesMediaRequest(session, version)) PublishSnapshot(track, timeline);
                    }
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
                catch (Exception ex) { AppLog.Caught(ex); }

                lock (_snapshotGate)
                {
                    if (!_stopping && version != _mediaRequestVersion) continue;
                    _mediaRefresh = null;
                    return;
                }
            }
        }

        private bool MatchesMediaRequest(GlobalSystemMediaTransportControlsSession session, long version) =>
            !_stopping && ReferenceEquals(session, _session) && version == _mediaRequestVersion;

        private static PlaybackTimeline? ReadPlaybackTimeline(GlobalSystemMediaTransportControlsSession session)
        {
            try
            {
                var playbackInfo = session.GetPlaybackInfo();
                bool playing = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var timeline = session.GetTimelineProperties();
                double rate = playbackInfo?.PlaybackRate ?? 1;
                return new(playing, timeline.Position, timeline.EndTime, timeline.LastUpdatedTime,
                    double.IsFinite(rate) && rate != 0 ? rate : 1);
            }
            catch (Exception ex)
            {
                AppLog.Caught(ex);
                return null;
            }
        }

        private bool RefreshPlaybackAndTimeline()
        {
            if (_stopping || _session is not { } session) return false;
            var timeline = ReadPlaybackTimeline(session);
            try
            {
                lock (_snapshotGate)
                {
                    if (_stopping || !ReferenceEquals(session, _session)) return false;
                    return PublishSnapshot(null, timeline);
                }
            }
            catch (Exception ex) { AppLog.Caught(ex); return false; }
        }

        // _snapshotGate orders reads and callbacks. Both sets of properties are visible
        // before subscribers are notified, so a new duration cannot amend the old track.
        private bool PublishSnapshot(TrackSnapshot? track, PlaybackTimeline? timeline)
        {
            bool playingChanged = false, timelineChanged = false;
            if (timeline is { } clock)
            {
                playingChanged = IsPlaying != clock.Playing;
                bool newPosition = !_hasTimeline || Position != clock.Position || _timelineUpdated != clock.Updated;
                double rate = clock.Rate == 0 ? 1 : clock.Rate;
                timelineChanged = newPosition || Duration != clock.Duration || _clock.Rate != rate;
                if (playingChanged || timelineChanged)
                {
                    long timestamp = Stopwatch.GetTimestamp();
                    // SMTC's Position is as of LastUpdatedTime. A cached opening sample may be
                    // minutes old. Pause/resume with the same sample preserves the local cursor.
                    double position = newPosition ? clock.Position.TotalSeconds : _clock.At(timestamp);
                    if (newPosition && clock.Playing && clock.Updated > DateTimeOffset.UnixEpoch)
                        position += Math.Max(0, (DateTimeOffset.UtcNow - clock.Updated).TotalSeconds) * rate;
                    Volatile.Write(ref _clock, new(Math.Max(0, position), Math.Max(0, clock.Duration.TotalSeconds),
                        clock.Playing, timestamp, rate));
                }
                IsPlaying = clock.Playing;
                _hasTimeline = true;
                Position = clock.Position;
                Duration = clock.Duration;
                _timelineUpdated = clock.Updated;
            }
            if (track != null && track != CurrentTrack)
            {
                Volatile.Write(ref _currentTrack, track);
                MediaTextChanged?.Invoke(track.Title, track.Artist, track.Thumbnail);
            }
            if (playingChanged) PlaybackChanged?.Invoke(IsPlaying);
            if (timelineChanged) TimelineChanged?.Invoke(Position, Duration);
            return playingChanged;
        }

        public async Task StopAsync()
        {
            Task? mediaRefresh;
            lock (_snapshotGate)
            {
                _stopping = true;
                mediaRefresh = _mediaRefresh;
            }
            _lifetime.Cancel();
            if (_initialization != null) await _initialization;
            if (_watchdog != null) await _watchdog;
            if (mediaRefresh != null) await mediaRefresh;
            await _gate.WaitAsync();
            try
            {
                if (_manager != null) _manager.SessionsChanged -= OnSessionsChanged;
                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                _session = null;
                _manager = null;
                Volatile.Write(ref _currentTrack, EmptyTrack);
                IsPlaying = false;
                Position = Duration = TimeSpan.Zero;
                Volatile.Write(ref _clock, MediaPlaybackClock.Empty);
            }
            finally { _gate.Release(); }
        }
    }
}
