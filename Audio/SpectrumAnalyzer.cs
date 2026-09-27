using NAudio.Dsp;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

// Keep the existing DataAvailable API while explicitly selecting event-driven loopback.
#pragma warning disable CS0618

namespace WinExSpectrumTest.Audio
{
    /// <summary>
    /// Feature vector layout inside the analyzer's published float buffer.
    /// </summary>
    public static class FeatureIndex
    {
        public const int SubBass = 0;
        public const int Bass = 1;
        public const int LowMid = 2;
        public const int Mid = 3;
        public const int HighMid = 4;
        public const int Presence = 5;
        public const int Brilliance = 6;
        public const int Air = 7;
        public const int Warmth = 8;
        public const int Brightness = 9;
        public const int Sharpness = 10;
        public const int Smoothness = 11;
        public const int Density = 12;
        public const int SpectralCentroid = 13;
        public const int Energy = 14;
        public const int Count = 15;
    }

    /// <summary>
    /// WASAPI loopback spectrum analyzer built on NAudio 3.0.
    /// Replaces the previous ManagedBass implementation: captures the render mix,
    /// runs a windowed real FFT and publishes 512 linear frequency bands plus the
    /// timbral feature vector consumed by the visualizer effects.
    /// All hot-path buffers are preallocated; the capture thread only ever mutates
    /// existing state and publishes by reference swap, so neither side allocates.
    /// </summary>
    public sealed class SpectrumAnalyzer : IDisposable
    {
        public const int BandCount = 512;
        // 4096 点：WASAPI 混音格式常见 48/96/192kHz，2048 点在 192kHz 下仅 93.75Hz/箱，
        // 低频对数分带（几十 Hz 一根条）会多条共用一个 FFT 箱。4096 提高一倍分辨率。
        public const int FftSize = 4096;
        public const int SpectrumLength = FftSize / 2 + 1;

        private const float SmoothingRate = 0.15f;

        // Trigger configuration (mirrors the "Pulse"/"Meteor" detectors of the ported effect).
        private const int PulseBandStart = 0;
        private const int PulseBandEnd = 12;
        private const float PulseSensitivity = 0.22f;
        private const float PulseCooldown = 45f;
        private const float PulseStrengthScale = 0.25f;

        private const int MeteorBandStart = 92;
        private const int MeteorBandEnd = 340;
        private const float MeteorSensitivity = 0.4f;
        private const float MeteorCooldown = 180f;
        private const float MeteorStrengthScale = 0.5f;

        private const int FluxHistorySize = 40;

        // The stock loopback constructor polls a 100 ms buffer at half its duration.
        // It drains several packets in a burst, so ~100 FFT/s can still leave the
        // renderer holding the same features for 50-60 ms. Wake on device events.
        private sealed class RealtimeLoopbackCapture : WasapiCapture
        {
            public RealtimeLoopbackCapture()
                : base(WasapiLoopbackCapture.GetDefaultLoopbackCaptureDevice(),
                    useEventSync: true, audioBufferMillisecondsLength: 20)
            {
            }

            protected override AudioClientStreamFlags GetAudioClientStreamFlags()
                => base.GetAudioClientStreamFlags() | AudioClientStreamFlags.Loopback;
        }

        private RealtimeLoopbackCapture _capture;
        private readonly FftProcessor _fft = new(FftSize, FftWindowType.Hann);
        private readonly Complex[] _spectrum = new Complex[SpectrumLength];
        private readonly Complex[] _spectrumL = new Complex[SpectrumLength];
        private readonly Complex[] _spectrumR = new Complex[SpectrumLength];
        private readonly float[] _mono = new float[FftSize];
        private readonly float[] _left = new float[FftSize];
        private readonly float[] _right = new float[FftSize];
        // 双声道 ring：写入时按声道分离（ch0→L, ch1→R），mono 在展开时平均——
        // mono 频谱与旧行为完全一致，features/节拍检测不受立体声改造影响。
        private readonly float[] _ringL = new float[FftSize];
        private readonly float[] _ringR = new float[FftSize];
        private int _ringFilled;
        private int _ringPos;

        // Double buffered publications (reference swaps are atomic).
        private readonly float[] _bandsA = new float[BandCount];
        private readonly float[] _bandsB = new float[BandCount];
        private float[] _bandsFront;
        private float[] _bandsBack;
        private readonly float[] _bandsLA = new float[BandCount];
        private readonly float[] _bandsLB = new float[BandCount];
        private float[] _bandsLFront;
        private float[] _bandsLBack;
        private readonly float[] _bandsRA = new float[BandCount];
        private readonly float[] _bandsRB = new float[BandCount];
        private float[] _bandsRFront;
        private float[] _bandsRBack;
        private readonly float[] _prevBands = new float[BandCount];

        private readonly float[] _featuresA = new float[FeatureIndex.Count];
        private readonly float[] _featuresB = new float[FeatureIndex.Count];
        private float[] _featuresFront;
        private float[] _featuresBack;

        // Flux history / beat detection state (one set per trigger).
        private readonly float[] _pulseFluxHistory = new float[FluxHistorySize];
        private int _pulseFluxIndex;
        private float _pulseSmoothedFlux;
        private float _pulsePrevSmoothedFlux;
        private float _pulseBeatHold;

        private readonly float[] _meteorFluxHistory = new float[FluxHistorySize];
        private int _meteorFluxIndex;
        private float _meteorSmoothedFlux;
        private float _meteorPrevSmoothedFlux;
        private float _meteorBeatHold;

        private float _prevBrightness;

        private int _pulseTriggerCount;
        private float _pulseTriggerStrength;
        private int _meteorTriggerCount;
        private float _meteorTriggerStrength;

        private long _publicationCount;
        public long PublicationCount => Volatile.Read(ref _publicationCount);

        private bool _disposed;
        private int _sampleRate;

        /// <summary>Overall gain applied to raw FFT magnitudes (user "sensitivity").</summary>
        public float InputGain = 8.0f;

        /// <summary>Actual loopback capture sample rate in Hz.</summary>
        public int SampleRate => Volatile.Read(ref _sampleRate);
        public event Action? SampleRateChanged;

        public SpectrumAnalyzer()
        {
            _bandsFront = _bandsA;
            _bandsBack = _bandsB;
            _bandsLFront = _bandsLA;
            _bandsLBack = _bandsLB;
            _bandsRFront = _bandsRA;
            _bandsRBack = _bandsRB;
            _featuresFront = _featuresA;
            _featuresBack = _featuresB;

            _capture = new RealtimeLoopbackCapture();
            _sampleRate = _capture.WaveFormat.SampleRate;
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            try
            {
                _capture.StartRecording();
            }
            catch (Exception ex)
            {
                // No render device (or denied) - run silent with an empty spectrum
                // instead of taking the whole app down at startup.
                System.Diagnostics.Debug.WriteLine("WASAPI loopback start failed: " + ex.Message);
            }
        }

        /// <summary>The newest 512-band spectrum (bands rise linearly from 0 Hz to Nyquist).</summary>
        public ReadOnlySpan<float> LatestBands => _bandsFront;

        /// <summary>左声道（ch0）频段，镜像频谱布局的左半圆数据源。</summary>
        public ReadOnlySpan<float> LatestBandsLeft => _bandsLFront;

        /// <summary>右声道（ch1）频段，镜像频谱布局的右半圆数据源。</summary>
        public ReadOnlySpan<float> LatestBandsRight => _bandsRFront;

        /// <summary>The newest timbral feature vector (see <see cref="FeatureIndex"/>).</summary>
        public ReadOnlySpan<float> LatestFeatures => _featuresFront;

        /// <summary>Monotonic counter incremented whenever a bass pulse (ripple) is detected.</summary>
        public int PulseTriggerCount => Volatile.Read(ref _pulseTriggerCount);

        /// <summary>Strength of the last detected bass pulse.</summary>
        public float PulseTriggerStrength => Volatile.Read(ref _pulseTriggerStrength);

        /// <summary>Monotonic counter incremented whenever a high-frequency burst (meteor) is detected.</summary>
        public int MeteorTriggerCount => Volatile.Read(ref _meteorTriggerCount);

        /// <summary>Strength of the last detected high-frequency burst.</summary>
        public float MeteorTriggerStrength => Volatile.Read(ref _meteorTriggerStrength);

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                ProcessChunk(e.BufferSpan, e.BytesRecorded);
            }
            catch (Exception)
            {
                // Never let the audio thread die from transient decode issues.
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (_disposed) return;
            App.WriteCrashLog("Audio", $"loopback stopped: {e.Exception?.Message ?? "no error"}; recreating capture", e.Exception);
            // 默认设备切换/独占接管（如 HQPlayer 独占 WASAPI）会让回环采集停掉，
            // 旧实例往往无法原地重启——后台重建捕获实例并带退避重试，
            // 独占释放后自动恢复。备份频谱清零由 ring 停更自然产生。
            _ = Task.Run(RecreateCaptureLoop);
        }

        private int _recreating;

        private async Task RecreateCaptureLoop()
        {
            // 重入保护：设备反复切换时可能同时触发多个恢复循环
            if (Interlocked.CompareExchange(ref _recreating, 1, 0) != 0) return;
            try
            {
                await RecreateCaptureLoopCore();
            }
            finally
            {
                Volatile.Write(ref _recreating, 0);
            }
        }

        private async Task RecreateCaptureLoopCore()
        {
            // 持续恢复直到成功：切歌可能连续触发设备重新协商（HQPlayer 换采样率），
            // 有限重试会在连续切换下耗尽而永久静默。退避 1s→3s 封顶，独占释放后即恢复。
            int attempt = 0;
            while (!_disposed)
            {
                try
                {
                    attempt++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(attempt, 3))).ConfigureAwait(false);
                    if (_disposed) return;

                    RealtimeLoopbackCapture? old = null;
                    RealtimeLoopbackCapture capture = new();
                    bool started = false;
                    try
                    {
                        capture.DataAvailable += OnDataAvailable;
                        capture.RecordingStopped += OnRecordingStopped;
                        capture.StartRecording();
                        started = true;
                    }
                    catch (Exception)
                    {
                        capture.Dispose();
                    }
                    if (!started) continue;

                    old = Interlocked.Exchange(ref _capture, capture);
                    Volatile.Write(ref _sampleRate, capture.WaveFormat.SampleRate);
                    SampleRateChanged?.Invoke();
                    if (old != null)
                    {
                        old.DataAvailable -= OnDataAvailable;
                        old.RecordingStopped -= OnRecordingStopped;
                        try
                        {
                            old.Dispose();
                        }
                        catch (Exception)
                        {
                        }
                    }
                    App.WriteCrashLog("Audio", $"loopback recreated on attempt {attempt}", null);
                    return;
                }
                catch (Exception ex)
                {
                    App.WriteCrashLog("Audio", $"recreate attempt {attempt} failed", ex);
                }
            }
        }

        private void ProcessChunk(ReadOnlySpan<byte> raw, int bytesRecorded)
        {
            if (bytesRecorded <= 0) return;
            ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(raw.Slice(0, bytesRecorded));
            int channels = Math.Max(1, _capture.WaveFormat.Channels);
            int frames = samples.Length / channels;
            if (frames <= 0) return;

            if (frames >= FftSize)
            {
                AppendStereo(samples.Slice((frames - FftSize) * channels), channels, FftSize, 0);
                _ringFilled = FftSize;
                _ringPos = 0;
            }
            else
            {
                int tail = Math.Min(frames, FftSize - _ringPos);
                AppendStereo(samples, channels, tail, _ringPos);
                if (frames > tail)
                {
                    AppendStereo(samples.Slice(tail * channels), channels, frames - tail, 0);
                }
                _ringPos = (_ringPos + frames) % FftSize;
                _ringFilled = Math.Min(FftSize, _ringFilled + frames);
            }

            // Unroll the ring so index 0 is the oldest sample.
            int oldest = _ringFilled < FftSize ? 0 : _ringPos;
            int contiguous = FftSize - oldest;
            _ringL.AsSpan(oldest, contiguous).CopyTo(_left);
            _ringR.AsSpan(oldest, contiguous).CopyTo(_right);
            if (oldest > 0)
            {
                _ringL.AsSpan(0, oldest).CopyTo(_left.AsSpan(contiguous));
                _ringR.AsSpan(0, oldest).CopyTo(_right.AsSpan(contiguous));
            }
            if (_ringFilled < FftSize)
            {
                _left.AsSpan(_ringFilled, FftSize - _ringFilled).Clear();
                _right.AsSpan(_ringFilled, FftSize - _ringFilled).Clear();
            }
            // mono = 双声道平均（时域），频谱语义与改造前逐位一致。
            for (int i = 0; i < FftSize; i++)
            {
                _mono[i] = (_left[i] + _right[i]) * 0.5f;
            }

            _fft.RealForward(_mono, _spectrum);
            _fft.RealForward(_left, _spectrumL);
            _fft.RealForward(_right, _spectrumR);

            float[] bands = _bandsBack;
            AggregateBands(_spectrum, bands);
            AggregateBands(_spectrumL, _bandsLBack);
            AggregateBands(_spectrumR, _bandsRBack);

            Analyze(bands, (float)bytesRecorded / _capture.WaveFormat.AverageBytesPerSecond);
            Interlocked.Increment(ref _publicationCount);
        }

        /// <summary>RealForward 带 1/N 缩放（满幅正弦峰值 0.5），聚合到 512 段取段内峰值。</summary>
        private void AggregateBands(Complex[] spectrum, float[] bands)
        {
            float binScale = InputGain * 16f;
            int binsPerBand = (SpectrumLength - 1) / BandCount;
            for (int i = 0; i < BandCount; i++)
            {
                int start = i * binsPerBand;
                int end = start + binsPerBand;
                float m = 0f;
                for (int b = start; b < end; b++)
                {
                    float re = spectrum[b].X;
                    float im = spectrum[b].Y;
                    float mag = MathF.Sqrt(re * re + im * im) * binScale;
                    if (mag > m) m = mag;
                }
                bands[i] = m > 1f ? 1f : m;
            }
        }

        private void AppendStereo(ReadOnlySpan<float> samples, int channels, int frames, int ringOffset)
        {
            for (int f = 0; f < frames; f++)
            {
                int o = f * channels;
                float l = samples[o];
                float r = channels > 1 ? samples[o + 1] : l;
                int idx = (ringOffset + f) % FftSize;
                _ringL[idx] = l;
                _ringR[idx] = r;
            }
        }

        private void Analyze(float[] bands, float dt)
        {
            float sum = 0f, weighted = 0f, diffSum = 0f;
            float sub = 0f, bass = 0f, lowMid = 0f, mid = 0f, highMid = 0f;
            float presence = 0f, brilliance = 0f, air = 0f;
            float pulseFlux = 0f, meteorFlux = 0f;

            for (int i = 0; i < BandCount; i++)
            {
                float v = bands[i];
                sum += v;
                weighted += i * v;
                float prev = _prevBands[i];
                diffSum += MathF.Abs(v - prev);
                if (i <= PulseBandEnd)
                {
                    float d = v - prev;
                    if (d > 0f) pulseFlux += d;
                }
                if (i >= MeteorBandStart && i <= MeteorBandEnd)
                {
                    float d = v - prev;
                    if (d > 0f) meteorFlux += d;
                }
                _prevBands[i] = v;
                if (i <= 4) sub += v;
                else if (i <= 12) bass += v;
                else if (i <= 24) lowMid += v;
                else if (i <= 45) mid += v;
                else if (i <= 81) highMid += v;
                else if (i <= 120) presence += v;
                else if (i <= 180) brilliance += v;
                else if (i <= 255) air += v;
            }

            float mean = sum / BandCount;
            // Normalize flux to a 60 fps reference frame so trigger strengths stay
            // calibration-stable regardless of the WASAPI callback period.
            float fluxScale = 0.016f / MathF.Max(dt, 0.001f);
            EvaluateFlux(ref _pulseSmoothedFlux, ref _pulsePrevSmoothedFlux, _pulseFluxHistory, ref _pulseFluxIndex,
                ref _pulseBeatHold, pulseFlux * fluxScale, dt, PulseSensitivity, PulseCooldown, PulseStrengthScale,
                ref _pulseTriggerCount, ref _pulseTriggerStrength);
            EvaluateFlux(ref _meteorSmoothedFlux, ref _meteorPrevSmoothedFlux, _meteorFluxHistory, ref _meteorFluxIndex,
                ref _meteorBeatHold, meteorFlux * fluxScale, dt, MeteorSensitivity, MeteorCooldown, MeteorStrengthScale,
                ref _meteorTriggerCount, ref _meteorTriggerStrength);

            float bassSum = sub + bass + lowMid;
            float midSum = mid + highMid;
            float trebleSum = presence + brilliance + air;
            float warmth = sum > 0f ? (sub + bass + lowMid + mid) / sum : 0f;
            float brightness = sum > 0f ? trebleSum / sum : 0f;
            float sharpness = MathF.Max(0f, brightness - _prevBrightness) * 10f;
            _prevBrightness = brightness;
            float smoothness = MathF.Max(0f, 1f - diffSum / BandCount * 2f);
            float level = mean * 1.5f;
            int above = 0;
            above += sub / 5f > level ? 1 : 0;
            above += bass / 9f > level ? 1 : 0;
            above += lowMid / 13f > level ? 1 : 0;
            above += mid / 21f > level ? 1 : 0;
            above += highMid / 37f > level ? 1 : 0;
            above += presence / 40f > level ? 1 : 0;
            above += brilliance / 61f > level ? 1 : 0;
            above += air / 76f > level ? 1 : 0;

            float[] target = _featuresBack;
            target[FeatureIndex.SubBass] = sub / 5f;
            target[FeatureIndex.Bass] = bass / 9f;
            target[FeatureIndex.LowMid] = lowMid / 13f;
            target[FeatureIndex.Mid] = mid / 21f;
            target[FeatureIndex.HighMid] = highMid / 37f;
            target[FeatureIndex.Presence] = presence / 40f;
            target[FeatureIndex.Brilliance] = brilliance / 61f;
            target[FeatureIndex.Air] = air / 76f;
            target[FeatureIndex.Warmth] = warmth;
            target[FeatureIndex.Brightness] = brightness;
            target[FeatureIndex.Sharpness] = sharpness;
            target[FeatureIndex.Smoothness] = smoothness;
            target[FeatureIndex.Density] = above / 8f;
            target[FeatureIndex.SpectralCentroid] = sum > 0f ? weighted / sum / BandCount : 0f;
            target[FeatureIndex.Energy] = mean;

            float follow = 1f - MathF.Pow(1f - SmoothingRate, MathF.Max(dt, 0f) * 60f);
            float[] smoothed = _featuresFront;
            for (int i = 0; i < FeatureIndex.Count; i++)
            {
                target[i] = smoothed[i] + (target[i] - smoothed[i]) * follow;
            }

            // Publish: swap front/back references (atomic reference writes, no allocation).
            float[] tmpBands = _bandsFront;
            _bandsFront = _bandsBack;
            _bandsBack = tmpBands;
            float[] tmpBandsL = _bandsLFront;
            _bandsLFront = _bandsLBack;
            _bandsLBack = tmpBandsL;
            float[] tmpBandsR = _bandsRFront;
            _bandsRFront = _bandsRBack;
            _bandsRBack = tmpBandsR;
            float[] tmpFeatures = _featuresFront;
            _featuresFront = _featuresBack;
            _featuresBack = tmpFeatures;
        }

        private static void EvaluateFlux(ref float smoothedFlux, ref float prevSmoothedFlux, float[] history,
            ref int historyIndex, ref float beatHold, float flux, float dt,
            float sensitivity, float cooldown, float strengthScale,
            ref int triggerCount, ref float triggerStrength)
        {
            // Adapt the smoothing constant to the callback period (matches a 60 fps reference loop).
            float follow = 1f - MathF.Pow(0.6f, MathF.Max(dt, 0.001f) * 60f);
            smoothedFlux += (flux - smoothedFlux) * follow;
            history[historyIndex] = smoothedFlux;
            historyIndex = (historyIndex + 1) % FluxHistorySize;

            float mean = 0f;
            for (int i = 0; i < FluxHistorySize; i++) mean += history[i];
            mean /= FluxHistorySize;
            float variance = 0f;
            for (int i = 0; i < FluxHistorySize; i++)
            {
                float d = history[i] - mean;
                variance += d * d;
            }
            variance /= FluxHistorySize;
            float deviation = MathF.Sqrt(variance);
            float threshold = mean + MathF.Max(0.1f, 5f - sensitivity * 4f) * deviation;

            if (beatHold > 0f)
            {
                beatHold = MathF.Max(0f, beatHold - dt * 60f);
            }
            else
            {
                bool fallingPeak = prevSmoothedFlux > threshold
                    && prevSmoothedFlux >= smoothedFlux
                    && prevSmoothedFlux - smoothedFlux > 1e-4f;
                if (fallingPeak)
                {
                    triggerStrength = prevSmoothedFlux * 2.2f * strengthScale;
                    triggerCount = triggerCount + 1; // render side polls this counter
                    beatHold = cooldown;
                }
            }

            prevSmoothedFlux = smoothedFlux;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
                _capture.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
