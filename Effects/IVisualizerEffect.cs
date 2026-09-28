using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Services;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects
{
    /// <summary>
    /// Shared services handed to Win2D effects on initialization.
    /// </summary>
    public sealed class VisualizerServices
    {
        public required CanvasAnimatedControl Control { get; init; }
        public required SpectrumAnalyzer Analyzer { get; init; }
        public required MediaInfoService Media { get; init; }
    }

    /// <summary>
    /// A Win2D visualizer. Effects live on the CanvasAnimatedControl render
    /// thread: <see cref="Update"/> and <see cref="Draw"/> are called once per frame and
    /// must not allocate (all buffers are expected to be preallocated by the effect).
    /// </summary>
    public interface IVisualizerEffect : IDisposable
    {
        /// <summary>Stable identifier used for persistence.</summary>
        string Id { get; }

        /// <summary>Localized display name.</summary>
        string DisplayName { get; }

        /// <summary>Subscribe to shared services. Called once, before any Update/Draw.</summary>
        void Initialize(VisualizerServices services);

        /// <summary>Called when the canvas size changes (and once after initialization).</summary>
        void OnResize(float width, float height);

        /// <summary>Per-frame simulation. Runs on the render thread.</summary>
        void Update(double elapsedSeconds);

        /// <summary>Per-frame rendering. Runs on the render thread.</summary>
        void Draw(Microsoft.Graphics.Canvas.CanvasDrawingSession session, float width, float height);
    }

    /// <summary>
    /// Registry of available effect pages.
    /// </summary>
    public static class EffectRegistry
    {
        public const string DefaultEffectId = AuroraRingEffect.EffectId;

        private static readonly EffectDescriptor[] Effects =
        [
            new(AuroraRingEffect.EffectId, "AuroraEffectName", EffectHost.Win2D, false, null, null),
            new(SonicTopographyEffect.EffectId, "SonicEffectName", EffectHost.Gpu, true,
                static () => new SonicGpuEffect(), static () =>
                {
                    var quality = AppSettings.SonicQuality;
                    var mode = quality.AntiAliasing switch
                    {
                        "fxaa" => ReconstructionMode.Fxaa, "smaa" => ReconstructionMode.Smaa,
                        "xess" => ReconstructionMode.XeSS, "fsr" => ReconstructionMode.Fsr,
                        "dlss" => ReconstructionMode.Dlss, _ => ReconstructionMode.Off
                    };
                    return new GpuSceneOptions(quality.RenderScalePercent, mode, quality.GridSize, quality.DlssPreset);
                })
        ];

        internal static EffectDescriptor Resolve(string? id)
        {
            foreach (var effect in Effects)
                if (effect.Id == id) return effect;
            return Effects[0];
        }

        /// <returns>(id, localized display name) of every registered effect, in display order.</returns>
        public static (string Id, string DisplayName)[] GetCatalog()
        {
            // Catalog queries must not construct effects or create graphics devices.
            var result = new (string Id, string DisplayName)[Effects.Length];
            var resources = new ResourceLoader();
            for (int i = 0; i < result.Length; i++)
                result[i] = (Effects[i].Id, resources.GetString(Effects[i].ResourceKey));
            return result;
        }

        public static string GetDisplayName(string id) => new ResourceLoader().GetString(Resolve(id).ResourceKey);

    }

    /// <summary>Identifies the rendering API required by an effect.</summary>
    internal enum EffectHost { Win2D, Gpu }

    /// <summary>Registers an effect without constructing graphics devices while listing the catalog.</summary>
    internal sealed record EffectDescriptor(string Id, string ResourceKey, EffectHost Host, bool UsesMedia,
        Func<IGpuVisualizerEffect>? CreateGpu, Func<GpuSceneOptions>? GetSceneOptions);
}
