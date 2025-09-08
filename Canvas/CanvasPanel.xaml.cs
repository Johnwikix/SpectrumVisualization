using ABI.Microsoft.UI.Xaml;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Windows.UI;
using WinExSpectrumTest.Analyzer;
using WinExSpectrumTest.Model;
using WinRT;
using ZLinq;
using static Vanara.PInvoke.Kernel32;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest.Canvas
{
    public sealed partial class CanvasPanel : Microsoft.UI.Xaml.Controls.UserControl
    {
        private SpectrumAnalyzer _analyzer;
        private readonly float[] _currentSpectrum;
        private readonly float[] _smoothedSpectrum;
        private readonly int _barCount = 128;
        //private readonly float _smoothingFactor = 0.9f;
        private bool _disposed = false;
        private float _rotationOffset = 0f;
        private int _middleNum = 0;
        private bool _isMiddleIncreasing = true;
        private GlobalSystemMediaTransportControlsSessionManager _sessionManager;
        private GlobalSystemMediaTransportControlsSession _currentSession;
        private GlobalSystemMediaTransportControlsSessionMediaProperties _mediaProperties;
        private IRandomAccessStreamWithContentType _thumbnail;
        private string _title;
        private string _artist;
        private CanvasTextLayout _titleLayout;
        private CanvasTextLayout _artistLayout;
        private float _lastMaxTextWidth = 0f;
        private CanvasDevice _device;
        private CanvasBitmap _albumArtBitmap;
        private float _smoothAverage = 0f;
        private float _currentAverage = 0f;
        private CanvasTextFormat _titleTextFormat;
        private CanvasTextFormat _artistTextFormat;
        private float _centerX = 0f;
        private float _centerY = 0f;
        private Vector2[][] _polygonPointsPool;
        private Vector2[][] _glowPointsPool;
        private CanvasGeometry _circleGeometry;
        private float _baseRadius = 0f;
        private float _lastBaseRadius = 0f;
        public CanvasPanel()
        {
            _currentSpectrum = new float[_barCount];
            _smoothedSpectrum = new float[_barCount];
            InitializeComponent();
            InitializeAudio();
            InitializeText();
            InitializeVectors();   
            _ = InitializeSMTCAsync();
        }

        private void InitializeVectors()
        {
            _polygonPointsPool = new Vector2[_barCount][];
            _glowPointsPool = new Vector2[_barCount][];
            for (int i = 0; i < _barCount; i++)
            {
                _polygonPointsPool[i] = new Vector2[4];
                _glowPointsPool[i] = new Vector2[4];
            }
        }

        private void InitializeAudio()
        {
            _analyzer = new SpectrumAnalyzer();
            _analyzer.SpectrumDataUpdated += OnSpectrumDataUpdated;            
            _analyzer.StartCapture();
        }

        private void InitializeText()
        {
            _titleTextFormat = new()
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
                TrimmingSign = CanvasTrimmingSign.Ellipsis,
                TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            };
            _artistTextFormat = new()
            {
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
                TrimmingSign = CanvasTrimmingSign.Ellipsis,
                TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
            };
        }

        public async Task InitializeSMTCAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.SessionsChanged += OnSessionsChanged;
                    await UpdateCurrentSession();
                }
            }
            catch (Exception)
            {
            }
        }

        private async void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
        {
            await UpdateCurrentSession();
        }

        private async Task UpdateCurrentSession()
        {
            try
            {
                if (_currentSession != null)
                {
                    _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                }
                var sessions = _sessionManager.GetSessions();
                _currentSession = sessions.Count > 0 ? sessions[0] : null;
                if (_currentSession != null)
                {
                    _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                    _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                    await GetCurrentMediaInfo();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新会话失败: {ex.Message}");
            }
        }

        private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            await GetCurrentMediaInfo();
        }

        private async void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            await GetCurrentMediaInfo();
        }

        public async Task GetCurrentMediaInfo()
        {
            if (_currentSession == null)
            {
                _albumArtBitmap = null;
                return;
            }
            try
            {
                _mediaProperties = await _currentSession.TryGetMediaPropertiesAsync();
                _title = _mediaProperties?.Title;
                _artist = _mediaProperties?.Artist;
                // 获取并加载封面
                if (_mediaProperties != null && _mediaProperties.Thumbnail != null)
                { 
                    DispatcherQueue.TryEnqueue(async () => {
                        try
                        {
                            _thumbnail = await _mediaProperties.Thumbnail.OpenReadAsync();
                            _albumArtBitmap = await CanvasBitmap.LoadAsync(_device, _thumbnail);
                        }
                        catch (Exception) {
                            try
                            {
                                _thumbnail = await _mediaProperties.Thumbnail.OpenReadAsync();
                                _albumArtBitmap = await CanvasBitmap.LoadAsync(_device, _thumbnail);
                            }
                            catch (Exception) {
                            }                            
                        }                        
                    });                    
                }
                else
                {
                    _albumArtBitmap = null;
                }
            }
            catch (Exception)
            {
                _albumArtBitmap = null;
            }
        }


        private void OnSpectrumDataUpdated(float[] spectrumData)
        {
            for (int i = 0; i < _barCount; i++)
            {
                int index = (int)((float)i / _barCount * spectrumData.Length);
                if (index < spectrumData.Length)
                {
                    _currentSpectrum[i] = spectrumData[index] * 250f * AppSettings.Sensitivity;
                }
            }
        }

        private void SpectrumCanvasControl_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
        {
            try
            {
                _device = sender.Device;
                var session = args.DrawingSession;
                var size = sender.Size;
                _currentAverage = _currentSpectrum.AsValueEnumerable().Average();
                _smoothAverage = _smoothedSpectrum.AsValueEnumerable().Average();
                if (_currentAverage > 0)
                {
                    _rotationOffset += 0.0001f * AppSettings.RotationSpeed;
                    if (_rotationOffset >= 2 * (float)Math.PI)
                    {
                        _rotationOffset = 0f;
                    }
                    if (_isMiddleIncreasing)
                    {
                        if (_middleNum >= 255)
                        {
                            _isMiddleIncreasing = false;
                        }
                        else
                        {
                            _middleNum += 1;
                        }
                    }
                    else
                    {
                        if (_middleNum <= 0)
                        {
                            _isMiddleIncreasing = true;
                        }
                        else
                        {
                            _middleNum -= 1;
                        }
                    }
                    _centerX = (float)size.Width * 0.5f;
                    _centerY = (float)size.Height * 0.5f;
                    _baseRadius = Math.Min(_centerX, _centerY) * 0.5f;
                }
                if (AppSettings.IsDrawRoundSpectrum)
                {
                    DrawRoundSpectrum(session, size);
                    DrawAlbumArt(session, size);
                    DrawTitleAndArtist(session, size);
                }
                if (AppSettings.IsDrawPlainSpectrum)
                {
                    DrawPlainSpectrum(session, size);
                }                
            }
            catch (Exception) { }            
        }

        private void DrawPlainSpectrum(CanvasDrawingSession session, Windows.Foundation.Size size) 
        {
            try {
            } catch (Exception) { }
            if (_smoothedSpectrum == null) return;

            // 绘制频谱条
            float barWidth = (float)size.Width / _barCount;
            float maxHeight = (float)size.Height * 0.3f;
            if (barWidth <= 2) return;
            for (int i = 0; i < _barCount; i++)
            {
                float x = i * barWidth;
                float height = Math.Max(Math.Min(_smoothedSpectrum[i], maxHeight), 0);
                float y = (float)size.Height/2 - height;

                // 创建渐变色彩效果
                var color = GetSpectrumColorLoop(height / maxHeight,i);

                // 绘制频谱条
                var rectUp = new Windows.Foundation.Rect(
                    x + 1, y,
                    barWidth - 2, height);

                var rectDown = new Windows.Foundation.Rect(
                    x + 1, (float)size.Height/2,
                    barWidth - 2, height);

                session.FillRectangle(rectUp, color);
                session.FillRectangle(rectDown, color);

                // 添加发光效果
                if (height > 10)
                {
                    var glowRectUp = new Windows.Foundation.Rect(
                        x, y - 5,
                        barWidth, height + 10);
                    var glowRectDown = new Windows.Foundation.Rect(
                        x, (float)size.Height/2 - 5,
                        barWidth, height + 10);

                    var glowColor = Color.FromArgb((byte)(32*AppSettings.SpectrumOpacity), color.R, color.G, color.B);
                    session.FillRectangle(glowRectUp, glowColor);
                    session.FillRectangle(glowRectDown, glowColor);
                }
            }
        }

        private void DrawRoundSpectrum(CanvasDrawingSession session, Windows.Foundation.Size size) 
        {
            if (_smoothedSpectrum == null) return;
            float angleStep = 2 * (float)Math.PI / _barCount;
            float angleOffset = 0.01f;

            // 计算线宽，基于角度步长
            float lineWidth = angleStep * _baseRadius * 0.8f;

            for (int i = 0; i < _barCount; i++)
            {
                // 径向长度
                float height = Math.Max(Math.Min(_smoothedSpectrum[i] * 0.02f, 0.8f), 0);
                float currentRadius = _baseRadius + _smoothAverage + (height * _baseRadius);
                // 起始和结束角度
                float startAngle = i * angleStep + angleOffset - _rotationOffset;
                float endAngle = (i + 1) * angleStep - angleOffset - _rotationOffset;
                var color = GetSpectrumColorLoop(height, i);

                // 计算中心角度，用一条线代替四边形
                float centerAngle = (startAngle + endAngle) / 2;
                Vector2 innerPoint = new Vector2(
                    _centerX + _baseRadius * (float)Math.Cos(centerAngle),
                    _centerY + _baseRadius * (float)Math.Sin(centerAngle)
                );
                Vector2 outerPoint = new Vector2(
                    _centerX + currentRadius * (float)Math.Cos(centerAngle),
                    _centerY + currentRadius * (float)Math.Sin(centerAngle)
                );

                // 用粗线替代多边形
                session.DrawLine(innerPoint, outerPoint, color, lineWidth);

                // 发光效果
                var glowColor = Color.FromArgb((byte)(32 * AppSettings.SpectrumOpacity), color.R, color.G, color.B);
                float glowRadius = (float)(currentRadius * 1.05);
                Vector2 glowPoint = new Vector2(
                    _centerX + glowRadius * (float)Math.Cos(centerAngle),
                    _centerY + glowRadius * (float)Math.Sin(centerAngle)
                );

                session.DrawLine(outerPoint, glowPoint, glowColor, lineWidth * 1.2f);
            }
        }

        private void SpectrumCanvasControl_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
        {
            if (_currentSpectrum == null) return;
            // 平滑处理频谱数据
            for (int i = 0; i < _barCount; i++)
            {
                _smoothedSpectrum[i] = _smoothedSpectrum[i] * AppSettings.SmoothingFactor +
                                     _currentSpectrum[i] * (1 - AppSettings.SmoothingFactor);
            }
        }
        // 绘制波形线条
        private void DrawWaveform(CanvasDrawingSession session, Windows.Foundation.Size size)
        {
            if (_smoothedSpectrum == null) return;            
            var points = new Vector2[_barCount];
            float centerX = (float)size.Width * 0.5f;
            float centerY = (float)size.Height * 0.5f;
            Vector2 center = new Vector2(centerX, centerY);            
            // 计算基础半径，确保圆形波形图在画布内
            float baseRadius = Math.Min(centerX, centerY) * 0.6f;

            for (int i = 0; i < _barCount; i++)
            {
                float angle = (float)i / (_barCount - 1) * 2 * (float)Math.PI - _rotationOffset;
                float radius = baseRadius + _smoothedSpectrum.Average() * 10 + _smoothedSpectrum[i] * 0.5f;
                // 将极坐标转换为笛卡尔坐标
                float x = centerX + radius * (float)Math.Cos(angle);
                float y = centerY + radius * (float)Math.Sin(angle);
                points[i] = new Vector2(x, y);
            }

            // 绘制圆心折线图
            for (int i = 0; i < points.Length - 1; i++)
            {
                session.DrawLine(points[i], points[i + 1], Color.FromArgb(128, 0, 255, 200), 2f);
            }

            // 将最后一个点与第一个点连接，形成闭合图形
            if (_barCount > 1)
            {
                session.DrawLine(points[points.Length - 1], points[0], Color.FromArgb(128, 0, 255, 200), 2f);
            }
        }

        private void DrawAlbumArt(CanvasDrawingSession session, Windows.Foundation.Size size)
        {
            try
            {                
                if (_albumArtBitmap == null)
                {
                    return;
                }
                if (_circleGeometry == null ||Math.Abs(_baseRadius - _lastBaseRadius) > float.Epsilon) {
                    _circleGeometry = CanvasGeometry.CreateCircle(session, _centerX, _centerY, _baseRadius);
                    _lastBaseRadius = _baseRadius;
                }
                

                // 计算图片缩放和位置，使其居中并覆盖圆形区域
                float imageAspectRatio = (float)_albumArtBitmap.SizeInPixels.Width / _albumArtBitmap.SizeInPixels.Height;
                float targetWidth = _baseRadius * 2;
                float targetHeight = _baseRadius * 2;

                float drawX, drawY, drawWidth, drawHeight;

                if (imageAspectRatio > 1)
                {
                    drawHeight = targetHeight;
                    drawWidth = drawHeight * imageAspectRatio;
                }
                else 
                {
                    drawWidth = targetWidth;
                    drawHeight = drawWidth / imageAspectRatio;
                }
                drawX = _centerX - drawWidth / 2;
                drawY = _centerY - drawHeight / 2;
                session.Transform = Matrix3x2.CreateRotation(-_rotationOffset, new Vector2(_centerX, _centerY)) * session.Transform;
                if (_circleGeometry == null || Math.Abs(_baseRadius - _lastBaseRadius) > float.Epsilon)
                {
                    _circleGeometry = CanvasGeometry.CreateCircle(session, _centerX, _centerY, _baseRadius);
                    _lastBaseRadius = _baseRadius;
                }
                using (session.CreateLayer(1.0f, _circleGeometry))
                {
                    session.DrawImage(
                        _albumArtBitmap,
                        new Rect(drawX, drawY, drawWidth, drawHeight),
                        new Rect(0, 0, _albumArtBitmap.SizeInPixels.Width, _albumArtBitmap.SizeInPixels.Height),
                        AppSettings.CoverOpacity,
                        CanvasImageInterpolation.HighQualityCubic);
                }
            }
            catch (Exception) {
            }                
        }

        private void DrawTitleAndArtist(CanvasDrawingSession session, Windows.Foundation.Size size)
        {
            if (_albumArtBitmap == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(_title) && string.IsNullOrEmpty(_artist)) return;
            float maxTextWidth = Math.Min((float)size.Width, (float)size.Height) * 0.4f;
            float baseFontSize = 18f;
            float newFontSize = baseFontSize * (maxTextWidth / 200f);
            float centerX = _centerX - maxTextWidth/2;
            float centerY = _centerY - newFontSize;
            _titleTextFormat.FontSize = newFontSize;
            _artistTextFormat.FontSize = newFontSize * 0.9f;
            if (_titleLayout == null || Math.Abs(maxTextWidth - _lastMaxTextWidth) > float.Epsilon)
            {
                // 销毁旧的布局对象以释放资源
                _titleLayout?.Dispose();
                _artistLayout?.Dispose();

                _titleTextFormat.FontSize = newFontSize;
                _artistTextFormat.FontSize = newFontSize * 0.9f;

                // 创建新的布局对象
                _titleLayout = new CanvasTextLayout(
                    _device, _title ?? string.Empty,
                    _titleTextFormat, maxTextWidth, 20
                );
                _artistLayout = new CanvasTextLayout(
                    _device, _artist ?? string.Empty,
                    _artistTextFormat, maxTextWidth, 16
                );

                // 更新上次的宽度值
                _lastMaxTextWidth = maxTextWidth;
            }
            session.DrawTextLayout(
                _titleLayout,
                new Vector2(centerX, centerY),
                Color.FromArgb((byte)(255 * AppSettings.FontOpacity), 255, 255, 255)
                );
            session.DrawTextLayout(
                _artistLayout,
                new Vector2(centerX, centerY + (float)_titleLayout.LayoutBounds.Height),
                Color.FromArgb((byte)(255 * AppSettings.FontOpacity), 255, 255, 255));
        }

        private Color GetSpectrumColorLoop(float intensity, int i = 0)
        {
            float coe = 2 * 256 / _barCount;
            return Color.FromArgb((byte)(255 * AppSettings.SpectrumOpacity), (byte)Math.Abs(i * coe-255), (byte)_middleNum, (byte)Math.Abs(255 - i * coe));
        }

        private Color GetSpectrumColor(float intensity)
        {
            // 根据强度创建彩虹色彩效果
            if (intensity < 0.1f)
                return Color.FromArgb((byte)(255 * AppSettings.SpectrumOpacity), 0, 100, 255); // 蓝色
            else if (intensity < 0.2f)
                return Color.FromArgb((byte)(255 * AppSettings.SpectrumOpacity), 0, 255, 200); // 青色
            else if (intensity < 0.3f)
                return Color.FromArgb((byte)(255 * AppSettings.SpectrumOpacity), 100, 255, 0); // 绿色
            else if (intensity < 0.4f)
                return Color.FromArgb((byte)(255 * AppSettings.SpectrumOpacity), 255, 200, 0); // 黄色
            else
                return Color.FromArgb((byte)(255 * AppSettings.SpectrumOpacity), 255, 100, 0);
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                _analyzer?.Dispose();
                _disposed = true;
            }
        }

    }
}
