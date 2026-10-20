using System;
using System.Numerics;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private float _lensBulge = float.NaN, _lensSpread = float.NaN, _lensOverscan;
    /// <summary>Publishes validated tuning to the expanded native constant-buffer contract.</summary>
    private void WriteTuning()
    {
        _data[30] = new(AppSettings.FoliaTemperaGrain, AppSettings.FoliaTemperaContrast, AppSettings.FoliaTemperaRgbShift, AppSettings.FoliaTemperaVignette);
        _data[31] = new(AppSettings.FoliaTemperaLens, AppSettings.FoliaNomandOverlay ? AppSettings.FoliaNomandOverlayOpacity : 0, 0, 0);
        _data[32] = new(AppSettings.FoliaNomandSize, MathF.Round(AppSettings.FoliaNomandSteps), AppSettings.FoliaNomandOriginal ? 1 : 0, AppSettings.FoliaNomandInverted ? 1 : 0);
        _data[33] = new(AppSettings.FoliaNomandGlassSize, AppSettings.FoliaNomandGlassDistortion, AppSettings.FoliaNomandGlassBlur, 0);
        float normal = 2 * .65f + 1.5f * .35f + .2f * .2f + .75f * AppSettings.FoliaNomandPaperRoughness + .1f * AppSettings.FoliaNomandPaperFiber;
        _data[34] = new(AppSettings.FoliaNomandPaperContrast, AppSettings.FoliaNomandPaperRoughness, AppSettings.FoliaNomandPaperFiber, Overscan(1 / (1 - 2 * Math.Min(.02f * normal, .45f))));
        _data[35] = new(AppSettings.FoliaNomandDotSize, AppSettings.FoliaNomandDotRadius, AppSettings.FoliaNomandDotContrast, 0);
        float bulge = AppSettings.FoliaNomandLensBulge, spread = AppSettings.FoliaNomandLensSpread;
        if (bulge != _lensBulge || spread != _lensSpread)
        {
            _lensOverscan = LensOverscan();
            _lensBulge = bulge;
            _lensSpread = spread;
        }
        _data[36] = new(spread, bulge, AppSettings.FoliaNomandLensDispersion, _lensOverscan);
        _data[37] = new(AppSettings.FoliaNomandDotOriginal ? 1 : 0, AppSettings.FoliaNomandDotInverted ? 0 : 1, AppSettings.FoliaLatentMesh ? 1 : 0, AppSettings.FoliaLatentDither ? 1 : 0);
    }

    private static float Overscan(float scale) => scale <= 1 ? 1 : Math.Min(1.8f, scale + .01f);
    private static float LensOverscan()
    {
        const float radius = 1.01986443f; // .5 * hypot(16/9, 1), the upstream reference aspect.
        float spread = AppSettings.FoliaNomandLensSpread;
        float reach = .7f * MathF.Pow(spread, 1.3f + 2.7f * spread);
        float room = Math.Max(.35f, 1 - reach * .2f / radius);
        float bulge = AppSettings.FoliaNomandLensBulge * 1.4f;
        if (bulge <= 0) return Overscan(1 / room);
        float low = Math.Max(1, 2 * radius * bulge / 1.45f), high = 1.8f;
        for (int i = 0; i < 32; i++)
        {
            float mid = (low + high) * .5f, r = 2 * radius / mid;
            float push = MathF.Tan(r * bulge) / MathF.Tan(bulge) / r;
            if (push > mid * room) low = mid;
            else high = mid;
        }
        return Overscan(high);
    }
}
