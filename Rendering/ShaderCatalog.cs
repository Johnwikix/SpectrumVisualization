using System;
using WinExSpectrumTest.Effects.Folia;
using WinExSpectrumTest.Effects.Sonic;
namespace WinExSpectrumTest.Rendering;

/// <summary>All built-in HLSL entry points; used by both formal precompilation and manual cache rebuilds.</summary>
internal static class ShaderCatalog
{
    internal static void ForEach(Action<string, string, string> compile)
    {
        void Pair(string source, string vs, params string[] entries)
        {
            compile(source, vs, "vs_5_0");
            foreach (string entry in entries) compile(source, entry, "ps_5_0");
        }
        Pair(ColorOutputShaderSource.Source, "VS", "Encode", "PrepareAA", "EncodePrepared");
        compile(ColorOutputShaderSource.Fxaa(), "EncodeAA", "ps_5_0");
        Pair(SmaaShaderSource.Load(), "VS", "Edges", "Weights", "Blend");
        Pair(SonicTerrainSource.Code, "TerrainVS", "TerrainPS", "TerrainTemporalPS");
        Pair(SonicTerrainSource.Code, "ParticleVS", "ParticlePS");
        Pair(FoliaShaderLibrary.Background("Stage"), "Fullscreen", "PS", "CopyPS", "TemporalPS", "VignettePS", "LatentCompositePS");
        Pair(FoliaShaderLibrary.Background("Stage"), "SpriteVS", "SpritePS");
        Pair(FoliaShaderLibrary.Background("Stage"), "ClockworkVS", "ClockworkPS");
        foreach (string name in new[] { "mesh-gradient", "dithering", "image-dithering", "fluted-glass", "paper-texture", "halftone-dots", "lens-distortion", "Lumiere", "Print", "Sonnet" })
            Pair(FoliaShaderLibrary.Background(name), "Fullscreen", "PS");
        Pair(FoliaShaderLibrary.Background("Sora"), "VS", "PS");
        Pair(FoliaShaderLibrary.Load("Bloom"), "VS", "Down", "Up", "Combine");
        foreach (string entry in new[] { "GenerateMtsdf", "CorrectMtsdf", "ShadowHorizontal", "ShadowVertical" })
            compile(FoliaShaderLibrary.Load("GlyphGenerator"), entry, "cs_5_0");
    }
}
