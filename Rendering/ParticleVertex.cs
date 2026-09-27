using System.Numerics;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct ParticleVertex(Vector2 position, Vector4 color)
{
    public readonly Vector2 Position = position;
    public readonly Vector4 Color = color;
}
