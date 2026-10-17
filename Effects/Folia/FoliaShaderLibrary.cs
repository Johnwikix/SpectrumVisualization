using System;
using System.IO;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Only used while building PSOs; resource names are explicit and survive trimming.</summary>
internal static class FoliaShaderLibrary
{
    internal static string Load(string name)
    {
        using var stream = typeof(FoliaShaderLibrary).Assembly.GetManifestResourceStream("Spectrum.Folia." + name + ".hlsl")
            ?? throw new InvalidOperationException("Missing Folia shader: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static string Background(string name) => Load("Shared") + "\n" + Load(name);
}
