using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Effects.Sonic;
using WinExSpectrumTest.Services;

namespace WinExSpectrumTest.Effects
{
    /// <summary>
    /// Shared services handed to every visualizer effect on initialization.
    /// </summary>
    public sealed class VisualizerServices
    {
        public required CanvasAnimatedControl Control { get; init; }
        public required SpectrumAnalyzer Analyzer { get; init; }
        public required MediaInfoService Media { get; init; }
    }

    /// <summary>
    /// A visualizer "effect page". Effects live on the CanvasAnimatedControl render
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

        /// <returns>(id, localized display name) of every registered effect, in display order.</returns>
        public static (string Id, string DisplayName)[] GetCatalog()
        {
            // Constant strings only: instantiating an effect here would allocate its
            // GPU resources (PixelShaderEffects) just to read a name.
            return
            [
                (AuroraRingEffect.EffectId, AuroraRingEffect.DisplayNameConst),
                (SonicTopographyEffect.EffectId, SonicTopographyEffect.DisplayNameConst),
            ];
        }

        public static IVisualizerEffect Create(string id)
        {
            return id switch
            {
                SonicTopographyEffect.EffectId => new SonicTopographyEffect(),
                _ => new AuroraRingEffect(),
            };
        }
    }
}
