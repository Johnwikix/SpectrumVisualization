using System;
using System.IO;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Only used while building PSOs; resource names are explicit and survive trimming.</summary>
internal static class FoliaShaderLibrary
{
    internal static string Load(string name)
    {
        if (name == "GlyphGenerator") return Read(name) + "\n" + Read("GlyphCorrection");
        return Read(name);
    }

    private static string Read(string name)
    {
        using var stream = typeof(FoliaShaderLibrary).Assembly.GetManifestResourceStream("Spectrum.Folia." + name + ".hlsl")
            ?? throw new InvalidOperationException("Missing Folia shader: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static string Background(string name)
    {
        string shared = Load("Shared"), source = Load(name);
        // Latent's two source canvases stay unjittered; their final scaled composite samples once.
        if (name is "Stage" or "Sora" or "Print" or "mesh-gradient" or "dithering") return shared + "\n" + source;
        // Backgrounds evaluate at the same subpixel location as geometry. Post effects must
        // preserve those samples, rather than shifting an already rasterized picture again.
        return shared + "\n#define PS UnjitteredPS\n" + source
            + "\n#undef PS\nfloat4 PS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target { p.xy-=Jitter.xy; return UnjitteredPS(p,uv-Jitter.xy/Screen.xy); }";
    }
}
