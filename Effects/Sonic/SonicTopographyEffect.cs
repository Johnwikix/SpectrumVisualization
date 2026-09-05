using ComputeSharp;
using ComputeSharp.D2D1.WinUI;
using Microsoft.Graphics.Canvas;
using Windows.Graphics.DirectX;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Numerics;
using Windows.Foundation;
using Windows.UI;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic.Shaders;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Services;

namespace WinExSpectrumTest.Effects.Sonic
{
    /// <summary>
    /// Native port of the "音域回响 / Sonic Topography" audio-responsive wallpaper
    /// (Wallpaper Engine workshop #3747222633) as a ComputeSharp.D2D1 powered
    /// effect page. The 160x160 instanced bar grid of the original is rebuilt as
    /// a GPU heightfield (HeightFieldShader) ray-marched with an exact voxel DDA
    /// (TerrainShader); beat ripples, meteors with particle explosions, idle
    /// waves, theme colors and the orbit camera all follow the original logic.
    /// Per-frame CPU work is simulation only over fixed-size pools: no
    /// allocations in Update/Draw.
    /// </summary>
    public sealed class SonicTopographyEffect : IVisualizerEffect
    {
        public const string EffectId = "sonic-topography";
        public const float GridExtent = 168f;

        public string Id => EffectId;
        public string DisplayName => DisplayNameConst;
        public const string DisplayNameConst = "Sonic Topography";

        private const int RippleSlots = 12;
        private const int MeteorSlots = 40;
        private const int ParticleSlots = 200;

        private const float CameraPitch = 27f;
        private const float CameraDistance = 85f;
        private const float FieldOfViewY = 45f;

        private VisualizerServices _services = null!;

        // GPU resources.
        private PixelShaderEffect<HeightFieldShader> _heightEffect = new();
        private PixelShaderEffect<TerrainShader> _terrainEffect = new();
        private CanvasRenderTarget? _heightField;
        // Resolved lazily: CanvasAnimatedControl has no device until it is loaded.
        private CanvasDevice? _deviceField;
        private CanvasDevice Device => _deviceField ??= _services.Control.Device;
        private int _gridSize;
        private float _rtDpi = 96f;

        // Simulation time.
        private float _time;

        // Ripple ring pool: position, start time, signed strength (negative = meteor).
        private readonly float[] _rippleX = new float[RippleSlots];
        private readonly float[] _rippleZ = new float[RippleSlots];
        private readonly float[] _rippleTime = new float[RippleSlots];
        private readonly float[] _rippleStrength = new float[RippleSlots];
        private int _rippleCursor;

        // Meteor pool.
        private readonly bool[] _meteorActive = new bool[MeteorSlots];
        private readonly float[] _meteorX = new float[MeteorSlots];
        private readonly float[] _meteorY = new float[MeteorSlots];
        private readonly float[] _meteorZ = new float[MeteorSlots];
        private readonly float[] _meteorSpeed = new float[MeteorSlots];
        private readonly float[] _meteorStrength = new float[MeteorSlots];
        private int _meteorCursor;
        private float _lastMeteorSpawnTime = -999f;

        // Particle pool.
        private readonly bool[] _particleActive = new bool[ParticleSlots];
        private readonly float[] _particleX = new float[ParticleSlots];
        private readonly float[] _particleY = new float[ParticleSlots];
        private readonly float[] _particleZ = new float[ParticleSlots];
        private readonly float[] _particleVX = new float[ParticleSlots];
        private readonly float[] _particleVY = new float[ParticleSlots];
        private readonly float[] _particleVZ = new float[ParticleSlots];
        private readonly float[] _particleLife = new float[ParticleSlots];
        private readonly float[] _particleMaxLife = new float[ParticleSlots];
        private readonly float[] _particleScale = new float[ParticleSlots];
        private int _particleCursor;

        // Ripple cluster targeting (kick beats land near a shared focus).
        private bool _clusterInitialized;
        private float _clusterX;
        private float _clusterZ;
        private float _clusterLastTriggerTime = -999f;
        private int _clusterHitsRemaining;

        // Idle wave state.
        private float _idleIntensity;
        private float _idleTimer;

        // Camera + theme.
        private float _yaw = 120f;
        private Vector3 _base1 = new(.005f, .008f, .025f);
        private Vector3 _base2 = new(.015f, .025f, .07f);
        private Vector3 _coolCore = new(.35f, .1f, .9f);
        private Vector3 _coolEdge = new(.15f, 0f, .45f);
        private Vector3 _warmCore = new(.65f, .25f, 1f);
        private Vector3 _warmEdge = new(.5f, .1f, .8f);
        private Vector3 _rippleColor = new(.5f, .2f, 1f);
        private Vector3 _peakColor = new(1f, .55f, .05f);
        private float _glowIntensity = 1f;

        // Analyzer trigger consumption.
        private int _lastPulseCount;
        private int _lastMeteorCount;

        private readonly Random _random = new();

        // Camera basis cache (updated each frame, shared by shader and projections).
        private Vector3 _cameraPosition;
        private Vector3 _cameraRight;
        private Vector3 _cameraUp;
        private Vector3 _cameraForward;
        private float _tanHalfFovY;

        public void Initialize(VisualizerServices services)
        {
            _services = services;
        }

        public void OnResize(float width, float height)
        {
        }

        public void Update(double elapsedSeconds)
        {
            float dt = (float)Math.Clamp(elapsedSeconds, 0.0, 0.1);
            _time += dt;

            ReadOnlySpan<float> features = _services.Analyzer.LatestFeatures;
            float energy = features.Length > FeatureIndex.Energy ? features[FeatureIndex.Energy] : 0f;

            // Idle wave: gentle breathing when the mix is quiet for a while.
            if (energy > 0.02f)
            {
                _idleTimer = 0f;
            }
            else
            {
                _idleTimer += dt;
            }
            bool idleTarget = AppSettings.SonicIdleWaveEnabled && _idleTimer >= 1f;
            float idleRate = dt / 1f;
            if (idleTarget) _idleIntensity = Math.Min(1f, _idleIntensity + idleRate);
            else _idleIntensity = Math.Max(0f, _idleIntensity - idleRate);

            // Consume beat triggers from the analyzer.
            int pulseCount = _services.Analyzer.PulseTriggerCount;
            if (pulseCount != _lastPulseCount)
            {
                _lastPulseCount = pulseCount;
                if (AppSettings.SonicRippleEnabled)
                {
                    OnBassPulse(_services.Analyzer.PulseTriggerStrength);
                }
            }
            int meteorCount = _services.Analyzer.MeteorTriggerCount;
            if (meteorCount != _lastMeteorCount)
            {
                _lastMeteorCount = meteorCount;
                if (AppSettings.SonicMeteorEnabled)
                {
                    OnMeteorTrigger(_services.Analyzer.MeteorTriggerStrength);
                }
            }

            // Meteors fall and detonate into ripples + particles.
            for (int i = 0; i < MeteorSlots; i++)
            {
                if (!_meteorActive[i]) continue;
                _meteorY[i] -= _meteorSpeed[i] * 60f * dt;
                if (_meteorY[i] <= 0f)
                {
                    _meteorActive[i] = false;
                    SpawnRipple(_meteorX[i], _meteorZ[i], Math.Min(_meteorStrength[i] * 1.5f, 8f), meteor: true);
                    for (int p = 0; p < 10; p++)
                    {
                        SpawnParticle(_meteorX[i], 0.5f, _meteorZ[i], _meteorSpeed[i] * 1.5f);
                    }
                }
                else if (_random.NextDouble() > 0.3)
                {
                    SpawnParticle(_meteorX[i], _meteorY[i], _meteorZ[i], _meteorSpeed[i] * 0.2f);
                }
            }

            // Particles fly and fade.
            for (int i = 0; i < ParticleSlots; i++)
            {
                if (!_particleActive[i]) continue;
                _particleLife[i] += dt;
                if (_particleLife[i] >= _particleMaxLife[i])
                {
                    _particleActive[i] = false;
                    continue;
                }
                _particleX[i] += _particleVX[i] * dt * 10f;
                _particleY[i] += _particleVY[i] * dt * 10f;
                _particleZ[i] += _particleVZ[i] * dt * 10f;
            }

            // Camera orbit.
            if (AppSettings.SonicAutoRotate)
            {
                _yaw = (_yaw + AppSettings.SonicRotateSpeed * dt) % 360f;
            }

            UpdateCamera();

            // Smoothly approach the selected theme palette.
            SonicPalette target = SonicThemes.Resolve(AppSettings.SonicTheme);
            float lerp = 1f - MathF.Pow(0.95f, dt * 60f);
            _base1 = Vector3.Lerp(_base1, target.BaseColor1, lerp);
            _base2 = Vector3.Lerp(_base2, target.BaseColor2, lerp);
            _coolCore = Vector3.Lerp(_coolCore, target.CoolCore, lerp);
            _coolEdge = Vector3.Lerp(_coolEdge, target.CoolEdge, lerp);
            _warmCore = Vector3.Lerp(_warmCore, target.WarmCore, lerp);
            _warmEdge = Vector3.Lerp(_warmEdge, target.WarmEdge, lerp);
            _rippleColor = Vector3.Lerp(_rippleColor, target.RippleColor, lerp);
            _peakColor = Vector3.Lerp(_peakColor, target.PeakColor, lerp);
            _glowIntensity += (target.GlowIntensity - _glowIntensity) * lerp;
        }

        private void OnBassPulse(float triggerStrength)
        {
            float now = _time;
            if (!_clusterInitialized || now - _clusterLastTriggerTime > 1.5f || _clusterHitsRemaining <= 0)
            {
                _clusterX = (_random.NextSingle() - 0.5f) * 50f;
                _clusterZ = (_random.NextSingle() - 0.5f) * 50f;
                _clusterHitsRemaining = 5 + _random.Next(9);
                _clusterInitialized = true;
            }
            _clusterHitsRemaining--;
            _clusterLastTriggerTime = now;

            float x = _clusterX + (_random.NextSingle() - 0.5f) * 8f;
            float z = _clusterZ + (_random.NextSingle() - 0.5f) * 8f;
            SpawnRipple(x, z, Math.Min(triggerStrength * 40f, 8f), meteor: false);
        }

        private void OnMeteorTrigger(float triggerStrength)
        {
            float now = _time;
            float cooldown = 180f / 60f;
            if (now - _lastMeteorSpawnTime < cooldown) return;
            _lastMeteorSpawnTime = now;

            float strength = Math.Min(triggerStrength * 20f, 4f);
            float angle = _random.NextSingle() * MathF.PI * 2f;
            float radius = 10f + _random.NextSingle() * 35f;
            int slot = _meteorCursor;
            _meteorActive[slot] = true;
            _meteorX[slot] = MathF.Cos(angle) * radius;
            _meteorZ[slot] = MathF.Sin(angle) * radius;
            _meteorY[slot] = 30f + _random.NextSingle() * 10f;
            _meteorSpeed[slot] = 1f + _random.NextSingle() * 0.5f + strength * 2.5f;
            _meteorStrength[slot] = strength;
            _meteorCursor = (slot + 1) % MeteorSlots;
        }

        private void SpawnRipple(float x, float z, float strength, bool meteor)
        {
            int slot = _rippleCursor;
            _rippleX[slot] = x;
            _rippleZ[slot] = z;
            _rippleTime[slot] = _time;
            _rippleStrength[slot] = meteor ? -Math.Max(strength, 0.001f) : Math.Max(strength, 0.001f);
            _rippleCursor = (slot + 1) % RippleSlots;
        }

        private void SpawnParticle(float x, float y, float z, float speed)
        {
            int slot = _particleCursor;
            _particleActive[slot] = true;
            _particleX[slot] = x + (_random.NextSingle() - 0.5f) * 1.5f;
            _particleY[slot] = y + (_random.NextSingle() - 0.5f) * 1.5f;
            _particleZ[slot] = z + (_random.NextSingle() - 0.5f) * 1.5f;
            _particleVX[slot] = (_random.NextSingle() - 0.5f) * 2f;
            _particleVY[slot] = _random.NextSingle() * 2f + speed * 10f;
            _particleVZ[slot] = (_random.NextSingle() - 0.5f) * 2f;
            _particleLife[slot] = 0f;
            _particleMaxLife[slot] = 0.5f + _random.NextSingle() * 0.5f;
            _particleScale[slot] = _random.NextSingle() * 0.6f + 0.2f;
            _particleCursor = (slot + 1) % ParticleSlots;
        }

        private void UpdateCamera()
        {
            float pitch = CameraPitch * MathF.PI / 180f;
            float yaw = _yaw * MathF.PI / 180f;
            _tanHalfFovY = MathF.Tan(FieldOfViewY * MathF.PI / 360f);

            float horizontal = CameraDistance * MathF.Cos(pitch);
            _cameraPosition = new Vector3(
                horizontal * MathF.Sin(yaw),
                CameraDistance * MathF.Sin(pitch),
                horizontal * MathF.Cos(yaw));

            _cameraForward = Vector3.Normalize(-_cameraPosition);
            _cameraRight = Vector3.Normalize(Vector3.Cross(_cameraForward, Vector3.UnitY));
            _cameraUp = Vector3.Cross(_cameraRight, _cameraForward);
        }

        public void Draw(CanvasDrawingSession session, float width, float height)
        {
            EnsureResources();

            int requestedGrid = Math.Clamp(AppSettings.SonicGridSize, 80, 320);
            float dpi = _services.Control.Dpi;
            if (_gridSize != requestedGrid || Math.Abs(_rtDpi - dpi) > 0.1f)
            {
                RecreateHeightField(requestedGrid);
            }

            // 1. Heightfield pass: one texel per bar cell.
            float cellSize = GridExtent / _gridSize;
            float halfExtent = GridExtent * 0.5f;
            float barSize = cellSize * 0.857f;

            ReadOnlySpan<float> f = _services.Analyzer.LatestFeatures;
            _heightEffect.ConstantBuffer = new HeightFieldShader(
                _time,
                Feature(f, FeatureIndex.SubBass),
                Feature(f, FeatureIndex.Bass),
                Feature(f, FeatureIndex.LowMid),
                Feature(f, FeatureIndex.Mid),
                Feature(f, FeatureIndex.HighMid),
                Feature(f, FeatureIndex.Energy),
                _idleIntensity,
                AppSettings.SonicAudioIntensity,
                AppSettings.SonicResponseRange,
                halfExtent,
                cellSize,
                Feature(f, FeatureIndex.Smoothness),
                Feature(f, FeatureIndex.Density),
                Ripple(0), Ripple(1), Ripple(2), Ripple(3),
                Ripple(4), Ripple(5), Ripple(6), Ripple(7),
                Ripple(8), Ripple(9), Ripple(10), Ripple(11));

            using (CanvasDrawingSession fieldSession = _heightField!.CreateDrawingSession())
            {
                fieldSession.Clear(Color.FromArgb(0, 0, 0, 0));
                fieldSession.DrawImage(_heightEffect);
            }

            // 2. Terrain pass: voxel-DDA ray march with the original shading model.
            // D2D scene positions are DIPs, matching the width/height passed by the host.
            _terrainEffect.Sources[0] = _heightField;
            _terrainEffect.ConstantBuffer = new TerrainShader(
                ToFloat3(_cameraPosition),
                ToFloat3(_cameraRight),
                ToFloat3(_cameraUp),
                ToFloat3(_cameraForward),
                _tanHalfFovY,
                new float2(width, height),
                cellSize,
                halfExtent,
                barSize,
                1f / _gridSize,
                ToFloat3(_base1),
                ToFloat3(_base2),
                ToFloat3(_coolCore),
                ToFloat3(_coolEdge),
                ToFloat3(_warmCore),
                ToFloat3(_warmEdge),
                ToFloat3(_rippleColor),
                ToFloat3(_peakColor),
                _time,
                Feature(f, FeatureIndex.Warmth),
                Feature(f, FeatureIndex.Brightness),
                Feature(f, FeatureIndex.Sharpness),
                Feature(f, FeatureIndex.Presence),
                Feature(f, FeatureIndex.Brilliance),
                Feature(f, FeatureIndex.Air),
                _glowIntensity,
                AppSettings.SonicPeakColorEnabled ? 1f : 0f,
                AppSettings.SonicPeakIntensity);

            session.DrawImage(_terrainEffect);

            // 3. Meteors and particles as projected glowing quads.
            DrawSkyObjects(session, width, height);
        }

        private float4 Ripple(int slot)
        {
            return new float4(_rippleX[slot], _rippleZ[slot], _rippleTime[slot], _rippleStrength[slot]);
        }

        private static float Feature(ReadOnlySpan<float> features, int index)
        {
            return features.Length > index ? features[index] : 0f;
        }

        private static float3 ToFloat3(Vector3 v) => new(v.X, v.Y, v.Z);

        private void EnsureResources()
        {
            if (_heightField == null)
            {
                RecreateHeightField(Math.Clamp(AppSettings.SonicGridSize, 80, 320));
            }
        }

        private void RecreateHeightField(int gridSize)
        {
            _gridSize = gridSize;
            _rtDpi = _services.Control.Dpi;
            _heightField?.Dispose();
            // Match the control DPI so Win2D inserts no DPI compensation between the
            // terrain shader and this source texture (that would rescale the sampled
            // uv range). Width/height are DIPs; one DIP == one texel.
            _heightField = new CanvasRenderTarget(
                Device,
                gridSize,
                gridSize,
                _rtDpi,
                DirectXPixelFormat.R16G16B16A16Float,
                CanvasAlphaMode.Premultiplied);
        }

        private void DrawSkyObjects(CanvasDrawingSession session, float width, float height)
        {
            Color warmColor = ToColor(Vector3.Lerp(_warmCore, Vector3.One, 0.7f), 235);
            Color warmGlow = ToColor(Vector3.Lerp(_warmCore, Vector3.One, 0.7f), 60);
            Color particleColor = ToColor(Vector3.One, 150);

            for (int i = 0; i < MeteorSlots; i++)
            {
                if (!_meteorActive[i]) continue;
                if (!TryProject(_meteorX[i], _meteorY[i], _meteorZ[i], width, height, out Vector2 pos, out float scale)) continue;
                float w = 0.45f * scale;
                float hgt = 1.3f * scale;
                var rect = new Rect(pos.X - w * 0.5f, pos.Y - hgt * 0.5f, w, hgt);
                var glowRect = new Rect(pos.X - w, pos.Y - hgt, w * 2f, hgt * 2f);
                session.FillRectangle(glowRect, warmGlow);
                session.FillRectangle(rect, warmColor);
            }

            for (int i = 0; i < ParticleSlots; i++)
            {
                if (!_particleActive[i]) continue;
                float fade = 1f - _particleLife[i] / _particleMaxLife[i];
                float size = 0.8f * _particleScale[i] * fade;
                if (size <= 0.01f) continue;
                if (!TryProject(_particleX[i], _particleY[i], _particleZ[i], width, height, out Vector2 pos, out float scale)) continue;
                float s = size * scale;
                session.FillRectangle(new Rect(pos.X - s * 0.5f, pos.Y - s * 0.5f, s, s), particleColor);
            }
        }

        private bool TryProject(float x, float y, float z, float width, float height, out Vector2 pos, out float scale)
        {
            pos = default;
            scale = 0f;
            Vector3 v = new Vector3(x, y, z) - _cameraPosition;
            float depth = Vector3.Dot(v, _cameraForward);
            if (depth < 0.5f) return false;
            float aspect = width / height;
            float ndcX = Vector3.Dot(v, _cameraRight) / (depth * _tanHalfFovY * aspect);
            float ndcY = Vector3.Dot(v, _cameraUp) / (depth * _tanHalfFovY);
            if (ndcX < -1.2f || ndcX > 1.2f || ndcY < -1.2f || ndcY > 1.2f) return false;
            pos = new Vector2((ndcX * 0.5f + 0.5f) * width, (1f - (ndcY * 0.5f + 0.5f)) * height);
            scale = height / (2f * _tanHalfFovY * depth);
            return true;
        }

        private static Color ToColor(Vector3 v, byte alpha = 255)
        {
            return Color.FromArgb(
                alpha,
                (byte)(Math.Clamp(v.X, 0f, 1f) * 255f),
                (byte)(Math.Clamp(v.Y, 0f, 1f) * 255f),
                (byte)(Math.Clamp(v.Z, 0f, 1f) * 255f));
        }

        public void Dispose()
        {
            _heightEffect.Dispose();
            _terrainEffect.Dispose();
            _heightField?.Dispose();
            _heightField = null;
        }
    }
}
