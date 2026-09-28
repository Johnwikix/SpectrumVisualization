using System.Runtime.CompilerServices;
using WinExSpectrumTest.Audio;

/// <summary>Injects deterministic stress input only into the probe's otherwise inactive analyzer.</summary>
internal static class ProbeSignal
{
    internal static void Update(SpectrumAnalyzer analyzer, int frame, bool triggers)
    {
        var data = Features(analyzer);
        for (int i = 0; i < data.Length; i++)
            data[i] = .55f + .4f * MathF.Sin(frame * .037f + i * .35f);
        if (!triggers) return;
        PulseStrength(analyzer) = .8f;
        MeteorStrength(analyzer) = .8f;
        if (frame % 20 == 0) PulseCount(analyzer)++;
        if (frame % 360 == 0) MeteorCount(analyzer)++;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_featuresFront")]
    private static extern ref float[] Features(SpectrumAnalyzer analyzer);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_pulseTriggerStrength")]
    private static extern ref float PulseStrength(SpectrumAnalyzer analyzer);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_meteorTriggerStrength")]
    private static extern ref float MeteorStrength(SpectrumAnalyzer analyzer);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_pulseTriggerCount")]
    private static extern ref int PulseCount(SpectrumAnalyzer analyzer);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_meteorTriggerCount")]
    private static extern ref int MeteorCount(SpectrumAnalyzer analyzer);
}
