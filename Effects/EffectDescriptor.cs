using System;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects;

/// <summary>Identifies the rendering API required by an effect.</summary>
internal enum EffectHost { Win2D, Gpu }

/// <summary>Registers an effect without constructing graphics devices while listing the catalog.</summary>
internal sealed record EffectDescriptor(string Id, string ResourceKey, EffectHost Host, bool UsesMedia,
    Func<IGpuVisualizerEffect>? CreateGpu, Func<GpuSceneOptions>? GetSceneOptions);
