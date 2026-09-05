using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
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
    /// a bass-breathing aura, a circular timeline progress ring, a log-mapped
    /// radial spectrum with peak caps and beat ripples, plus the optional plain
    /// bar mode. All per-frame state is preallocated; text/geometry objects are
    /// only rebuilt when the track or the canvas size actually changes.
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
        private float[] _bandPos = [];          // 每根条的对数频率边界（分数频段下标）
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
        private bool _isPlaying;
        private readonly System.Diagnostics.Stopwatch _timelineWatch = new();
        private TimeSpan _timelineBase;
        private float _textFade;
        private CanvasBitmap? _albumArt;
        private Color _primary = Color.FromArgb(255, 90, 170, 255);
        // 从封面提取的调色板（按色相排序），频谱条沿频率做渐变着色
        private Color[] _palette = [Color.FromArgb(255, 90, 170, 255), Color.FromArgb(255, 60, 220, 200)];
        private bool _brushDirty = true;

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
        private CanvasRadialGradientBrush? _auraBrush;
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
            Color primary = _primary;
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
                _brushDirty = true;
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
        }

        private void EnsureBandMapping()
        {
            int requested = Math.Clamp(AppSettings.BarCount, 32, SpectrumAnalyzer.BandCount);
            if (requested == _barCount && _smoothed.Length > 0) return;

            _barCount = requested;
            _smoothed = new float[_barCount];
            _peaks = new float[_barCount];
            _bandPos = new float[_barCount + 1];
            // 对数频率映射（40 Hz .. 16 kHz），条带边界存"分数频段下标"：
            // 低频处多根条共用一个线性频段，整数取整会让相邻条同高复制；
            // 分数位置 + 邻段插值让相邻条连续变化（配合 4096 FFT 的频率分辨率）。
            const float minFreq = 40f;
            float nyquist = Math.Max(_services.Analyzer.SampleRate, 8000) * 0.5f;
            float maxFreq = MathF.Min(16000f, nyquist * 0.9f);
            for (int i = 0; i <= _barCount; i++)
            {
                float freq = minFreq * MathF.Pow(maxFreq / minFreq, (float)i / _barCount);
                _bandPos[i] = freq / nyquist * SpectrumAnalyzer.BandCount;
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
            ReadOnlySpan<float> bands = _services.Analyzer.LatestBands;

            float gain = AppSettings.Sensitivity * 0.1f;
            float smoothing = Math.Clamp(AppSettings.SmoothingFactor, 0f, 0.99f);
            float bassSum = 0f;

            for (int i = 0; i < _barCount; i++)
            {
                float p0 = _bandPos[i];
                float p1 = MathF.Max(_bandPos[i + 1], p0 + 0.001f);
                float v = MathF.Max(SampleBands(bands, p0), SampleBands(bands, p1)) * gain;
                int b0 = (int)p0 + 1;
                int b1 = Math.Min((int)p1, SpectrumAnalyzer.BandCount - 1);
                for (int b = b0; b <= b1; b++)
                {
                    float bv = bands[b] * gain;
                    if (bv > v) v = bv;
                }
                if (v > 1f) v = 1f;
                float s = _smoothed[i];
                s = s * smoothing + v * (1f - smoothing);
                _smoothed[i] = s;
                float p = _peaks[i] - dt * 0.55f;
                _peaks[i] = s > p ? s : p;
            }

            for (int b = 0; b <= _bassBandEnd; b++)
            {
                bassSum += bands[b];
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
            _textFade += (1f - _textFade) * Math.Clamp(dt * 3f, 0f, 1f);

        }

        private void RebuildTextLayouts()
        {
            _layoutsDirty = false;
            float maxTextWidth = Math.Min(_width, _height) * 0.46f;
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
            _timeLayout = null; // rebuilt lazily when the second flips
            _lastTimeSecond = -1;
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

            DrawAura(session);

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

        private void DrawAura(CanvasDrawingSession session)
        {
            if (_auraBrush == null || _brushDirty)
            {
                _auraBrush?.Dispose();
                var stops = new CanvasGradientStop[3];
                Color c = _primary;
                stops[0] = new CanvasGradientStop { Position = 0f, Color = c };
                stops[1] = new CanvasGradientStop { Position = 0.55f, Color = Color.FromArgb(90, c.R, c.G, c.B) };
                stops[2] = new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0, c.R, c.G, c.B) };
                _auraBrush = new CanvasRadialGradientBrush(Device, stops)
                {
                    Center = new Vector2(_centerX, _centerY),
                    RadiusX = _outerRadius * 1.35f,
                    RadiusY = _outerRadius * 1.35f,
                };
                _brushDirty = false;
            }

            float glow = Math.Clamp(0.10f + _bass * 0.55f, 0f, 0.55f);
            _auraBrush.Opacity = glow;
            session.FillEllipse(new Vector2(_centerX, _centerY), _outerRadius * 1.3f, _outerRadius * 1.3f, _auraBrush);

            Color rim = _primary;
            rim.A = (byte)(26 * AppSettings.SpectrumOpacity);
            session.DrawCircle(new Vector2(_centerX, _centerY), _outerRadius * 0.58f, rim, 1.5f);
        }

        private void DrawProgressRing(CanvasDrawingSession session)
        {
            double duration = _services.Media.Duration.TotalSeconds;
            if (duration <= 1.0) return;
            double elapsed = _timelineBase.TotalSeconds + (_timelineWatch.IsRunning ? _timelineWatch.Elapsed.TotalSeconds : 0);
            float fraction = (float)Math.Clamp(elapsed / duration, 0.0, 1.0);

            float radius = (_albumRadius + _spectrumInner) * 0.5f;
            Color c = _primary;
            c.A = (byte)(200 * AppSettings.SpectrumOpacity);
            Color glow = c;
            glow.A = (byte)(40 * AppSettings.SpectrumOpacity);

            // Fill the preallocated polyline (one trig evaluation per vertex) and draw
            // its segments from the cache - no per-frame allocations.
            int steps = (int)(fraction * ProgressArcSegments);
            float startAngle = -MathF.PI * 0.5f;
            float step = MathF.PI * 2f / ProgressArcSegments;
            if (steps < 1) return;

            for (int i = 0; i <= steps; i++)
            {
                float a = startAngle + MathF.Min(i, steps) * step;
                _progressArc[i] = new Vector2(
                    _centerX + MathF.Cos(a) * radius,
                    _centerY + MathF.Sin(a) * radius);
            }

            for (int i = 0; i < steps; i++)
            {
                session.DrawLine(_progressArc[i], _progressArc[i + 1], glow, 5f);
            }
            for (int i = 0; i < steps; i++)
            {
                session.DrawLine(_progressArc[i], _progressArc[i + 1], c, 2f);
            }

            float tipAngle = startAngle + fraction * MathF.PI * 2f;
            var tip = new Vector2(_centerX + MathF.Cos(tipAngle) * radius, _centerY + MathF.Sin(tipAngle) * radius);
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

        /// <summary>沿频率位置在封面调色板上取渐变色，高亮处向白色提亮。</summary>
        private Color GetGradientColor(float t, float intensity)
        {
            Color[] palette = _palette;
            float scaled = Math.Clamp(t, 0f, 1f) * (palette.Length - 1);
            int index = Math.Min((int)scaled, palette.Length - 2);
            Color c = LerpColor(palette[index], palette[index + 1], scaled - index);
            return LerpColor(c, Color.FromArgb(255, 255, 255, 255), intensity * 0.35f);
        }

        private void DrawRadialSpectrum(CanvasDrawingSession session)
        {
            float spectrumRange = _outerRadius * 0.33f;
            float angleStep = MathF.PI * 2f / _barCount;
            float lineWidth = angleStep * _spectrumInner * 0.62f;
            byte baseAlpha = (byte)(255 * AppSettings.SpectrumOpacity);
            float frequencySpan = 1f / MathF.Max(_barCount - 1, 1);

            for (int i = 0; i < _barCount; i++)
            {
                float intensity = Math.Clamp(_smoothed[i], 0f, 1f);
                float angle = i * angleStep - _rotation - MathF.PI * 0.5f;
                float cos = MathF.Cos(angle);
                float sin = MathF.Sin(angle);
                var inner = new Vector2(_centerX + cos * _spectrumInner, _centerY + sin * _spectrumInner);
                var outer = new Vector2(_centerX + cos * (_spectrumInner + intensity * spectrumRange),
                                        _centerY + sin * (_spectrumInner + intensity * spectrumRange));

                Color c = GetGradientColor(i * frequencySpan, intensity);
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
            float radius = MathF.Round(_albumRadius * (1f + Math.Clamp(_bass, 0f, 1f) * 0.05f) * 2f) * 0.5f;
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
            float textY = _centerY + _outerRadius * 0.70f;

            Color textColor = _textColor;
            textColor.A = alpha;
            // 可调软阴影（DesktopLyrics 同款规则）：阴影色 = 文字色反相，
            // 强度 0-100 线性映射 0-2 并拆两层（单层透明度上限 1.0）。
            float strength = Math.Clamp(AppSettings.FontShadow, 0f, 100f) / 50f;
            float innerShadow = MathF.Min(1f, strength);
            float outerShadow = Math.Clamp(strength - 1f, 0f, 1f);
            Color shadowColor = Color.FromArgb(
                (byte)(255 * Math.Clamp(innerShadow * 0.8f, 0f, 1f)),
                (byte)(255 - textColor.R),
                (byte)(255 - textColor.G),
                (byte)(255 - textColor.B));
            Color outerShadowColor = Color.FromArgb(
                (byte)(255 * outerShadow * 0.55f),
                shadowColor.R,
                shadowColor.G,
                shadowColor.B);

            if (_titleLayout != null)
            {
                DrawTextWithShadow(session, _titleLayout,
                    new Vector2(_centerX - (float)_titleLayout.LayoutBounds.Width * 0.5f, textY),
                    textColor, shadowColor, outerShadowColor);
                textY += (float)_titleLayout.LayoutBounds.Height + 2f;
            }
            if (_artistLayout != null)
            {
                Color artistColor = textColor;
                artistColor.A = (byte)(alpha * 0.62f);
                DrawTextWithShadow(session, _artistLayout,
                    new Vector2(_centerX - (float)_artistLayout.LayoutBounds.Width * 0.5f, textY),
                    artistColor, shadowColor, outerShadowColor);
                textY += (float)_artistLayout.LayoutBounds.Height + 4f;
            }

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
                        _width * 0.5f,
                        20f);
                }
                if (_timeLayout != null)
                {
                    Color timeColor = textColor;
                    timeColor.A = (byte)(alpha * 0.45f);
                    DrawTextWithShadow(session, _timeLayout,
                        new Vector2(_centerX - (float)_timeLayout.LayoutBounds.Width * 0.5f, textY + 2f),
                        timeColor, shadowColor, outerShadowColor);
                }
            }
        }

        /// <summary>文字双层阴影 + 本体，三层都用缓存的 TextLayout，零分配。</summary>
        private static void DrawTextWithShadow(
            CanvasDrawingSession session,
            CanvasTextLayout layout,
            Vector2 position,
            Color textColor,
            Color innerShadowColor,
            Color outerShadowColor)
        {
            if (outerShadowColor.A > 0)
            {
                session.DrawTextLayout(layout, position + new Vector2(3f, 3f), outerShadowColor);
            }
            if (innerShadowColor.A > 0)
            {
                session.DrawTextLayout(layout, position + new Vector2(1.5f, 1.5f), innerShadowColor);
            }
            session.DrawTextLayout(layout, position, textColor);
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
            _auraBrush?.Dispose();
            _albumArt?.Dispose();
            _titleFormat.Dispose();
            _artistFormat.Dispose();
            _timeFormat.Dispose();
        }
    }
}
