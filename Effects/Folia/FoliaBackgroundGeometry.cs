using System;
using System.Numerics;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Small instanced primitives keep Sora's cost proportional to stars rather than viewport pixels.</summary>
internal sealed class FoliaBackgroundGeometry
{
    private readonly Vector4[] _shapes = new Vector4[15];
    private readonly Vector4[] _motion = new Vector4[15];
    private readonly Vector4[] _particles = new Vector4[20];
    private readonly Vector2[] _shapeTiming = new Vector2[15];
    private readonly Vector2[] _particleTiming = new Vector2[20];
    private float _energy;
    private readonly float[] _bands = new float[4];

    internal FoliaBackgroundGeometry()
    {
        var random = new Random(1729);
        for (int i = 0; i < _shapes.Length; i++)
        {
            _shapes[i] = new(random.NextSingle(), random.NextSingle(), 40 + random.NextSingle() * 100, random.Next(4));
            _motion[i] = new(30 + random.NextSingle() * 30, .11f + random.NextSingle() * .08f, random.NextSingle() * MathF.Tau, random.NextSingle() < .3f ? 1 : 0);
            _shapeTiming[i] = new(random.NextSingle() * 5, random.NextSingle() > .5f ? 1 : -1);
        }
        for (int i = 0; i < _particles.Length; i++)
        {
            _particles[i] = new(random.NextSingle(), random.NextSingle(), random.NextSingle() * 4 + 1, random.NextSingle() * .3f);
            _particleTiming[i] = new(15 + random.NextSingle() * 20, random.NextSingle() * 10);
        }
    }

    internal int Write(Span<FoliaInstance> instances, FoliaSong song, int background, float time, float energy, float width, float height, bool paused = false, ReadOnlySpan<float> features = default)
    {
        var painter = new FoliaPainter(instances, width, height);
        if (!paused) _energy = energy;
        energy = _energy;
        if (!paused && features.Length >= FeatureIndex.Count)
        {
            _bands[0] = features[FeatureIndex.Bass];
            _bands[1] = features[FeatureIndex.LowMid];
            _bands[2] = features[FeatureIndex.Mid];
            _bands[3] = (features[FeatureIndex.Brilliance] + features[FeatureIndex.Air]) * .5f;
        }
        if (background == 0)
        {
            for (int i = 0; i < _shapes.Length; i++)
            {
                Vector4 shape = _shapes[i], motion = _motion[i];
                float phase = Math.Max(0, time - _shapeTiming[i].X) / motion.X;
                phase -= MathF.Floor(phase);
                float swing = phase <= .5f ? phase * 4 - 1 : 3 - phase * 4;
                float x = shape.X * width + swing * 15 * _shapeTiming[i].Y;
                float y = shape.Y * height - swing * 30 * _shapeTiming[i].Y;
                float size = shape.Z * (.95f + Math.Clamp((_bands[(int)shape.W] * 255 - 10) / 190, 0, 1) * .5f);
                int kind = (int)shape.W switch
                {
                    0 => motion.W > 0 ? 16 : 2,
                    1 => motion.W > 0 ? 1 : 7,
                    2 => 8,
                    _ => 9
                };
                painter.Shape(kind, x, y, size, size, song.Palette[2], motion.Y, motion.Z + phase * MathF.Tau, 1);
            }
            for (int i = 0; i < _particles.Length; i++)
            {
                var particle = _particles[i];
                float phase = Math.Max(0, time - _particleTiming[i].Y) / _particleTiming[i].X;
                phase -= MathF.Floor(phase);
                float y = particle.Y * height - phase * 100;
                float alpha = particle.W * (1 - Math.Abs(phase * 2 - 1));
                painter.Shape(16, particle.X * width, y, particle.Z, particle.Z, song.Palette[3], alpha);
            }
        }
        return painter.Count;
    }
}
