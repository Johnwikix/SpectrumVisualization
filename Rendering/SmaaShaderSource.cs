using System;
using System.IO;
namespace WinExSpectrumTest.Rendering;
internal static class SmaaShaderSource
{
    // Source color is perceptual, brightness-mapped FP16. Encoding remains the host's responsibility.
    private const string Prefix = """
        #define SMAA_HLSL_4 1
        // HIGH preset with brightness-relative edge sensitivity for HDR.
        #define SMAA_MAX_SEARCH_STEPS 16
        #define SMAA_MAX_SEARCH_STEPS_DIAG 8
        #define SMAA_CORNER_ROUNDING 25
        #define SMAA_THRESHOLD (.1 * edgeScale)
        cbuffer Settings : register(b0) { float4 metrics; float edgeScale; };
        #define SMAA_RT_METRICS metrics
        """;
    private const string Suffix = """
        Texture2D colorTex : register(t0);
        Texture2D edgesTex : register(t1);
        Texture2D blendTex : register(t2);
        Texture2D areaTex : register(t3);
        Texture2D searchTex : register(t4);
        void VS(uint id : SV_VertexID, out float4 position : SV_Position, out float2 uv : TEXCOORD0) {
            float2 xy = float2((id << 1) & 2, id & 2);
            position = float4(xy * 2 - 1, 0, 1); uv = float2(xy.x, 1 - xy.y);
        }
        float2 Edges(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float4 offsets[3]; SMAAEdgeDetectionVS(uv,offsets);
            return SMAAColorEdgeDetectionPS(uv,offsets,colorTex);
        }
        float4 Weights(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float2 pixel; float4 offsets[3]; SMAABlendingWeightCalculationVS(uv,pixel,offsets);
            return SMAABlendingWeightCalculationPS(uv,pixel,offsets,edgesTex,areaTex,searchTex,0);
        }
        float4 Blend(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float4 offset; SMAANeighborhoodBlendingVS(uv,offset);
            return SMAANeighborhoodBlendingPS(uv,offset,colorTex,blendTex);
        }
        """;

    internal static string Load()
    {
        using var stream = typeof(SmaaShaderSource).Assembly.GetManifestResourceStream("Spectrum.SMAA.SMAA.hlsl")
            ?? throw new InvalidOperationException("The embedded SMAA shader is missing.");
        using var reader = new StreamReader(stream);
        return Prefix + "\n" + reader.ReadToEnd() + "\n" + Suffix;
    }
}
