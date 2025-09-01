using Microsoft.Graphics.Canvas;
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
        private readonly float _smoothingFactor = 0.8f;
        private bool _disposed = false;
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
                    _currentSpectrum[i] = spectrumData[index] * 5000f;
                }
            }
        }

        private void SpectrumCanvasControl_Draw(Microsoft.Graphics.Canvas.UI.Xaml.ICanvasAnimatedControl sender, Microsoft.Graphics.Canvas.UI.Xaml.CanvasAnimatedDrawEventArgs args)
        {
            var session = args.DrawingSession;
            var size = sender.Size;

            if (_smoothedSpectrum == null) return;

            // 绘制频谱条
            float barWidth = (float)size.Width / _barCount;
            float maxHeight = (float)size.Height * 0.4f;

            for (int i = 0; i < _barCount; i++)
            {
                float x = i * barWidth;
                float height = Math.Max(Math.Min(_smoothedSpectrum[i], maxHeight),0);
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
            DrawWaveform(session, size);
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
            float width = (float)size.Width;
            float centerY = (float)size.Height * 0.5f;

            for (int i = 0; i < _barCount; i++)
            {
                float x = (float)i / (_barCount - 1) * width;
                float y = centerY - (_smoothedSpectrum[i] * 0.3f);
                points[i] = new Vector2(x, y);
            }            
            for (int i = 0; i < points.Length - 1; i++)
            {
                session.DrawLine(points[i], points[i + 1], Color.FromArgb(128, 0, 255, 200), 2f);
            }
        }

        private Color GetSpectrumColor(float intensity)
        {
            // 根据强度创建彩虹色彩效果
            if (intensity < 0.2f)
                return Color.FromArgb(128, 0, 100, 255); // 蓝色
            else if (intensity < 0.4f)
                return Color.FromArgb(128, 0, 255, 200); // 青色
            else if (intensity < 0.6f)
                return Color.FromArgb(128, 100, 255, 0); // 绿色
            else if (intensity < 0.8f)
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
