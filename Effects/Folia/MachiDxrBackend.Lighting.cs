using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class MachiDxrBackend
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct AnalyticLight(Vector4 PositionRange, Vector4 DirectionOuter, Vector4 RadianceInner,
        uint Alias, float Threshold, float Probability, uint Padding);

    private ID3D12Resource _analyticLights = null!;
    private int _analyticCount;
    private float _analyticFraction;

    private void InitializeAnalyticLights(ReadOnlySpan<FoliaMachi.Light> lights)
    {
        if (lights.IsEmpty)
        {
            _analyticLights = Upload<AnalyticLight>([default]);
            return;
        }

        var weights = new float[lights.Length];
        float total = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            float luminance = Vector3.Dot(light.Color, new(.2126f, .7152f, .0722f));
            float solidAngle = light.CosOuter < 0 ? 4 : 2 * (1 - light.CosOuter);
            float weight = Math.Max(0, luminance * light.Intensity * solidAngle);
            weights[i] = weight;
            total += weight;
        }
        if (total <= 0)
        {
            _analyticLights = Upload<AnalyticLight>([default]);
            return;
        }
        _analyticCount = lights.Length;
        _analyticFraction = _emitterWeight > 0 ? Math.Clamp(total / (total + _emitterWeight), .15f, .95f) : 1;
        float inverseWeight = 1 / total;
        AliasEntry[] entries = BuildAliasTable(weights, inverseWeight);
        var gpuLights = new AnalyticLight[lights.Length];
        for (int i = 0; i < gpuLights.Length; i++)
        {
            var light = lights[i];
            gpuLights[i] = new(new(light.Position, light.Range), new(light.Direction, light.CosOuter),
                new(light.Color * light.Intensity, light.CosInner), entries[i].Alias, entries[i].Threshold, weights[i] * inverseWeight, 0);
        }
        _analyticLights = Upload<AnalyticLight>(gpuLights);
    }
}
