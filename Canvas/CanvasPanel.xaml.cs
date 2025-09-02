using ABI.Microsoft.UI.Xaml;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest.Canvas
{
    public sealed partial class CanvasPanel : Microsoft.UI.Xaml.Controls.UserControl
    {
        private SpectrumAnalyzer _analyzer;
        private float[] _currentSpectrum;
        private float[] _smoothedSpectrum;
        private readonly int _barCount = 128;
        private readonly float _smoothingFactor = 0.9f;
        private bool _disposed = false;
        private float _rotationOffset = 0f;
        private int _middleNum = 0;
        private bool _isMiddleIncreasing = true;
        private GlobalSystemMediaTransportControlsSessionManager _sessionManager;
        private GlobalSystemMediaTransportControlsSession _currentSession;
        private IRandomAccessStreamWithContentType _albumStream;
        private CanvasDevice _device;
        private CanvasBitmap _albumArtBitmap;
        public CanvasPanel()
        {
            InitializeComponent();
            InitializeAudio();
            _ = InitializeSMTCAsync();
        }

        private void InitializeAudio()
        {
            _analyzer = new SpectrumAnalyzer();
            _analyzer.SpectrumDataUpdated += OnSpectrumDataUpdated;
            _currentSpectrum = new float[_barCount];
            _smoothedSpectrum = new float[_barCount];
            _analyzer.StartCapture();
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
                GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties = await _currentSession.TryGetMediaPropertiesAsync();
                //var playbackInfo = _currentSession.GetPlaybackInfo();
                // 获取并加载封面
                if (mediaProperties != null && mediaProperties.Thumbnail != null)
                {
                    var stream = await mediaProperties.Thumbnail.OpenReadAsync();
                    _albumArtBitmap = await CanvasBitmap.LoadAsync(_device, stream);
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
                    _currentSpectrum[i] = spectrumData[index] * 2000f;
                }
            }
        }

        private void SpectrumCanvasControl_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
        {
            _device = sender.Device;
            var session = args.DrawingSession;
            var size = sender.Size;
            _rotationOffset += 0.001f;
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
                else {
                    _middleNum += 1;
                }               
            }
            else {
                if (_middleNum <= 0)
                {
                    _isMiddleIncreasing = true;
                }
                else
                {
                    _middleNum -= 1;
                }
            }
            DrawPlainSpectrum(session, size);
            DrawRoundSpectrum(session, size);
            DrawAlbumArt(session, size);
            //DrawWaveform(session, size);
        }

        private void DrawPlainSpectrum(CanvasDrawingSession session, Windows.Foundation.Size size) 
        {
            if (_smoothedSpectrum == null) return;

            // 绘制频谱条
            float barWidth = (float)size.Width / _barCount;
            float maxHeight = (float)size.Height * 0.3f;

            for (int i = 0; i < _barCount; i++)
            {
                float x = i * barWidth;
                float height = Math.Max(Math.Min(_smoothedSpectrum[i], maxHeight), 0);
                float y = (float)size.Height - height;

                // 创建渐变色彩效果
                var color = GetSpectrumColor(height / maxHeight);

                // 绘制频谱条
                var rect = new Windows.Foundation.Rect(
                    x + 1, y,
                    barWidth - 2, height);

                session.FillRectangle(rect, color);

                // 添加发光效果
                if (height > 10)
                {
                    var glowRect = new Windows.Foundation.Rect(
                        x, y - 5,
                        barWidth, height + 10);

                    var glowColor = Color.FromArgb(30, color.R, color.G, color.B);
                    session.FillRectangle(glowRect, glowColor);
                }
            }
        }

        private void DrawRoundSpectrum(CanvasDrawingSession session, Windows.Foundation.Size size) 
        {
            if (_smoothedSpectrum == null) return;

            float centerX = (float)size.Width * 0.5f;
            float centerY = (float)size.Height * 0.5f;
            float baseRadius = Math.Min(centerX, centerY) * 0.5f + _smoothedSpectrum.Average() * 5;
            float angleStep = 2 * (float)Math.PI / _barCount;
            float angleOffset = 0.01f;
            for (int i = 0; i < _barCount; i++)
            {
                // 径向长度
                float height = Math.Max(Math.Min(_smoothedSpectrum[i] * 0.02f, 0.4f), 0);
                float currentRadius = baseRadius + (height * baseRadius);

                // 起始和结束角度
                float startAngle = i * angleStep + angleOffset - _rotationOffset;
                float endAngle = (i + 1) * angleStep - angleOffset - _rotationOffset;
                var color = GetSpectrumColorLoop(height,i);
                // 创建多边形的顶点
                var polygonPoints = new List<Vector2>();

                // 1. 添加内圆的起始点和结束点
                polygonPoints.Add(new Vector2(
                    centerX + baseRadius * (float)Math.Cos(startAngle),
                    centerY + baseRadius * (float)Math.Sin(startAngle)));

                polygonPoints.Add(new Vector2(
                    centerX + baseRadius * (float)Math.Cos(endAngle),
                    centerY + baseRadius * (float)Math.Sin(endAngle)));

                // 2. 添加外圆的结束点和起始点，注意顺序，以形成闭合的多边形
                polygonPoints.Add(new Vector2(
                    centerX + currentRadius * (float)Math.Cos(endAngle),
                    centerY + currentRadius * (float)Math.Sin(endAngle)));

                polygonPoints.Add(new Vector2(
                    centerX + currentRadius * (float)Math.Cos(startAngle),
                    centerY + currentRadius * (float)Math.Sin(startAngle)));

                // 绘制多边形，模拟频谱条
                session.FillGeometry(CanvasGeometry.CreatePolygon(session, polygonPoints.ToArray()), color);

                if (height > 0.05f)
                {
                    var glowColor = Color.FromArgb(30, color.R, color.G, color.B);
                    float glowRadius = currentRadius + 10;
                    var glowPoints = new List<Vector2>();

                    glowPoints.Add(new Vector2(
                        centerX + currentRadius * (float)Math.Cos(startAngle),
                        centerY + currentRadius * (float)Math.Sin(startAngle)));

                    glowPoints.Add(new Vector2(
                        centerX + currentRadius * (float)Math.Cos(endAngle),
                        centerY + currentRadius * (float)Math.Sin(endAngle)));

                    glowPoints.Add(new Vector2(
                        centerX + glowRadius * (float)Math.Cos(endAngle),
                        centerY + glowRadius * (float)Math.Sin(endAngle)));

                    glowPoints.Add(new Vector2(
                        centerX + glowRadius * (float)Math.Cos(startAngle),
                        centerY + glowRadius * (float)Math.Sin(startAngle)));

                    session.FillGeometry(CanvasGeometry.CreatePolygon(session, glowPoints.ToArray()), glowColor);
                }
            }
        }

        private void SpectrumCanvasControl_Update(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedUpdateEventArgs args)
        {
            if (_currentSpectrum == null) return;
            // 平滑处理频谱数据
            for (int i = 0; i < _barCount; i++)
            {
                _smoothedSpectrum[i] = _smoothedSpectrum[i] * _smoothingFactor +
                                     _currentSpectrum[i] * (1 - _smoothingFactor);
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

        private async void DrawAlbumArt(CanvasDrawingSession session, Windows.Foundation.Size size)
        {
            try
            {                
                if (_albumArtBitmap == null)
                {
                    return;
                }
                float centerX = (float)size.Width * 0.5f;
                float centerY = (float)size.Height * 0.5f;
                float baseRadius = Math.Min(centerX, centerY) * 0.5f + _smoothedSpectrum.Average() * 5;
                var circleGeometry = CanvasGeometry.CreateCircle(session, centerX, centerY, baseRadius);

                // 计算图片缩放和位置，使其居中并覆盖圆形区域
                float imageAspectRatio = (float)_albumArtBitmap.SizeInPixels.Width / _albumArtBitmap.SizeInPixels.Height;
                float targetWidth = baseRadius * 2;
                float targetHeight = baseRadius * 2;

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
                drawX = centerX - drawWidth / 2;
                drawY = centerY - drawHeight / 2;
                session.Transform = Matrix3x2.CreateRotation(-_rotationOffset, new Vector2(centerX, centerY)) * session.Transform;
                using (session.CreateLayer(1.0f, circleGeometry))
                {
                    session.DrawImage(_albumArtBitmap, new Rect(drawX, drawY, drawWidth, drawHeight));
                }

            }
            catch (Exception) {
            }                
        }

        private Color GetSpectrumColorLoop(float intensity, int i = 0)
        {
            float coe = 256 / _barCount;
            return Color.FromArgb(128, (byte)(i * coe), (byte)_middleNum, (byte)(255 - i * coe));
        }

        private Color GetSpectrumColor(float intensity)
        {
            // 根据强度创建彩虹色彩效果
            if (intensity < 0.1f)
                return Color.FromArgb(128, 0, 100, 255); // 蓝色
            else if (intensity < 0.2f)
                return Color.FromArgb(128, 0, 255, 200); // 青色
            else if (intensity < 0.3f)
                return Color.FromArgb(128, 100, 255, 0); // 绿色
            else if (intensity < 0.4f)
                return Color.FromArgb(128, 255, 200, 0); // 黄色
            else
                return Color.FromArgb(128, 255, 100, 0);
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
