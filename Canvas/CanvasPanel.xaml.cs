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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using WinExSpectrumTest.Analyzer;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinExSpectrumTest.Canvas
{
    public sealed partial class CanvasPanel : UserControl
    {
        private SpectrumAnalyzer _analyzer;
        private float[] _currentSpectrum;
        private float[] _smoothedSpectrum;
        private readonly int _barCount = 128;
        private readonly float _smoothingFactor = 0.5f;
        private bool _disposed = false;
        private float _rotationOffset = 0f;
        public CanvasPanel()
        {
            InitializeComponent();
            InitializeAudio();
        }

        private void InitializeAudio()
        {
            _analyzer = new SpectrumAnalyzer();
            _analyzer.SpectrumDataUpdated += OnSpectrumDataUpdated;
            _currentSpectrum = new float[_barCount];
            _smoothedSpectrum = new float[_barCount];
            _analyzer.StartCapture();
        }

        private void OnSpectrumDataUpdated(float[] spectrumData)
        {
            for (int i = 0; i < _barCount; i++)
            {
                int index = (int)((float)i / _barCount * spectrumData.Length);
                if (index < spectrumData.Length)
                {
                    _currentSpectrum[i] = spectrumData[index] * 2500f;
                }
            }
        }

        private void SpectrumCanvasControl_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
        {
            var session = args.DrawingSession;
            var size = sender.Size;
            DrawPlainSpectrum(session, size);
            DrawRoundSpectrum(session, size);
            DrawWaveform(session, size);
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
            float baseRadius = Math.Min(centerX, centerY) * 0.5f + _smoothedSpectrum.Average() * 10;
            float angleStep = 2 * (float)Math.PI / _barCount;
            float angleOffset = 0.01f;
            for (int i = 0; i < _barCount; i++)
            {
                // 频谱条的高度现在代表径向的长度
                float height = Math.Max(Math.Min(_smoothedSpectrum[i] * 0.025f, 0.5f), 0);
                float currentRadius = baseRadius + (height * baseRadius);

                // 计算扇形的起始和结束角度
                float startAngle = i * angleStep + angleOffset - _rotationOffset;
                float endAngle = (i + 1) * angleStep - angleOffset - _rotationOffset;

                // 根据频谱高度获取颜色
                var color = GetSpectrumColor(height);

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

                // --- 增加发光效果 ---
                if (height > 0.05f)
                {
                    var glowColor = Color.FromArgb(30, color.R, color.G, color.B);

                    // 重新计算外圆半径，增加发光效果的宽度
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
            _rotationOffset += 0.001f;
            if (_rotationOffset >= 2 * (float)Math.PI)
            {
                _rotationOffset -= 0f;
            }
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
            //if (_smoothedSpectrum == null) return;

            //var points = new Vector2[_barCount];
            //float width = (float)size.Width;
            //float centerY = (float)size.Height * 0.5f;

            //for (int i = 0; i < _barCount; i++)
            //{
            //    float x = (float)i / (_barCount - 1) * width;
            //    float y = centerY - (_smoothedSpectrum[i] * 0.3f);
            //    points[i] = new Vector2(x, y);
            //}            
            //for (int i = 0; i < points.Length - 1; i++)
            //{
            //    session.DrawLine(points[i], points[i + 1], Color.FromArgb(128, 0, 255, 200), 2f);
            //}
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
                return Color.FromArgb(128, 255, 100, 100); // 红色
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
