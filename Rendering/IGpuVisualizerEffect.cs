using ComputeSharp;
using System;
using Vortice.Direct3D12;
using WinExSpectrumTest.Audio;

namespace WinExSpectrumTest.Rendering;

/// <summary>Describes the output and internal scene sizes in physical pixels.</summary>
internal readonly record struct GpuRenderSize(int OutputWidth, int OutputHeight, int Width, int Height)
{
    public static GpuRenderSize Create(int width, int height, int scale) => new(width, height,
        Math.Max(1, (int)((long)width * Math.Clamp(scale, 1, 100) / 100)),
        Math.Max(1, (int)((long)height * Math.Clamp(scale, 1, 100) / 100)));
}

/// <summary>Provides borrowed devices and the application's existing analyzer.</summary>
internal readonly record struct GpuEffectServices(GraphicsDevice Compute, ID3D12Device Device, SpectrumAnalyzer Analyzer);

/// <summary>Records an opaque linear Rec.709 scene on the owning render thread.</summary>
/// <remarks>The host drains both queues before resize, replacement or disposal. Effects never present or release borrowed devices.</remarks>
internal interface IGpuVisualizerEffect : IDisposable
{
    /// <summary>Allocates effect resources on the render thread.</summary>
    void Initialize(in GpuEffectServices services);
    /// <summary>Rebuilds size-dependent resources after the previous frame completes.</summary>
    void Resize(in GpuRenderSize size);
    /// <summary>Updates the simulation and completes compute work before direct rendering.</summary>
    void PrepareFrame(double elapsedSeconds, int detail);
    /// <summary>Records drawing into the supplied FP16 target without submitting the borrowed command list.</summary>
    void RecordScene(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target);
}
