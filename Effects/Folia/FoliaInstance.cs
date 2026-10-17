using System.Numerics;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Six float4s match the shader StructuredBuffer element exactly.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FoliaInstance
{
    internal Vector4 Rect;
    internal Vector4 Uv;
    internal Vector4 Color;
    internal Vector4 Transform;
    internal Vector4 Shape;
    internal Vector4 Effect;
}
