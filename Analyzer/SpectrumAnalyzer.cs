using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Threading.Channels;

namespace WinExSpectrumTest.Analyzer
{
    public class SpectrumAnalyzer : IDisposable
    {
        private WasapiLoopbackCapture _capture;
        private readonly int _fftLength = 1024;
        private readonly int _sampleRate = 48000;
        private readonly float[] _fftBuffer;
        private readonly Complex[] _fftData;
        private readonly float[] _spectrumData;
        private bool _disposed = false;

        public event Action<float[]> SpectrumDataUpdated;

        public SpectrumAnalyzer()
        {
            _fftBuffer = new float[_fftLength];
            _fftData = new Complex[_fftLength];
            _spectrumData = new float[_fftLength / 2];
        }

        public void StartCapture()
        {
            try
            {
                _capture = new WasapiLoopbackCapture();
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"启动音频捕获失败: {ex.Message}");
            }
        }

        public void StopCapture()
        {
            _capture?.StopRecording();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (_disposed || e.BytesRecorded == 0) return;

            // 将字节转换为浮点数
            int samples = e.BytesRecorded / (4 ); 
            if (samples < _fftLength) return;

            for (int i = 0; i < _fftLength; i++)
            {
                _fftBuffer[i] = BitConverter.ToSingle(e.Buffer, i * 4);
            }
            // 准备FFT数据
            for (int i = 0; i < _fftLength; i++)
            {
                _fftData[i].X = _fftBuffer[i]; // Real part
                _fftData[i].Y = 0;             // Imaginary part
            }

            // 执行FFT
            FastFourierTransform.FFT(true, (int)Math.Log(_fftLength, 2), _fftData);

            // 计算频谱幅度
            for (int i = 0; i < _spectrumData.Length; i++)
            {
                float real = (float)_fftData[i].X;
                float imaginary = (float)_fftData[i].Y;
                float magnitude = (float)Math.Sqrt(real * real + imaginary * imaginary);
                float frequency = i * _sampleRate /_fftLength;
                float compensationFactor = GetCompensationFactor(frequency);
                _spectrumData[i] = magnitude * compensationFactor;
            }

            // 触发事件
            SpectrumDataUpdated?.Invoke(_spectrumData);
        }

        private float GetCompensationFactor(float freq)
        {
            // 定义关键频率点和对应的补偿值
            // 这个数组定义了你的补偿曲线
            float[] frequencies = { 20, 50, 100, 200, 500, 1000, 2000, 4000, 8000, 16000, 20000 };
            float[] gains = { 0.2f, 0.3f, 0.4f, 0.6f, 0.8f, 1.0f, 1.2f, 1.3f, 1.1f, 0.9f, 0.8f };
            if (freq <= frequencies[0])
            {
                return gains[0];
            }
            if (freq >= frequencies[frequencies.Length - 1])
            {
                return gains[gains.Length - 1];
            }
            // 查找当前频率所在的区间
            int i = 0;
            while (freq > frequencies[i + 1])
            {
                i++;
            }
            // 在区间内进行线性插值
            float x1 = frequencies[i];
            float y1 = gains[i];
            float x2 = frequencies[i + 1];
            float y2 = gains[i + 1];
            return y1 + (freq - x1) * ((y2 - y1) / (x2 - x1));
        }

        private void ApplyHammingWindow(float[] data, int length)
        {
            for (int i = 0; i < length; i++)
            {
                double window = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * i / (length - 1));
                data[i] *= (float)window;
            }
        }

        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"录音停止异常: {e.Exception.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _capture?.Dispose();
                _disposed = true;
            }
        }
    }
}
