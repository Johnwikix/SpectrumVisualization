using System.Numerics;
using System.Runtime.InteropServices;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Prepared orthographic Machi scene geometry, signs and playback inputs for DXR.</summary>
internal sealed partial class FoliaMachi
{
    internal const int PanelWidth = 1024;
    internal const int PanelHeight = 560;
    // Scene-linear radiance of a white LED pixel before the diffuser and day/night dimmer.
    internal const float BoardWhiteRadiance = 24;
    internal const float Near = .1f;
    internal const float Far = 200;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct Vertex(Vector4 PositionMaterial, Vector4 Color, Vector4 NormalUvX, Vector4 UvY);

    internal Vertex[] Mesh { get; }
}
