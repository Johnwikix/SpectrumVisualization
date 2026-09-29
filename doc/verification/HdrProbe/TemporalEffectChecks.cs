using ComputeSharp;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Effects.Sonic;

internal sealed unsafe partial class SonicGpuEffect
{
    internal void SeedForProbe() => ProbeRandom(_simulation) = new Random(7319);

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_random")]
    private static extern ref Random ProbeRandom(SonicTopographyEffect effect);

    internal ID3D12Resource PreviousForProbe => _previousField!;
    internal ID3D12Resource FieldForProbe => _field!;
    internal void VerifyStaticConstants()
    {
        for (int i = 0; i < 4; i++)
        {
            var current = _constantData[i];
            var previous = _constantData[i + 13];
            if (current.X != previous.X || current.Y != previous.Y || current.Z != previous.Z || current.W != previous.W)
                throw new InvalidOperationException("Static probe camera changed");
        }
    }
}
