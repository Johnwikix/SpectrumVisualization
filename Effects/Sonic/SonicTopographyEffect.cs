using ComputeSharp;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;
using WinExSpectrumTest.Effects.Sonic.Shaders;

namespace WinExSpectrumTest.Effects.Sonic;

/// <summary>Render-thread-owned Sonic simulation and compute passes.</summary>
internal sealed class SonicTopographyEffect : IDisposable
{
    public const string EffectId = "sonic-topography";
    public const float GridExtent = 168f;
    private const int RippleSlots = 12;
    private const int MeteorSlots = 40;
    private const int ParticleSlots = 200;

    // 原版运行默认（sG 组件 state）：cameraDistance=85, yaw=120, pitch=25, fov=45, lookAt(0,0,0)
    private const float CameraPitch = 25f;
    private const float CameraDistance = 85f;
    // 注视点高度偏移：视觉上把音频响应主体居中（透视导致亮顶偏向画面上方）
    private const float CameraLookHeight = 0f;   // 注视点=触发中心：旋转/缩放原点必须投影在屏幕正中
    internal const float FieldOfViewY = 45f;

    private readonly SpectrumAnalyzer _analyzer;
    private readonly GraphicsDevice _device;
    private ReadWriteTexture2D<float4>? _heightField;
    private readonly ReadWriteTexture2D<float4>?[] _heightFields = new ReadWriteTexture2D<float4>?[2];
    private int _heightFieldCount = 1;
    private int _gridSize;
    private readonly ReadOnlyBuffer<float4> _shaderData;
    private readonly UploadBuffer<float4> _upload;


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

    // Idle wave state.
    private float _idleIntensity;
    private float _idleTimer;

    // Ripple cluster targeting (kick beats land near a shared focus).
    private bool _clusterInitialized;
    private float _clusterX;
    private float _clusterZ;
    private float _clusterLastTriggerTime = -999f;
    private int _clusterHitsRemaining;

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


    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.NonPublicFields, typeof(float4))]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "The buffer element is ComputeSharp.Float4; its four primitive float fields are explicitly preserved above.")]
    public SonicTopographyEffect(GraphicsDevice device, SpectrumAnalyzer analyzer)
    {
        _device = device;
        _analyzer = analyzer;
        _shaderData = device.AllocateReadOnlyBuffer<float4>(32);
        try { _upload = device.AllocateUploadBuffer<float4>(32); }
        catch { _shaderData.Dispose(); throw; }

        _lastPulseCount = analyzer.PulseTriggerCount;
        _lastMeteorCount = analyzer.MeteorTriggerCount;
    }

    public void Update(double elapsedSeconds)
    {
        float dt = (float)Math.Clamp(elapsedSeconds, 0.0, 0.1);
        _time += dt;

        ReadOnlySpan<float> features = _analyzer.LatestFeatures;
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
        int pulseCount = _analyzer.PulseTriggerCount;
        if (pulseCount != _lastPulseCount)
        {
            _lastPulseCount = pulseCount;
            if (AppSettings.SonicRippleEnabled)
            {
                OnBassPulse(_analyzer.PulseTriggerStrength);
            }
        }
        int meteorCount = _analyzer.MeteorTriggerCount;
        if (meteorCount != _lastMeteorCount)
        {
            _lastMeteorCount = meteorCount;
            if (AppSettings.SonicMeteorEnabled)
            {
                OnMeteorTrigger(_analyzer.MeteorTriggerStrength);
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

        // 注视点相对原点抬高：地面透视会把远处柱体的亮顶"推"向画面上方，
        // 抬高注视点把整个响应主体视觉下移回画面中央。
        var lookTarget = new Vector3(0f, CameraLookHeight, 0f);
        _cameraForward = Vector3.Normalize(lookTarget - _cameraPosition);
        _cameraRight = Vector3.Normalize(Vector3.Cross(_cameraForward, Vector3.UnitY));
        _cameraUp = Vector3.Cross(_cameraRight, _cameraForward);
    }


    /// <summary>Gets a frame slot's height field, borrowed by the raster renderer.</summary>
    internal ReadWriteTexture2D<float4> GetHeightField(int index) => _heightFields[index]!;

    /// <summary>Called only after the host drains all readers of the old textures.</summary>
    internal void ConfigureHeightFields(int requestedGrid, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _heightFields.Length);
        requestedGrid = Math.Clamp(requestedGrid, 80, 320);
        for (int i = 0; i < _heightFields.Length; i++)
        {
            if (i >= count)
            {
                _heightFields[i]?.Dispose();
                _heightFields[i] = null;
            }
            else if (_heightFields[i] == null || _heightFields[i]!.Width != requestedGrid)
            {
                var replacement = _device.AllocateReadWriteTexture2D<float4>(requestedGrid, requestedGrid);
                _heightFields[i]?.Dispose();
                _heightFields[i] = replacement;
            }
        }
        _gridSize = requestedGrid;
        _heightFieldCount = count;
        _heightField = _heightFields[0];
    }

    /// <summary>Gets the linear background used by both rendering paths.</summary>
    internal Vector3 BackgroundColor => _base1;

    /// <summary>Updates the small height texture without tracing a full-screen image.</summary>
    internal void PrepareHeightField(int requestedGrid, int frameIndex = 0)
    {
        requestedGrid = Math.Clamp(requestedGrid, 80, 320);
        if (_heightFields[frameIndex] == null || _gridSize != requestedGrid)
            ConfigureHeightFields(requestedGrid, Math.Max(_heightFieldCount, frameIndex + 1));
        _heightField = _heightFields[frameIndex];
        Span<float4> data = _upload.Span;
        for (int i = 0; i < RippleSlots; i++)
        {
            float strength = _rippleStrength[i];
            bool meteor = strength < 0;
            float age = _time - _rippleTime[i];
            float radius = age * (meteor ? 18f : 14f);
            data[i] = new(_rippleX[i], _rippleZ[i], radius, age >= 0 ? strength : 0);
            data[20 + i] = new(meteor ? .4f : .2f,
                MathF.Exp(-radius / (meteor ? 18f : 22f)) * Math.Clamp(MathF.Abs(strength) * .4f, 0, 1),
                meteor ? 1.8f : 3f, 0);
        }
        data[12] = new(ToFloat3(_base1), 0);
        data[13] = new(ToFloat3(_base2), 0);
        data[14] = new(ToFloat3(_coolCore), 0);
        data[15] = new(ToFloat3(_coolEdge), 0);
        data[16] = new(ToFloat3(_warmCore), 0);
        data[17] = new(ToFloat3(_warmEdge), 0);
        data[18] = new(ToFloat3(_rippleColor), 0);
        data[19] = new(ToFloat3(_peakColor), 0);
        _upload.CopyTo(_shaderData);
        using var context = _device.CreateComputeContext();
        // 1. Heightfield pass: one texel per bar cell.
        float cellSize = GridExtent / _gridSize;
        float halfExtent = GridExtent * 0.5f;

        ReadOnlySpan<float> f = _analyzer.LatestFeatures;
        context.For(_gridSize, _gridSize, new HeightFieldShader(
            _shaderData, _heightField!,
            _time,
            EaseLift(Feature(f, FeatureIndex.SubBass), 6),
            EaseLift(Feature(f, FeatureIndex.Bass), 5),
            FlowLift(Feature(f, FeatureIndex.LowMid), 3),
            FlowLift(Feature(f, FeatureIndex.Mid), 4),
            EaseLift(Feature(f, FeatureIndex.HighMid), 3),
            EnergyLift(Feature(f, FeatureIndex.Energy)),
            _idleIntensity,
            AppSettings.SonicAudioIntensity,
            AppSettings.SonicResponseRange,
            halfExtent,
            cellSize,
            Feature(f, FeatureIndex.Smoothness),
            Feature(f, FeatureIndex.Density)));

    }

    /// <summary>Renders the reference DDA image used by verification probes.</summary>
    public void Render(ReadWriteTexture2D<float4> target)
    {
        PrepareHeightField(AppSettings.SonicGridSize);
        using var context = _device.CreateComputeContext();
        int width = target.Width;
        int height = target.Height;
        float cellSize = GridExtent / _gridSize;
        float halfExtent = GridExtent * .5f;
        float barSize = cellSize * .857f;
        ReadOnlySpan<float> f = _analyzer.LatestFeatures;
        context.For(width, height, new TerrainShader(
            _shaderData, _heightField!, target,
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
            _time,
            Feature(f, FeatureIndex.Warmth),
            Feature(f, FeatureIndex.Brightness),
            Feature(f, FeatureIndex.Sharpness),
            Feature(f, FeatureIndex.Presence),
            Feature(f, FeatureIndex.Brilliance),
            Feature(f, FeatureIndex.Air),
            _glowIntensity,
            AppSettings.SonicPeakColorEnabled ? 1f : 0f,
            AppSettings.SonicPeakIntensity));


    }

    /// <summary>Writes the fixed raster constants into caller-owned storage.</summary>
    internal void WriteSceneData(Span<float4> data, float aspect)
    {
        ReadOnlySpan<float> f = _analyzer.LatestFeatures;
        data[0] = new(ToFloat3(_cameraPosition), _tanHalfFovY);
        data[1] = new(ToFloat3(_cameraRight), aspect);
        data[2] = new(ToFloat3(_cameraUp), _time);
        data[3] = new(ToFloat3(_cameraForward), GridExtent / _gridSize);
        data[4] = new(ToFloat3(_base1), GridExtent * .5f);
        data[5] = new(ToFloat3(_base2), _glowIntensity);
        data[6] = new(ToFloat3(_coolCore), _gridSize);
        data[7] = new(ToFloat3(_coolEdge), AppSettings.SonicPeakColorEnabled ? 1 : 0);
        data[8] = new(ToFloat3(_warmCore), AppSettings.SonicPeakIntensity);
        data[9] = new(ToFloat3(_warmEdge), Feature(f, FeatureIndex.Warmth));
        data[10] = new(ToFloat3(_rippleColor), Feature(f, FeatureIndex.Brightness));
        data[11] = new(ToFloat3(_peakColor), Feature(f, FeatureIndex.Sharpness));
        data[12] = new(Feature(f, FeatureIndex.Presence), Feature(f, FeatureIndex.Brilliance), Feature(f, FeatureIndex.Air), 0);
    }

    // These curves depend on the frame's audio features, not on individual grid cells.
    private static float EaseLift(float raw, float height)
    {
        float x = Math.Clamp(raw, 0, 1);
        return (1 - MathF.Pow(1 - x, 2.5f) + MathF.Sin(x * 6.283f * 1.5f) * MathF.Exp(-x * 4) * .15f) * height;
    }

    private static float FlowLift(float raw, float height)
    {
        float x = Math.Clamp(raw, 0, 1);
        return (MathF.Pow(x, .75f) + MathF.Sin(x * 3.14159f) * .12f) * height;
    }

    private static float EnergyLift(float raw)
    {
        float x = Math.Clamp(raw, 0, 1);
        return (1 - MathF.Pow(1 - x, 1.5f) + MathF.Sin(x * 6.283f * 2) * MathF.Exp(-x * 5) * .2f) * 6;
    }
    private static float Feature(ReadOnlySpan<float> f, int i) => f.Length > i ? f[i] : 0;
    private static float3 ToFloat3(Vector3 v) => new(v.X, v.Y, v.Z);

    // Six vertices per projected quad. The caller supplies a persistently mapped upload buffer.
    public int WriteParticles(Span<ParticleVertex> vertices, float width, float height)
    {
        int count = 0;
        Vector3 warm = Vector3.Lerp(_warmCore, Vector3.One, 0.7f);
        for (int i = 0; i < MeteorSlots; i++)
        {
            if (!_meteorActive[i] || !TryProject(_meteorX[i], _meteorY[i], _meteorZ[i], width, height, out var p, out float scale)) continue;
            float w = .45f * scale;
            float h = 1.3f * scale;
            AddQuad(vertices, ref count, p, w * 2, h * 2, warm, 60f / 255, width, height);
            AddQuad(vertices, ref count, p, w, h, warm, 235f / 255, width, height);
        }
        for (int i = 0; i < ParticleSlots; i++)
        {
            if (!_particleActive[i]) continue;
            float size = .8f * _particleScale[i] * (1 - _particleLife[i] / _particleMaxLife[i]);
            if (size <= .01f || !TryProject(_particleX[i], _particleY[i], _particleZ[i], width, height, out var p, out float scale)) continue;
            AddQuad(vertices, ref count, p, size * scale, size * scale, Vector3.One, 150f / 255, width, height);
        }
        return count;
    }

    private static void AddQuad(Span<ParticleVertex> vertices, ref int count, Vector2 p, float w, float h, Vector3 c, float a, float width, float height)
    {
        float left = (p.X - w / 2) * 2 / width - 1;
        float right = (p.X + w / 2) * 2 / width - 1;
        float top = 1 - (p.Y - h / 2) * 2 / height;
        float bottom = 1 - (p.Y + h / 2) * 2 / height;
        // The legacy palette is sRGB. Decode before blending into the floating-point scene.
        var color = new Vector4(Decode(c.X) * a, Decode(c.Y) * a, Decode(c.Z) * a, a);
        vertices[count++] = new(new(left, top), color);
        vertices[count++] = new(new(right, top), color);
        vertices[count++] = new(new(left, bottom), color);
        vertices[count++] = new(new(left, bottom), color);
        vertices[count++] = new(new(right, top), color);
        vertices[count++] = new(new(right, bottom), color);
    }

    private static float Decode(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
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


    public void Dispose()
    {
        foreach (var field in _heightFields) field?.Dispose();
        _shaderData.Dispose();
        _upload.Dispose();
    }
}
