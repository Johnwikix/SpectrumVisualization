using NAudio.Dsp;
using NAudio.Wave;
using System;
using System.Runtime.InteropServices;
using System.Threading;

// WasapiLoopbackCapture is marked obsolete in NAudio 3.0 in favor of WasapiRecorderBuilder,
// but it remains fully functional and keeps the callback semantics (DataAvailable per device
// period) that this analyzer is built around.
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
        public const int FftSize = 2048;
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

        private readonly WasapiLoopbackCapture _capture;
        private readonly FftProcessor _fft = new(FftSize, FftWindowType.Hann);
        private readonly Complex[] _spectrum = new Complex[SpectrumLength];
        private readonly float[] _mono = new float[FftSize];
        private readonly float[] _ring = new float[FftSize];
        private int _ringFilled;
        private int _ringPos;

        // Double buffered publications (reference swaps are atomic).
        private readonly float[] _bandsA = new float[BandCount];
        private readonly float[] _bandsB = new float[BandCount];
        private float[] _bandsFront;
        private float[] _bandsBack;
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

        private bool _disposed;
        private readonly int _sampleRate;

        /// <summary>Overall gain applied to raw FFT magnitudes (user "sensitivity").</summary>
        public float InputGain = 8.0f;

        /// <summary>Actual loopback capture sample rate in Hz.</summary>
        public int SampleRate => _sampleRate;

        public SpectrumAnalyzer()
        {
            _bandsFront = _bandsA;
            _bandsBack = _bandsB;
            _featuresFront = _featuresA;
            _featuresBack = _featuresB;

            _capture = new WasapiLoopbackCapture();
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
            // Loopback captures die when the default device changes; try to recover once.
            if (_disposed) return;
            try
            {
                _capture.StartRecording();
            }
            catch (Exception)
            {
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
                AppendMono(samples.Slice((frames - FftSize) * channels), channels, FftSize, 0);
                _ringFilled = FftSize;
                _ringPos = 0;
            }
            else
            {
                int tail = Math.Min(frames, FftSize - _ringPos);
                AppendMono(samples, channels, tail, _ringPos);
                if (frames > tail)
                {
                    AppendMono(samples.Slice(tail * channels), channels, frames - tail, 0);
                }
                _ringPos = (_ringPos + frames) % FftSize;
                _ringFilled = Math.Min(FftSize, _ringFilled + frames);
            }

            // Unroll the ring so index 0 is the oldest sample.
            int oldest = _ringFilled < FftSize ? 0 : _ringPos;
            _ring.AsSpan(oldest, FftSize - oldest).CopyTo(_mono);
            if (oldest > 0)
            {
                _ring.AsSpan(0, oldest).CopyTo(_mono.AsSpan(FftSize - oldest));
            }
            if (_ringFilled < FftSize)
            {
                _mono.AsSpan(_ringFilled, FftSize - _ringFilled).Clear();
            }

            _fft.RealForward(_mono, _spectrum);

            float[] bands = _bandsBack;
            // RealForward applies 1/N scaling (a full-scale sine lands at 0.5 on its
            // peak bin), so raw per-bin magnitudes for music are ~1e-3..1e-2. Rescale
            // into the 0..1 range the feature pipeline expects; InputGain trims it.
            float binScale = InputGain * 16f;
            int binsPerBand = (SpectrumLength - 1) / BandCount;
            for (int i = 0; i < BandCount; i++)
            {
                int start = i * binsPerBand;
                int end = start + binsPerBand;
                float m = 0f;
                for (int b = start; b < end; b++)
                {
                    float re = _spectrum[b].X;
                    float im = _spectrum[b].Y;
                    float mag = MathF.Sqrt(re * re + im * im) * binScale;
                    if (mag > m) m = mag;
                }
                bands[i] = m > 1f ? 1f : m;
            }

            Analyze(bands, (float)bytesRecorded / _capture.WaveFormat.AverageBytesPerSecond);
        }

        private void AppendMono(ReadOnlySpan<float> samples, int channels, int frames, int ringOffset)
        {
            for (int f = 0; f < frames; f++)
            {
                float acc = 0f;
                int o = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    acc += samples[o + c];
                }
                _ring[(ringOffset + f) % FftSize] = acc / channels;
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

            float[] smoothed = _featuresFront;
            for (int i = 0; i < FeatureIndex.Count; i++)
            {
                target[i] = smoothed[i] + (target[i] - smoothed[i]) * SmoothingRate;
            }

            // Publish: swap front/back references (atomic reference writes, no allocation).
            float[] tmpBands = _bandsFront;
            _bandsFront = _bandsBack;
            _bandsBack = tmpBands;
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
