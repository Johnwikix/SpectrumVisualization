using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace WinExSpectrumTest.Effects.Folia;

internal enum MachiDxrSignal
{
    Diffuse, DiffuseSh, Specular, SpecularSh, Normal, ViewZ, Motion, DiffuseFactor, SpecularFactor, Deterministic,
    Reflection, ReflectionNormal, ReflectionZ, ReflectionMotion, Transmission, TransmissionNormal, TransmissionZ,
    TransmissionMotion, Noisy, Albedo, SpecularAlbedo, LinearNormal, Depth, SpecularDistance,
    ReconstructionDepth, ReconstructionMotion, Count
}

internal static class MachiDxrShaders
{
    private static readonly Lazy<string> TraceSource = new(BuildTrace);
    private static readonly Lazy<string> InlineTraceSource = new(() => "#define MACHI_INLINE_TRACE 1\n" + Trace);
    private static readonly Lazy<string> ResolveSource = new(() => WithNrd(FoliaShaderLibrary.Load("MachiResolve")));
    internal static string Trace => TraceSource.Value;
    internal static string InlineTrace => InlineTraceSource.Value;
    internal static string Resolve => ResolveSource.Value;

    internal static int SignalComponents(MachiDxrSignal signal) => signal switch
    {
        MachiDxrSignal.ViewZ or MachiDxrSignal.ReflectionZ or MachiDxrSignal.TransmissionZ or
        MachiDxrSignal.Depth or MachiDxrSignal.SpecularDistance or MachiDxrSignal.ReconstructionDepth => 1,
        MachiDxrSignal.Motion or MachiDxrSignal.ReflectionMotion or MachiDxrSignal.TransmissionMotion or
        MachiDxrSignal.ReconstructionMotion => 2,
        _ => 4
    };

    private static string BuildTrace()
    {
        string source = FoliaShaderLibrary.Load("MachiTrace");
        ValidateTraceBindings(source);
        string microfacet = Resource("RTXPT", "Microfacet").Replace("#include \"BxDFConfig.hlsli\"", Resource("RTXPT", "BxDFConfig"));
        return WithNrd(source).Replace("#include \"Microfacet.hlsli\"", microfacet)
            .Replace("#include \"NoiseAndSequences.hlsli\"", Resource("RTXPT", "NoiseAndSequences"));
    }

    // The same contract runs during formal precompilation and source fallback.
    private static void ValidateTraceBindings(string source)
    {
        Span<bool> seen = stackalloc bool[(int)MachiDxrSignal.Count];
        seen.Clear();
        foreach (Match declaration in Regex.Matches(source,
            @"RWTexture2D<float(?<components>[234]?)>\s+(?<name>\w+)\s*:\s*register\(u(?<slot>\d+)\)\s*;",
            RegexOptions.CultureInvariant))
        {
            int slot = int.Parse(declaration.Groups["slot"].Value, CultureInfo.InvariantCulture);
            string name = declaration.Groups["name"].Value;
            string componentText = declaration.Groups["components"].Value;
            int components = componentText.Length == 0 ? 1 : componentText[0] - '0';
            var signal = (MachiDxrSignal)slot;
            if ((uint)slot >= (uint)seen.Length || seen[slot] || Enum.GetName(signal) != name || SignalComponents(signal) != components)
                throw new InvalidDataException($"Machi signal {name} does not match u{slot} with {components} components.");
            seen[slot] = true;
        }
        if (seen.Contains(false)) throw new InvalidDataException("Machi trace is missing a signal binding.");
    }

    private static string WithNrd(string source)
    {
        string nrd = Resource("NRD", "NRD").Replace("#include \"NRDConfig.hlsli\"", Resource("NRD", "NRDConfig"));
        return source.Replace("#include \"NRD.hlsli\"", nrd);
    }

    private static string Resource(string library, string name)
    {
        using var stream = typeof(MachiDxrShaders).Assembly.GetManifestResourceStream($"Spectrum.{library}.{name}.hlsli")
            ?? throw new InvalidDataException($"Missing {library} shader {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
