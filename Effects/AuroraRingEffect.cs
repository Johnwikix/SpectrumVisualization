using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Text;
using System;
using System.Collections.Generic;
using System.Numerics;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Services;

namespace WinExSpectrumTest.Effects
{
    /// <summary>
    /// Redesigned default effect page ("Aurora Ring"). Keeps the SMTC album art and
    /// track info of the legacy panel, but rebuilds the visuals around it:
    /// a circular timeline progress ring, a log-mapped radial spectrum with peak
    /// caps and beat ripples, plus the optional plain bar mode. All per-frame state
    /// is preallocated; text/geometry objects are only rebuilt when the track or
    /// the canvas size actually changes.
    /// </summary>
    public sealed class AuroraRingEffect : IVisualizerEffect
    {
        public const string EffectId = "aurora-ring";

        public string Id => EffectId;
        public string DisplayName => DisplayNameConst;
        public const string DisplayNameConst = "Aurora Ring";

        private const int ProgressArcSegments = 96;
        private const int BassHistorySize = 43;

        private VisualizerServices _services = null!;
        private CanvasAnimatedControl _control = null!;

        // Audio state (render thread only).
        private int _barCount = 64;
        private int _half = 32;
        private float[] _bandPos = [];          // 每根条的对数频率边界（分数频段下标，镜像映射）
        private float[] _barT = [];             // 每根条在半圆内的频率位置（0 低频 → 1 高频）
        private int _bassBandEnd = 2;
        private float[] _smoothed = [];
        private float[] _peaks = [];
        private readonly float[] _bassHistory = new float[BassHistorySize];
        private int _bassHistoryIndex;
        private float _bass;
        private float _beatCooldown;
        private float _rotation;

        // Ripple ring pool.
        private readonly float[] _ringRadius = new float[4];
        private readonly float[] _ringAlpha = new float[4];
        private int _ringCursor;

        // SMTC state.
        private string? _title;
        private string? _artist;
        private IRandomAccessStreamReference? _lastThumbnailRef;   // 相同推送去重
        private bool _isPlaying;
        private readonly System.Diagnostics.Stopwatch _timelineWatch = new();
        private TimeSpan _timelineBase;
        private float _textFade;
        private CanvasBitmap? _albumArt;
        // 封面均色：节拍涟漪圆环的着色源
        private Color _primary = Color.FromArgb(255, 90, 170, 255);
        // 从封面提取的调色板（按色相排序），频谱条沿频率做渐变着色
        private Color[] _palette = [Color.FromArgb(255, 90, 170, 255), Color.FromArgb(255, 60, 220, 200)];

        // 文字颜色随背景自适应（移植 DesktopLyrics 思路）：1s 采样窗口外围环带亮度，
        // 阈值 128 + 滞回 16 切换黑/白文字，避免边界处采样抖动来回闪。
        private Microsoft.UI.Dispatching.DispatcherQueueTimer? _adaptiveColorTimer;
        private bool? _adaptiveIsDarkBackground;
        private Color _textColor = Color.FromArgb(255, 255, 255, 255);
        private const double AdaptiveSwitchThreshold = 128;
        private const double AdaptiveHysteresis = 16;

        // Cached device resources. The device is resolved lazily on first use -
        // CanvasAnimatedControl does not have one until it is loaded.
        private CanvasDevice? _deviceField;
        private CanvasDevice Device => _deviceField ??= _control.Device;
        private CanvasTextFormat _titleFormat = null!;
        private CanvasTextFormat _artistFormat = null!;
        private CanvasTextFormat _timeFormat = null!;
        private CanvasTextLayout? _titleLayout;
        private CanvasTextLayout? _artistLayout;
        private CanvasTextLayout? _timeLayout;
        private CanvasRenderTarget? _textLayer;          // 白色文字离屏层（软阴影源）
        private bool _textLayerDirty = true;
        private ShadowEffect? _shadowInner;
        private ShadowEffect? _shadowOuter;
        private CanvasGeometry? _clipCircle;
        private float _clipCircleRadius = -1f;
        private bool _clipDirty = true;
        private readonly Vector2[] _progressArc = new Vector2[ProgressArcSegments + 1];

        // Layout (recomputed on resize).
        private float _width;
        private float _height;
        private float _centerX;
        private float _centerY;
        private float _outerRadius;
        private float _albumRadius;
        private float _spectrumInner;
        private float _lastTimeSecond = -1;
        private bool _layoutsDirty = true;

        /// <summary>三段文字共用的布局框宽：DrawTextLayout 绘制整个布局框、文字在框内
        /// 居中，布局框宽必须与定位基准（GetTextPositions 的 frameWidth）一致，否则偏心。</summary>
        private float TextFrameWidth => Math.Min(_width, _height) * 0.46f;

        public void Initialize(VisualizerServices services)
        {
            _services = services;
            _control = services.Control;
            _titleFormat = new CanvasTextFormat
            {
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
                TrimmingSign = CanvasTrimmingSign.Ellipsis,
                TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            };
            _artistFormat = new CanvasTextFormat
            {
                FontSize = 14,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
                TrimmingSign = CanvasTrimmingSign.Ellipsis,
                TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            };
            _timeFormat = new CanvasTextFormat
            {
                FontSize = 11.5f,
                FontWeight = FontWeights.Normal,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
            };
            _services.Media.MediaTextChanged += OnMediaTextChanged;
            _services.Media.PlaybackChanged += OnPlaybackChanged;
            _services.Media.TimelineChanged += OnTimelineChanged;

            // 回放缓存：效果页切换回来时 SMTC 不会重新推送（推送只在曲目/状态变化时
            // 发生），从服务的快照恢复，避免"从 Sonic 切回 Aurora 信息丢失"。
            MediaInfoService media = _services.Media;
            if (media.CurrentTitle is not null || media.CurrentArtist is not null)
            {
                _title = media.CurrentTitle;
                _artist = media.CurrentArtist;
                _textFade = 1f;
                _layoutsDirty = true;
                if (media.CurrentThumbnail != null)
                {
                    LoadThumbnail(media.CurrentThumbnail);
                }
            }
            _isPlaying = media.IsPlaying;
            _timelineBase = media.Position;
            if (_isPlaying) _timelineWatch.Start();

            // 背景亮度轮询：驱动文字黑/白自适应
            if (_adaptiveColorTimer == null)
            {
                _adaptiveColorTimer = _control.DispatcherQueue.CreateTimer();
                _adaptiveColorTimer.Interval = TimeSpan.FromSeconds(1);
                _adaptiveColorTimer.Tick += (_, _) => RefreshAdaptiveTextColor();
            }
            _adaptiveColorTimer.Start();
        }

        /// <summary>采样窗口外围环带亮度，经滞回判定黑/白文字色（DesktopLyrics 同款）。</summary>
        private void RefreshAdaptiveTextColor()
        {
            var window = App.MainWindow;
            if (window == null) return;
            IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (!Helper.ScreenLuminanceSampler.TrySampleWindowLuminance(hwnd, out double luminance)) return;

            bool isDark;
            if (_adaptiveIsDarkBackground is { } previous)
            {
                isDark = previous
                    ? luminance < AdaptiveSwitchThreshold + AdaptiveHysteresis
                    : luminance < AdaptiveSwitchThreshold - AdaptiveHysteresis;
            }
            else
            {
                isDark = luminance < AdaptiveSwitchThreshold;
            }

            if (_adaptiveIsDarkBackground == isDark) return;
            _adaptiveIsDarkBackground = isDark;
            _textColor = isDark
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 16, 16, 16);
        }

        private void OnMediaTextChanged(string? title, string? artist, IRandomAccessStreamReference? thumbnail)
        {
            // 内容相同的重复推送直接忽略：每次推送都会清零 _textFade 并重载封面，
            // 高频重复推送（会话抖动/重绑）会让文字永远停在淡入起点（表现为消失）
            if (title == _title && artist == _artist && thumbnail == _lastThumbnailRef) return;
            _lastThumbnailRef = thumbnail;
            _control.DispatcherQueue.TryEnqueue(() =>
            {
                _title = title;
                _artist = artist;
                _textFade = 0f;
                _layoutsDirty = true;
                LoadThumbnail(thumbnail);
            });
        }

        private void OnPlaybackChanged(bool playing)
        {
            _control.DispatcherQueue.TryEnqueue(() =>
            {
                _isPlaying = playing;
                if (!playing) _timelineWatch.Reset();
                else if (!_timelineWatch.IsRunning) _timelineWatch.Start();
            });
        }

        private void OnTimelineChanged(TimeSpan position, TimeSpan duration)
        {
            _control.DispatcherQueue.TryEnqueue(() =>
            {
                _timelineBase = position;
                _timelineWatch.Reset();
                if (_isPlaying) _timelineWatch.Start();
            });
        }

        private async void LoadThumbnail(IRandomAccessStreamReference? thumbnail)
        {
            CanvasBitmap? bitmap = null;
            Color primary = default;
            Color[] palette = _palette;
            try
            {
                if (thumbnail != null)
                {
                    using var stream = await thumbnail.OpenReadAsync();
                    bitmap = await CanvasBitmap.LoadAsync(Device, stream);

                    // 16x16 CPU 解码：均色作主色，再做 HSV 直方图取 4 色调色板。
                    // 仅在曲目变更时运行，允许少量分配。
                    stream.Seek(0);
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = 16,
                        ScaledHeight = 16,
                        InterpolationMode = BitmapInterpolationMode.Fant,
                    };
                    var pixelData = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Rgba8,
                        BitmapAlphaMode.Ignore,
                        transform,
                        ExifOrientationMode.IgnoreExifOrientation,
                        ColorManagementMode.DoNotColorManage);
                    byte[] pixels = pixelData.DetachPixelData();

                    float r = 0f, g = 0f, b = 0f;
                    int count = pixels.Length / 4;
                    // 12 个色相桶，权重 = 饱和度 × 亮度 × 出现次数；剔除近黑/近灰像素
                    Span<float> hueWeight = stackalloc float[12];
                    Span<float> hueR = stackalloc float[12];
                    Span<float> hueG = stackalloc float[12];
                    Span<float> hueB = stackalloc float[12];
                    for (int i = 0; i + 3 < pixels.Length; i += 4)
                    {
                        float pr = pixels[i] / 255f;
                        float pg = pixels[i + 1] / 255f;
                        float pb = pixels[i + 2] / 255f;
                        r += pr;
                        g += pg;
                        b += pb;

                        float max = MathF.Max(pr, MathF.Max(pg, pb));
                        float min = MathF.Min(pr, MathF.Min(pg, pb));
                        float value = max;
                        float saturation = max <= 0f ? 0f : (max - min) / max;
                        if (value < 0.14f || saturation < 0.14f) continue;   // 近黑/近灰：不参与色相统计
                        float hue = 0f;
                        float delta = max - min;
                        if (delta > 0f)
                        {
                            if (max == pr) hue = ((pg - pb) / delta) % 6f;
                            else if (max == pg) hue = (pb - pr) / delta + 2f;
                            else hue = (pr - pg) / delta + 4f;
                            hue *= 60f;
                            if (hue < 0f) hue += 360f;
                        }
                        int bin = Math.Clamp((int)(hue / 30f), 0, 11);
                        float weight = saturation * value;
                        hueWeight[bin] += weight;
                        hueR[bin] += pr * weight;
                        hueG[bin] += pg * weight;
                        hueB[bin] += pb * weight;
                    }

                    primary = Color.FromArgb(255,
                        (byte)(Math.Clamp(r / count / 255f * 1.25f, 0f, 1f) * 255f),
                        (byte)(Math.Clamp(g / count / 255f * 1.25f, 0f, 1f) * 255f),
                        (byte)(Math.Clamp(b / count / 255f * 1.25f, 0f, 1f) * 255f));

                    // 取权重最高的色相桶，按色相排序构成 4 色调色板（相邻桶去重），
                    // 桶均值色向主色回拉 35% 保证与整体观感协调。
                    var candidates = new List<(int Bin, float Weight)>(12);
                    for (int bin = 0; bin < 12; bin++)
                    {
                        if (hueWeight[bin] > 0.01f) candidates.Add((bin, hueWeight[bin]));
                    }
                    candidates.Sort((a, b2) => b2.Weight.CompareTo(a.Weight));
                    var picked = new List<int>(4);
                    foreach ((int bin, _) in candidates)
                    {
                        bool adjacent = false;
                        foreach (int chosen in picked)
                        {
                            int distance = Math.Abs(chosen - bin);
                            distance = Math.Min(distance, 12 - distance);
                            if (distance <= 1) { adjacent = true; break; }
                        }
                        if (!adjacent) picked.Add(bin);
                        if (picked.Count == 4) break;
                    }
                    if (picked.Count > 0)
                    {
                        picked.Sort();
                        var extracted = new Color[picked.Count];
                        for (int k = 0; k < picked.Count; k++)
                        {
                            int bin = picked[k];
                            float w = MathF.Max(hueWeight[bin], 0.001f);
                            var mean = Color.FromArgb(255,
                                (byte)(Math.Clamp(hueR[bin] / w, 0f, 1f) * 255f),
                                (byte)(Math.Clamp(hueG[bin] / w, 0f, 1f) * 255f),
                                (byte)(Math.Clamp(hueB[bin] / w, 0f, 1f) * 255f));
                            extracted[k] = LerpColor(mean, primary, 0.35f);
                        }
                        palette = extracted;
                    }
                }
            }
            catch (Exception)
            {
                bitmap = null;
            }

            _albumArt?.Dispose();
            _albumArt = bitmap;
            if (bitmap != null)
            {
                _primary = primary;
                _palette = palette;
            }
        }

        public void OnResize(float width, float height)
        {
            _width = width;
            _height = height;
            _centerX = width * 0.5f;
            _centerY = height * 0.5f;
            _outerRadius = Math.Min(width, height) * 0.5f;
            _albumRadius = _outerRadius * 0.42f;
            _spectrumInner = _outerRadius * 0.62f;
            _clipDirty = true;
            _layoutsDirty = true;
            _textLayerDirty = true;
        }

        private int _mappedSampleRate;

        private void EnsureBandMapping()
        {
            int requested = Math.Clamp(AppSettings.BarCount, 32, SpectrumAnalyzer.BandCount);
            int sampleRate = _services.Analyzer.SampleRate;
            if (requested == _barCount && _mappedSampleRate == sampleRate && _smoothed.Length > 0) return;
            _mappedSampleRate = sampleRate;

            _barCount = requested;
            _half = _barCount / 2;
            _smoothed = new float[_barCount];
            _peaks = new float[_barCount];
            _bandPos = new float[_barCount + 1];
            _barT = new float[_barCount];
            // 镜像对数频率映射（左右两半各自 40 Hz .. 16 kHz）：
            // 圆环首尾（条 0 与条 N-1）都是低频、圆心正上方是高频——低频接低频、
            // 高频接高频，环形收口处不再有低→高硬跳变；
            // 左半圆驱动自左声道、右半圆驱动自右声道（双声道镜像频谱）。
            // 条带边界存"分数频段下标"：低频处多根条共用一个线性频段，整数取整
            // 会让相邻条同高复制；分数位置 + 邻段插值让相邻条连续变化。
            const float minFreq = 40f;
            float nyquist = Math.Max(_services.Analyzer.SampleRate, 8000) * 0.5f;
            float maxFreq = MathF.Min(16000f, nyquist * 0.9f);
            for (int i = 0; i <= _barCount; i++)
            {
                // 半圆内位置 t：左半 0→1（低→高），右半 1→0（高→低），i==half 为 1
                float t = i <= _half
                    ? (float)i / _half
                    : (float)(_barCount - i) / _half;
                t = Math.Clamp(t, 0f, 1f);
                float freq = minFreq * MathF.Pow(maxFreq / minFreq, t);
                _bandPos[i] = freq / nyquist * SpectrumAnalyzer.BandCount;
                if (i < _barCount) _barT[i] = t;
            }
            _bassBandEnd = Math.Clamp((int)(250f / nyquist * SpectrumAnalyzer.BandCount), 1, SpectrumAnalyzer.BandCount - 1);
        }

        /// <summary>按分数位置在相邻线性频段间插值取样。</summary>
        private static float SampleBands(ReadOnlySpan<float> bands, float pos)
        {
            int b0 = (int)pos;
            if (b0 >= SpectrumAnalyzer.BandCount - 1) return bands[SpectrumAnalyzer.BandCount - 1];
            float frac = pos - b0;
            return bands[b0] + (bands[b0 + 1] - bands[b0]) * frac;
        }

        public void Update(double elapsedSeconds)
        {
            float dt = (float)Math.Clamp(elapsedSeconds, 0.0, 0.1);
            EnsureBandMapping();
            ReadOnlySpan<float> bandsL = _services.Analyzer.LatestBandsLeft;
            ReadOnlySpan<float> bandsR = _services.Analyzer.LatestBandsRight;

            float gain = AppSettings.Sensitivity * 0.1f;
            float smoothing = Math.Clamp(AppSettings.SmoothingFactor, 0f, 0.99f);
            float bassSum = 0f;

            for (int i = 0; i < _barCount; i++)
            {
                // 镜像布局：左半圆取左声道、右半圆取右声道；
                // 右半的频段区间向低频方向递减，取 min/max 归一为 [低, 高]。
                ReadOnlySpan<float> bands = i < _half ? bandsL : bandsR;
                float pa = _bandPos[i];
                float pb = _bandPos[i + 1];
                float p0 = MathF.Min(pa, pb);
                float p1 = MathF.Max(MathF.Max(pa, pb), p0 + 0.001f);
                float v = MathF.Max(SampleBands(bands, p0), SampleBands(bands, p1)) * gain;
                int b0 = (int)p0 + 1;
                int b1 = Math.Min((int)p1, SpectrumAnalyzer.BandCount - 1);
                for (int b = b0; b <= b1; b++)
                {
                    float bv = bands[b] * gain;
                    if (bv > v) v = bv;
                }
                // 高频能量天然偏低：沿频率做轻微倾斜补偿（t 为半圆内频率位置），
                // 避免低频束与高频束之间出现硬落差
                float tilt = 0.7f + 0.6f * _barT[i];
                v *= tilt;
                v = MathF.Pow(Math.Clamp(v, 0f, 1f), 100f / Math.Clamp(AppSettings.PowCoe, 50, 500));
                float s = _smoothed[i];
                s = s * smoothing + v * (1f - smoothing);
                _smoothed[i] = s;
                float p = _peaks[i] - dt * 0.55f;
                _peaks[i] = s > p ? s : p;
            }

            // 频域空间平滑（3 抽头）：抹平相邻条之间的幅度硬接，让低频束→高频束连续过渡
            float leftNeighbor = _smoothed[0];
            for (int i = 1; i < _barCount - 1; i++)
            {
                float current = _smoothed[i];
                _smoothed[i] = leftNeighbor * 0.25f + current * 0.5f + _smoothed[i + 1] * 0.25f;
                leftNeighbor = current;
            }

            // 节拍检测用混音频段（低频两声道几乎一致，与改造前行为相同）；
            // 低频能量驱动节拍涟漪与封面的呼吸缩放。
            ReadOnlySpan<float> bandsMixed = _services.Analyzer.LatestBands;
            for (int b = 0; b <= _bassBandEnd; b++)
            {
                bassSum += bandsMixed[b];
            }
            _bass = bassSum / (_bassBandEnd + 1);
            _bassHistory[_bassHistoryIndex] = _bass;
            _bassHistoryIndex = (_bassHistoryIndex + 1) % BassHistorySize;
            float bassMean = 0f;
            for (int i = 0; i < BassHistorySize; i++) bassMean += _bassHistory[i];
            bassMean /= BassHistorySize;

            // 节拍涟漪：半径按窗口内切半径归一化，渐隐绑定扩散进度，
            // 保证涟漪始终完整收敛在窗口范围内（锁定态不被窗口边硬裁切）。
            _beatCooldown -= dt;
            if (_bass > bassMean * 1.35f && _bass > 0.03f && _beatCooldown <= 0f)
            {
                _beatCooldown = 0.35f;
                _ringRadius[_ringCursor] = _albumRadius * 1.05f;
                _ringAlpha[_ringCursor] = 1f;
                _ringCursor = (_ringCursor + 1) % _ringRadius.Length;
            }

            float maxRingRadius = MathF.Min(_centerX, _centerY);
            for (int i = 0; i < _ringRadius.Length; i++)
            {
                if (_ringAlpha[i] > 0f)
                {
                    _ringRadius[i] += dt * maxRingRadius * 0.55f;
                    float progress = (_ringRadius[i] - _albumRadius) / MathF.Max(maxRingRadius - _albumRadius, 1f);
                    if (progress >= 1f)
                    {
                        _ringAlpha[i] = 0f;
                    }
                    else
                    {
                        float fade = 1f - progress;
                        _ringAlpha[i] = 0.6f * fade * fade;
                    }
                }
            }

            _rotation += dt * AppSettings.RotationSpeed * 0.006f;
            if (_textFade < 0.999f)
            {
                _textFade = Math.Min(1f, _textFade + Math.Clamp(dt * 3f, 0f, 1f));
                _textLayerDirty = true;   // 淡入期间离屏层透明度同步
            }

            // 时间文本每秒重建（渲染线程）
            double duration = _services.Media.Duration.TotalSeconds;
            if (duration > 1.0)
            {
                double elapsed = _timelineBase.TotalSeconds + (_timelineWatch.IsRunning ? _timelineWatch.Elapsed.TotalSeconds : 0);
                int currentSecond = (int)elapsed;
                if (currentSecond != _lastTimeSecond || _timeLayout == null)
                {
                    _lastTimeSecond = currentSecond;
                    _timeLayout?.Dispose();
                    _timeLayout = new CanvasTextLayout(
                        Device,
                        FormatTime(elapsed) + " / " + FormatTime(duration),
                        _timeFormat,
                        TextFrameWidth,
                        20f);
                    _textLayerDirty = true;
                }
            }
        }

        private void RebuildTextLayouts()
        {
            _layoutsDirty = false;
            float maxTextWidth = TextFrameWidth;
            float fontSize = 17f * Math.Clamp(_outerRadius / 256f, 0.55f, 1.6f);
            _titleFormat.FontSize = fontSize;
            _artistFormat.FontSize = fontSize * 0.82f;
            _timeFormat.FontSize = fontSize * 0.68f;

            _titleLayout?.Dispose();
            _artistLayout?.Dispose();
            _timeLayout?.Dispose();
            _titleLayout = string.IsNullOrEmpty(_title)
                ? null
                : new CanvasTextLayout(Device, _title, _titleFormat, maxTextWidth, fontSize * 1.4f);
            _artistLayout = string.IsNullOrEmpty(_artist)
                ? null
                : new CanvasTextLayout(Device, _artist, _artistFormat, maxTextWidth, fontSize * 1.2f);
            _timeLayout = null;
            _lastTimeSecond = -1;
            _textLayerDirty = true;
        }

        public void Draw(CanvasDrawingSession session, float width, float height)
        {
            if (width != _width || height != _height)
            {
                OnResize(width, height);
            }

            // Resources that need a device are built here: Win2D only exposes the
            // control's device on the render thread (CreateResources/Draw).
            if (_layoutsDirty)
            {
                RebuildTextLayouts();
            }
            if (_clipDirty && _clipCircleRadius < 0f)
            {
                _clipCircle?.Dispose();
                _clipCircle = CanvasGeometry.CreateCircle(session, _centerX, _centerY, _albumRadius);
                _clipCircleRadius = _albumRadius;
                _clipDirty = false;
            }

            if (AppSettings.IsDrawRoundSpectrum)
            {
                DrawProgressRing(session);
                DrawRippleRings(session);
                DrawRadialSpectrum(session);
                DrawAlbumArt(session);
                DrawTrackInfo(session);
            }

            if (AppSettings.IsDrawPlainSpectrum)
            {
                DrawPlainSpectrum(session, width, height);
            }
        }

        private void DrawProgressRing(CanvasDrawingSession session)
        {
            double duration = _services.Media.Duration.TotalSeconds;
            if (duration <= 1.0) return;
            double elapsed = _timelineBase.TotalSeconds + (_timelineWatch.IsRunning ? _timelineWatch.Elapsed.TotalSeconds : 0);
            float fraction = (float)Math.Clamp(elapsed / duration, 0.0, 1.0);

            float radius = (_albumRadius + _spectrumInner) * 0.5f;
            // 跟随自适应文字色（深色模式白、亮色模式黑），与文字保持一致的可读性；
            // 之前用封面主色，深色封面在深背景上整条弧几乎不可见。
            Color c = _textColor;
            c.A = (byte)(200 * AppSettings.SpectrumOpacity);
            Color glow = c;
            glow.A = (byte)(40 * AppSettings.SpectrumOpacity);

            // 先取整段顶点，再补一段从最后整段顶点到圆点精确角度的部分段，
            // 让弧线与圆点严格同步平滑运动（而不是按 1/96 进度跳格）。
            float fractionAngle = fraction * MathF.PI * 2f;
            int fullSteps = (int)(fraction * ProgressArcSegments);
            float startAngle = -MathF.PI * 0.5f;
            float step = MathF.PI * 2f / ProgressArcSegments;
            if (fullSteps < 1) return;

            for (int i = 0; i <= fullSteps; i++)
            {
                float a = startAngle + i * step;
                _progressArc[i] = new Vector2(
                    _centerX + MathF.Cos(a) * radius,
                    _centerY + MathF.Sin(a) * radius);
            }

            var tip = new Vector2(
                _centerX + MathF.Cos(startAngle + fractionAngle) * radius,
                _centerY + MathF.Sin(startAngle + fractionAngle) * radius);

            for (int i = 0; i < fullSteps - 1; i++)
            {
                session.DrawLine(_progressArc[i], _progressArc[i + 1], glow, 5f);
            }
            for (int i = 0; i < fullSteps - 1; i++)
            {
                session.DrawLine(_progressArc[i], _progressArc[i + 1], c, 2f);
            }

            // 部分段：从最后一条整段边到圆点
            session.DrawLine(_progressArc[fullSteps - 1], tip, glow, 5f);
            session.DrawLine(_progressArc[fullSteps - 1], tip, c, 2f);

            c.A = 255;
            session.FillCircle(tip, 3f, c);
        }

        private void DrawRippleRings(CanvasDrawingSession session)
        {
            for (int i = 0; i < _ringRadius.Length; i++)
            {
                float alpha = _ringAlpha[i];
                if (alpha <= 0f) continue;
                Color c = _primary;
                c.A = (byte)(alpha * 255f * AppSettings.SpectrumOpacity);
                session.DrawCircle(new Vector2(_centerX, _centerY), _ringRadius[i], c, 2f);
            }
        }

        /// <summary>沿频率位置在封面调色板上取渐变色，高亮处向白色提亮。
        /// 在 HSV 空间插值（色相走最短路径）：调色板相邻色跨越色相时，RGB 线性
        /// 插值会经过低饱和灰谷，视觉上呈"色块硬接"——HSV 插值让过渡带保持饱和。</summary>
        private Color GetGradientColor(float t, float intensity)
        {
            Color[] palette = _palette;
            if (palette.Length == 1)
            {
                // 单色调色板（封面主色相集中在同一桶，相邻桶去重后只剩 1 色）：
                // 此时 (palette.Length - 1) = 0、index 被 clamp 成 -1，继续索引会越界。
                // 无渐变可用，频谱整环退化为唯一色 + 按强度向白提亮。
                return LerpColor(palette[0], Color.FromArgb(255, 255, 255, 255), intensity * 0.35f);
            }
            float scaled = Math.Clamp(t, 0f, 1f) * (palette.Length - 1);
            int index = Math.Min((int)scaled, palette.Length - 2);
            Color c = LerpColorHsv(palette[index], palette[index + 1], scaled - index);
            return LerpColor(c, Color.FromArgb(255, 255, 255, 255), intensity * 0.35f);
        }

        private void DrawRadialSpectrum(CanvasDrawingSession session)
        {
            float spectrumRange = _outerRadius * 0.33f;
            float angleStep = MathF.PI * 2f / _barCount;
            float lineWidth = angleStep * _spectrumInner * 0.62f;
            byte baseAlpha = (byte)(255 * AppSettings.SpectrumOpacity);

            for (int i = 0; i < _barCount; i++)
            {
                float intensity = Math.Clamp(_smoothed[i], 0f, 1f);
                float angle = i * angleStep - _rotation - MathF.PI * 0.5f;
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);
                var inner = new Vector2(_centerX + cos * _spectrumInner, _centerY + sin * _spectrumInner);
                var outer = new Vector2(_centerX + cos * (_spectrumInner + intensity * spectrumRange),
                                        _centerY + sin * (_spectrumInner + intensity * spectrumRange));

                // 颜色跟随半圆内频率位置（镜像对称），与幅度映射一致：
                // 圆环首尾相接处两侧都是 t=0（palette[0]）、顶部两侧都是 t=1——
                // 若按环位置 0→1 取色，首尾分别是 palette 首尾色，底部出现暖冷硬接。
                Color c = GetGradientColor(_barT[i], intensity);
                c.A = baseAlpha;
                session.DrawLine(inner, outer, c, lineWidth);

                // Peak cap.
                float capRadius = _spectrumInner + _peaks[i] * spectrumRange + lineWidth * 0.6f;
                var cap = new Vector2(_centerX + cos * capRadius, _centerY + sin * capRadius);
                c.A = (byte)(baseAlpha * 0.85f);
                session.FillCircle(cap, lineWidth * 0.42f, c);

                // Mirrored inner bars, subtler.
                var inner2 = new Vector2(_centerX + cos * (_spectrumInner - 6f), _centerY + sin * (_spectrumInner - 6f));
                var inner3 = new Vector2(_centerX + cos * (_spectrumInner - 6f - intensity * spectrumRange * 0.28f),
                                         _centerY + sin * (_spectrumInner - 6f - intensity * spectrumRange * 0.28f));
                c.A = (byte)(baseAlpha * 0.35f);
                session.DrawLine(inner2, inner3, c, lineWidth * 0.7f);
            }
        }

        private void DrawAlbumArt(CanvasDrawingSession session)
        {
            if (_albumArt == null || _clipCircle == null) return;
            // Quantize the pulse radius so the clip geometry is only rebuilt when the
            // quantized value flips (avoids allocating a new geometry every frame).
            float radius = MathF.Round(_albumRadius * (1f + (AppSettings.CoverPulseEnabled ? Math.Clamp(_bass, 0f, 1f) * 0.05f : 0f)) * 2f) * 0.5f;
            if (_clipDirty || _clipCircleRadius != radius)
            {
                _clipCircle?.Dispose();
                _clipCircle = CanvasGeometry.CreateCircle(session, _centerX, _centerY, radius);
                _clipCircleRadius = radius;
                _clipDirty = false;
            }

            float aspect = (float)_albumArt.SizeInPixels.Width / _albumArt.SizeInPixels.Height;
            float drawWidth, drawHeight;
            if (aspect > 1f)
            {
                drawHeight = radius * 2f;
                drawWidth = drawHeight * aspect;
            }
            else
            {
                drawWidth = radius * 2f;
                drawHeight = drawWidth / aspect;
            }

            session.Transform = Matrix3x2.CreateRotation(_rotation * 0.25f, new Vector2(_centerX, _centerY)) * session.Transform;
            using (session.CreateLayer(1f, _clipCircle))
            {
                session.DrawImage(
                    _albumArt,
                    new Rect(_centerX - drawWidth * 0.5f, _centerY - drawHeight * 0.5f, drawWidth, drawHeight),
                    new Rect(0, 0, _albumArt.SizeInPixels.Width, _albumArt.SizeInPixels.Height),
                    AppSettings.CoverOpacity,
                    CanvasImageInterpolation.Linear);
            }
            session.Transform = Matrix3x2.CreateRotation(_rotation * -0.25f, new Vector2(_centerX, _centerY)) * session.Transform;
        }

        private void DrawTrackInfo(CanvasDrawingSession session)
        {
            byte alpha = (byte)(255 * AppSettings.FontOpacity * _textFade);
            if (alpha == 0) return;

            EnsureTextLayer();

            Color textColor = _textColor;
            textColor.A = alpha;

            // 可调软阴影（music_player Win2D 渲染器同款）：文字以白色离屏渲染进
            // _textLayer，ShadowEffect 从其 alpha 生成高斯模糊阴影；阴影色 = 文字色
            // 反相，强度 0-100 拆内外两层（模糊量与偏移分层，观感与参考一致）。
            float strength = Math.Clamp(AppSettings.FontShadow, 0f, 100f) / 100f;
            if (strength > 0f && _textLayer != null)
            {
                Color inverse = Color.FromArgb(255, (byte)(255 - textColor.R), (byte)(255 - textColor.G), (byte)(255 - textColor.B));

                if (_shadowInner == null) _shadowInner = new ShadowEffect();
                _shadowInner.Source = _textLayer;
                _shadowInner.BlurAmount = 6f;
                _shadowInner.ShadowColor = Color.FromArgb((byte)(255 * strength * 0.85f), inverse.R, inverse.G, inverse.B);
                

                if (_shadowOuter == null) _shadowOuter = new ShadowEffect();
                _shadowOuter.Source = _textLayer;
                _shadowOuter.BlurAmount = 16f;
                _shadowOuter.ShadowColor = Color.FromArgb((byte)(255 * strength * 0.45f), inverse.R, inverse.G, inverse.B);

                var old = session.Transform;
                session.Transform = Matrix3x2.CreateTranslation(1.5f, 1.5f) * old;
                session.DrawImage(_shadowInner);
                session.Transform = Matrix3x2.CreateTranslation(4f, 4f) * old;
                session.DrawImage(_shadowOuter);
                session.Transform = old;
            }

            GetTextPositions(out Vector2 titlePos, out Vector2 artistPos, out Vector2 timePos);

            if (_titleLayout != null)
            {
                session.DrawTextLayout(_titleLayout, titlePos, textColor);
            }
            if (_artistLayout != null)
            {
                Color artistColor = textColor;
                artistColor.A = (byte)(alpha * 0.62f);
                session.DrawTextLayout(_artistLayout, artistPos, artistColor);
            }

            double duration = _services.Media.Duration.TotalSeconds;
            if (duration > 1.0 && _timeLayout != null)
            {
                Color timeColor = textColor;
                timeColor.A = (byte)(alpha * 0.45f);
                session.DrawTextLayout(_timeLayout, timePos, timeColor);
            }
        }

        /// <summary>三段文字的绘制位置（DrawTrackInfo 与离屏层共用同一套布局）。
        /// 注意按"布局框宽"定位：DrawTextLayout 绘制整个布局框（文字已在框内居中），
        /// 若按 LayoutBounds.Width（紧凑宽）定位会系统性右偏 (框宽-文字宽)/2。</summary>
        private void GetTextPositions(out Vector2 titlePos, out Vector2 artistPos, out Vector2 timePos)
        {
            float frameWidth = TextFrameWidth;
            float frameX = _centerX - frameWidth * 0.5f;
            float textY = _centerY + _outerRadius * 0.70f;
            titlePos = _titleLayout != null
                ? new Vector2(frameX, textY)
                : default;
            if (_titleLayout != null) textY += (float)_titleLayout.LayoutBounds.Bottom;
            artistPos = _artistLayout != null
                ? new Vector2(frameX, textY)
                : default;
            if (_artistLayout != null) textY += (float)_artistLayout.LayoutBounds.Bottom;
            timePos = _timeLayout != null
                ? new Vector2(frameX, textY + 2f)
                : default;
        }

        /// <summary>把全部文字以白色渲染进离屏层（阴影从其 alpha 生成）；布局/曲目/秒数/
        /// 淡入变化时置脏。每秒至多重绘一次，尺寸内 GPU 开销可忽略。</summary>
        private void EnsureTextLayer()
        {
            if (_width <= 0 || _height <= 0) return;
            float dpi = _control.Dpi;
            if (_textLayer == null
                || MathF.Abs((float)_textLayer.Size.Width - _width) > 0.5f
                || MathF.Abs((float)_textLayer.Size.Height - _height) > 0.5f
                || MathF.Abs(_textLayer.Dpi - dpi) > 0.1f)
            {
                _textLayer?.Dispose();
                _textLayer = new CanvasRenderTarget(Device, _width, _height, dpi);
                _textLayerDirty = true;
            }
            if (!_textLayerDirty) return;
            _textLayerDirty = false;

            byte alpha = (byte)(255 * AppSettings.FontOpacity * _textFade);
            using CanvasDrawingSession s = _textLayer.CreateDrawingSession();
            s.Clear(Color.FromArgb(0, 0, 0, 0));
            if (alpha == 0) return;
            Color white = Color.FromArgb(alpha, 255, 255, 255);
            GetTextPositions(out Vector2 titlePos, out Vector2 artistPos, out Vector2 timePos);
            if (_titleLayout != null) s.DrawTextLayout(_titleLayout, titlePos, white);
            if (_artistLayout != null)
            {
                white.A = (byte)(alpha * 0.62f);
                s.DrawTextLayout(_artistLayout, artistPos, white);
            }
            if (_timeLayout != null)
            {
                white.A = (byte)(alpha * 0.45f);
                s.DrawTextLayout(_timeLayout, timePos, white);
            }
        }

        private static string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
            int total = (int)seconds;
            return (total / 60).ToString() + ":" + (total % 60).ToString("00");
        }

        private void DrawPlainSpectrum(CanvasDrawingSession session, float width, float height)
        {
            float centerY = height * 0.5f;
            float maxHeight = height * 0.3f;
            float barWidth = width / _barCount;
            float lineWidth = MathF.Max(barWidth - 2f, 1.5f);
            byte baseAlpha = (byte)(255 * AppSettings.SpectrumOpacity);
            float frequencySpan = 1f / MathF.Max(_barCount - 1, 1);

            for (int i = 0; i < _barCount; i++)
            {
                float intensity = Math.Clamp(_smoothed[i], 0f, 1f);
                float x = i * barWidth + barWidth * 0.5f;
                float barHeight = intensity * maxHeight;
                var top = new Vector2(x, centerY - barHeight);
                var bottom = new Vector2(x, centerY + barHeight);

                Color c = GetGradientColor(i * frequencySpan, intensity);
                c.A = baseAlpha;
                session.DrawLine(top, bottom, c, lineWidth);

                if (intensity > 0.08f)
                {
                    Color glow = c;
                    glow.A = (byte)(baseAlpha * 0.14f);
                    var glowTop = new Vector2(x, centerY - barHeight - 6f);
                    var glowBottom = new Vector2(x, centerY + barHeight + 6f);
                    session.DrawLine(glowTop, glowBottom, glow, barWidth);
                }

                float capY = centerY - Math.Clamp(_peaks[i], 0f, 1f) * maxHeight - 4f;
                c.A = (byte)(baseAlpha * 0.8f);
                session.FillCircle(new Vector2(x, capY), lineWidth * 0.45f, c);
            }
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return Color.FromArgb(
                (byte)(a.A + (b.A - a.A) * t),
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
        }

        /// <summary>HSV 空间插值（色相走最短路径）。调色板相邻色常跨越色相，
        /// RGB 线性插值的中段会掉进低饱和灰谷，看起来像色块之间"硬接"。</summary>
        private static Color LerpColorHsv(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            RgbToHsv(a, out float h1, out float s1, out float v1);
            RgbToHsv(b, out float h2, out float s2, out float v2);

            float dh = h2 - h1;
            if (dh > 180f) dh -= 360f;
            else if (dh < -180f) dh += 360f;

            return HsvToRgb(h1 + dh * t, s1 + (s2 - s1) * t, v1 + (v2 - v1) * t, (byte)Math.Clamp(a.A + (b.A - a.A) * t, 0f, 255f));
        }

        private static void RgbToHsv(Color c, out float h, out float s, out float v)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float max = MathF.Max(r, MathF.Max(g, b));
            float min = MathF.Min(r, MathF.Min(g, b));
            float d = max - min;
            v = max;
            s = max <= 0f ? 0f : d / max;
            if (d <= 0f)
            {
                h = 0f;
            }
            else if (max == r)
            {
                h = 60f * (((g - b) / d) % 6f);
            }
            else if (max == g)
            {
                h = 60f * ((b - r) / d + 2f);
            }
            else
            {
                h = 60f * ((r - g) / d + 4f);
            }
            if (h < 0f) h += 360f;
        }

        private static Color HsvToRgb(float h, float s, float v, byte alpha)
        {
            h = h % 360f;
            if (h < 0f) h += 360f;
            float c = v * s;
            float x = c * (1f - MathF.Abs(h / 60f % 2f - 1f));
            float m = v - c;
            float r, g, b;
            switch ((int)(h / 60f))
            {
                case 0: r = c; g = x; b = 0f; break;
                case 1: r = x; g = c; b = 0f; break;
                case 2: r = 0f; g = c; b = x; break;
                case 3: r = 0f; g = x; b = c; break;
                case 4: r = x; g = 0f; b = c; break;
                default: r = c; g = 0f; b = x; break;
            }
            return Color.FromArgb(
                alpha,
                (byte)Math.Clamp((r + m) * 255f, 0f, 255f),
                (byte)Math.Clamp((g + m) * 255f, 0f, 255f),
                (byte)Math.Clamp((b + m) * 255f, 0f, 255f));
        }

        public void Dispose()
        {
            _services.Media.MediaTextChanged -= OnMediaTextChanged;
            _services.Media.PlaybackChanged -= OnPlaybackChanged;
            _services.Media.TimelineChanged -= OnTimelineChanged;
            _adaptiveColorTimer?.Stop();
            _titleLayout?.Dispose();
            _artistLayout?.Dispose();
            _timeLayout?.Dispose();
            _clipCircle?.Dispose();
            _textLayer?.Dispose();
            _shadowInner?.Dispose();
            _shadowOuter?.Dispose();
            _albumArt?.Dispose();
            _titleFormat.Dispose();
            _artistFormat.Dispose();
            _timeFormat.Dispose();
        }
    }
}
