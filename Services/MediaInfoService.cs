using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

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
        /// <summary>Preferred source app (the bundled HQPlayer package), kept from the legacy behavior.</summary>
        private const string PreferredAppUserModelIdFragment = "SennpaiStudio.528762A6196EF_z79ft30j24epr";

        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _session;

        // 串行化 UpdateSessionAsync（SessionsChanged / PlaybackInfoChanged / 健康检查并发触发）
        private readonly SemaphoreSlim _gate = new(1, 1);
        // 空枚举需连续两次（间隔 500ms）才认定"真的没有会话"——WinRT 在会话增删
        // 瞬间可能短暂返回空列表，单次就清空并解绑会让文字永久消失
        private int _emptyEnumerations;
        private int _healthChecksStarted;

        /// <summary>最近一次的媒体信息快照。效果页切换后重建时从这里回放，
        /// 否则 SMTC 只在新曲目属性变化时推送（表现为切回 Aurora 后信息丢失）。</summary>
        public string? CurrentTitle { get; private set; }
        public string? CurrentArtist { get; private set; }
        public IRandomAccessStreamReference? CurrentThumbnail { get; private set; }

        public event Action<string?, string?, IRandomAccessStreamReference?>? MediaTextChanged;
        public event Action<bool>? PlaybackChanged;
        public event Action<TimeSpan, TimeSpan>? TimelineChanged;

        public bool IsPlaying { get; private set; }
        public TimeSpan Position { get; private set; }
        public TimeSpan Duration { get; private set; }

        public async Task InitializeAsync()
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_manager == null) return;
                _manager.SessionsChanged += OnSessionsChanged;
                await UpdateSessionAsync();

                // 启动竞态：RequestAsync 返回时系统的会话枚举可能尚未就绪（概率性
                // 拿不到任何会话）。轮询等待第一个会话出现，最长约 5 秒。
                for (int i = 0; i < 10 && _session == null; i++)
                {
                    await Task.Delay(500);
                    await UpdateSessionAsync();
                }

                StartHealthWatchdog();
            }
            catch (Exception)
            {
            }
        }

        /// <summary>2s 健康检查：错过 SessionsChanged（或被瞬态空枚举清绑）时自愈重绑。
        /// 会话正常时该调用在 AUMID 判同后空转，开销可忽略。</summary>
        private void StartHealthWatchdog()
        {
            if (Interlocked.Exchange(ref _healthChecksStarted, 1) == 1) return;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        await Task.Delay(2000).ConfigureAwait(false);
                        await UpdateSessionAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
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
                if (_manager == null) return;

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
                    if (_session != null)
                    {
                        App.WriteCrashLog("SMTC", $"session list empty, unbinding (was {_session.SourceAppUserModelId})", null);
                        _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                        _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                        _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                        _session = null;
                    }
                    if (CurrentTitle != null || CurrentArtist != null)
                    {
                        CurrentTitle = null;
                        CurrentArtist = null;
                        CurrentThumbnail = null;
                        MediaTextChanged?.Invoke(null, null, null);
                        PlaybackChanged?.Invoke(false);
                        TimelineChanged?.Invoke(TimeSpan.Zero, TimeSpan.Zero);
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
                    if (sessions[i].SourceAppUserModelId.Contains(PreferredAppUserModelIdFragment))
                    {
                        try
                        {
                            var info = sessions[i].GetPlaybackInfo();
                            if (info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            {
                                selected = sessions[i];
                            }
                        }
                        catch (Exception)
                        {
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
                        catch (Exception)
                        {
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
                // 内容自愈：清空事件与重绑推送乱序时（切歌触发会话短暂重建的典型
                // 现象），缓存可能停留在空值且再无推送来修复——这里强制补推一次。
                if (string.IsNullOrEmpty(CurrentTitle) && string.IsNullOrEmpty(CurrentArtist))
                {
                    await RefreshMediaPropertiesAsync().ConfigureAwait(false);
                }
                return;
                }

                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                _session = selected;
                _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                App.WriteCrashLog("SMTC", $"bound session {_session.SourceAppUserModelId}", null);
                await RefreshMediaPropertiesAsync().ConfigureAwait(false);
                RefreshPlaybackAndTimeline();
            }
            catch (Exception)
            {
            }
            finally
            {
                _gate.Release();
            }
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await RefreshMediaPropertiesAsync();
        }

        private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            RefreshPlaybackAndTimeline();
            await RefreshMediaPropertiesAsync();
            // 当前会话停止/暂停而别的会话可能开始播放：重跑选择策略
            try
            {
                if (_session == null
                    || _session.GetPlaybackInfo()?.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    await UpdateSessionAsync();
                }
            }
            catch (Exception)
            {
            }
        }

        private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            RefreshPlaybackAndTimeline();
        }

        private async Task RefreshMediaPropertiesAsync()
        {
            try
            {
                if (_session == null) return;
                var props = await _session.TryGetMediaPropertiesAsync();
                if (props == null) return;
                // 标题与歌手全空视为"无效快照"（切歌瞬间/暂停态可能拿到）——不更新不推送，
                // 保留旧值等下一次推送；否则空串会把正在显示的信息冲掉（表现为文字消失）。
                if (string.IsNullOrEmpty(props.Title) && string.IsNullOrEmpty(props.Artist))
                {
                    return;
                }
                CurrentTitle = props.Title;
                CurrentArtist = props.Artist;
                CurrentThumbnail = props.Thumbnail;
                App.WriteCrashLog("SMTC", $"push title='{props.Title}' artist='{props.Artist}'", null);
                MediaTextChanged?.Invoke(props.Title, props.Artist, props.Thumbnail);
            }
            catch (Exception)
            {
            }
        }

        private void RefreshPlaybackAndTimeline()
        {
            try
            {
                if (_session == null) return;
                var playbackInfo = _session.GetPlaybackInfo();
                bool playing = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (playing != IsPlaying)
                {
                    IsPlaying = playing;
                    PlaybackChanged?.Invoke(playing);
                }

                var timeline = _session.GetTimelineProperties();
                Position = timeline.Position;
                Duration = timeline.EndTime;
                TimelineChanged?.Invoke(timeline.Position, timeline.EndTime);
            }
            catch (Exception)
            {
            }
        }
    }
}
