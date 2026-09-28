using ComputeSharp;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Effects.Sonic;

internal sealed unsafe partial class SonicGpuEffect
{
    internal ID3D12Resource PreviousForProbe => _previousField!;
    internal ID3D12Resource FieldForProbe => _field!;
    internal void VerifyStaticConstants()
    {
        for (int i = 0; i < 4; i++)
        {
            float4 delta = _constantData[i] - _constantData[i + 13];
            if (delta.X != 0 || delta.Y != 0 || delta.Z != 0 || delta.W != 0)
                throw new InvalidOperationException("Static probe camera changed");
        }
    }
}
