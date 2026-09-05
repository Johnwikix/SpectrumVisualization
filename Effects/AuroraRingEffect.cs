using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Text;
using System;
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
        private int[] _bandStarts = [];
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
        private bool _brushDirty = true;

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
            try
            {
                if (thumbnail != null)
                {
                    using var stream = await thumbnail.OpenReadAsync();
                    bitmap = await CanvasBitmap.LoadAsync(Device, stream);

                    // Decode a tiny 8x8 copy on the CPU to average the album's primary color.
                    stream.Seek(0);
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    var transform = new BitmapTransform
                    {
                        ScaledWidth = 8,
                        ScaledHeight = 8,
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
                    for (int i = 0; i + 3 < pixels.Length; i += 4)
                    {
                        r += pixels[i];
                        g += pixels[i + 1];
                        b += pixels[i + 2];
                    }
                    int count = pixels.Length / 4;
                    primary = Color.FromArgb(255,
                        (byte)(Math.Clamp(r / count / 255f * 1.25f, 0f, 1f) * 255f),
                        (byte)(Math.Clamp(g / count / 255f * 1.25f, 0f, 1f) * 255f),
                        (byte)(Math.Clamp(b / count / 255f * 1.25f, 0f, 1f) * 255f));
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
            _bandStarts = new int[_barCount + 1];
            // Logarithmic frequency mapping: 30 Hz .. 16 kHz across the 512 linear bands.
            const float minFreq = 30f;
            const float maxFreq = 16000f;
            float nyquist = Math.Max(_services.Analyzer.SampleRate, 8000) * 0.5f;
            for (int i = 0; i <= _barCount; i++)
            {
                float freq = minFreq * MathF.Pow(maxFreq / minFreq, (float)i / _barCount);
                int band = (int)(freq / nyquist * SpectrumAnalyzer.BandCount);
                _bandStarts[i] = Math.Clamp(band, 0, SpectrumAnalyzer.BandCount - 1);
            }
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
                int start = _bandStarts[i];
                int end = Math.Max(_bandStarts[i + 1], start + 1);
                float peak = 0f;
                for (int b = start; b < end; b++)
                {
                    float v = bands[b] * gain;
                    if (v > peak) peak = v;
                }
                if (peak > 1f) peak = 1f;
                float s = _smoothed[i];
                s = s * smoothing + peak * (1f - smoothing);
                _smoothed[i] = s;
                float p = _peaks[i] - dt * 0.55f;
                _peaks[i] = s > p ? s : p;
                if (i < 32) bassSum += bands[i];
            }

            _bass = bassSum / 32f;
            _bassHistory[_bassHistoryIndex] = _bass;
            _bassHistoryIndex = (_bassHistoryIndex + 1) % BassHistorySize;
            float bassMean = 0f;
            for (int i = 0; i < BassHistorySize; i++) bassMean += _bassHistory[i];
            bassMean /= BassHistorySize;

            _beatCooldown -= dt;
            if (_bass > bassMean * 1.35f && _bass > 0.03f && _beatCooldown <= 0f)
            {
                _beatCooldown = 0.35f;
                _ringRadius[_ringCursor] = _albumRadius * 1.1f;
                _ringAlpha[_ringCursor] = 0.55f;
                _ringCursor = (_ringCursor + 1) % _ringRadius.Length;
            }

            for (int i = 0; i < _ringRadius.Length; i++)
            {
                if (_ringAlpha[i] > 0f)
                {
                    _ringRadius[i] += dt * _outerRadius * 0.9f;
                    _ringAlpha[i] -= dt * 0.9f;
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

                Color c = LerpColor(_primary, Color.FromArgb(255, 255, 255, 255), intensity * 0.65f);
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
            Color textColor = Color.FromArgb(alpha, 255, 255, 255);

            if (_titleLayout != null)
            {
                session.DrawTextLayout(_titleLayout, new Vector2(_centerX - (float)_titleLayout.LayoutBounds.Width * 0.5f, textY), textColor);
                textY += (float)_titleLayout.LayoutBounds.Height + 2f;
            }
            if (_artistLayout != null)
            {
                Color artistColor = Color.FromArgb((byte)(alpha * 0.62f), 255, 255, 255);
                session.DrawTextLayout(_artistLayout, new Vector2(_centerX - (float)_artistLayout.LayoutBounds.Width * 0.5f, textY), artistColor);
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
                    Color timeColor = Color.FromArgb((byte)(alpha * 0.45f), 255, 255, 255);
                    session.DrawTextLayout(
                        _timeLayout,
                        new Vector2(_centerX - (float)_timeLayout.LayoutBounds.Width * 0.5f, textY + 2f),
                        timeColor);
                }
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

            for (int i = 0; i < _barCount; i++)
            {
                float intensity = Math.Clamp(_smoothed[i], 0f, 1f);
                float x = i * barWidth + barWidth * 0.5f;
                float barHeight = intensity * maxHeight;
                var top = new Vector2(x, centerY - barHeight);
                var bottom = new Vector2(x, centerY + barHeight);

                Color c = LerpColor(_primary, Color.FromArgb(255, 255, 255, 255), intensity * 0.5f);
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
