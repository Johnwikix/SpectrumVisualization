using System;
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
            }
            catch (Exception)
            {
            }
        }

        private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            await UpdateSessionAsync();
        }

        private async Task UpdateSessionAsync()
        {
            try
            {
                if (_manager == null) return;

                if (_session != null)
                {
                    _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                    _session = null;
                }

                var sessions = _manager.GetSessions();
                GlobalSystemMediaTransportControlsSession? selected = sessions.Count > 0 ? sessions[0] : null;
                for (int i = 0; i < sessions.Count; i++)
                {
                    if (sessions[i].SourceAppUserModelId.Contains(PreferredAppUserModelIdFragment))
                    {
                        selected = sessions[i];
                        break;
                    }
                }

                if (selected != null)
                {
                    _session = selected;
                    _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                    await RefreshMediaPropertiesAsync();
                    RefreshPlaybackAndTimeline();
                }
                else
                {
                    CurrentTitle = null;
                    CurrentArtist = null;
                    CurrentThumbnail = null;
                    MediaTextChanged?.Invoke(null, null, null);
                    PlaybackChanged?.Invoke(false);
                    TimelineChanged?.Invoke(TimeSpan.Zero, TimeSpan.Zero);
                }
            }
            catch (Exception)
            {
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
                CurrentTitle = props.Title;
                CurrentArtist = props.Artist;
                CurrentThumbnail = props.Thumbnail;
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
