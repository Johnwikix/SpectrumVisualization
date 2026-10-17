using System;
using System.IO;
namespace WinExSpectrumTest.Rendering;
internal static class ColorOutputShaderSource
{
    internal const string Source = """
        Texture2D<float4> image : register(t0);
        SamplerState sampleImage : register(s0);
        cbuffer Output : register(b0) { float hdr; float whiteNits; float peakNits; float padding; float2 inverseSize; };
        void VS(uint id : SV_VertexID, out float4 position : SV_Position, out float2 uv : TEXCOORD0) {
            float2 xy = float2((id << 1) & 2, id & 2);
            position = float4(xy * 2 - 1, 0, 1); uv = float2(xy.x, 1 - xy.y);
        }
        float3 MapBrightness(float3 c) {
            c = max(c,0);
            if (hdr < .5) return saturate(c);
            c *= whiteNits;
            float high = max(c.r,max(c.g,c.b));
            if (high > whiteNits) {
                float headroom = max(peakNits-whiteNits,.001);
                float mapped = whiteNits + headroom*(1-exp(-(high-whiteNits)/headroom));
                c *= min(mapped,peakNits)/max(high,.001);
            }
            return c;
        }
        float3 EncodeSrgb(float3 c) {
            return lerp(c*12.92,1.055*pow(max(c,0),1.0/2.4)-.055,step(.0031308,c));
        }
        float3 PQ(float3 nits) {
            float3 y = pow(max(nits,0)/10000,2610.0/16384);
            return pow((3424.0/4096+(2413.0/128)*y)/(1+(2392.0/128)*y),2523.0/32);
        }
        float4 EncodeMapped(float3 c, float2 pixel) {
            if (hdr > .5) {
                c = float3(dot(c,float3(.627404,.329283,.0433136)),
                    dot(c,float3(.069097,.919540,.0113612)),dot(c,float3(.0163916,.0880132,.895595)));
                c = PQ(c);
            } else c = EncodeSrgb(c);
            float noise = frac(52.9829189*frac(dot(pixel,float2(.06711056,.00583715))))-.5;
            return float4(saturate(c+noise/1023),1);
        }
        float4 Encode(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            return EncodeMapped(MapBrightness(image.SampleLevel(sampleImage,uv,0).rgb),p.xy);
        }
        float4 PrepareAA(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float3 c = MapBrightness(image.SampleLevel(sampleImage,uv,0).rgb);
            c = sqrt(saturate(c/(hdr > .5 ? peakNits : 1)));
            return float4(c,dot(c,float3(.299,.587,.114)));
        }
        float4 EncodePrepared(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            float3 c = image.SampleLevel(sampleImage,uv,0).rgb;
            return EncodeMapped(c*c*(hdr > .5 ? peakNits : 1),p.xy);
        }
        """;

    private const string AntialiasSource = """
        float4 EncodeAA(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target {
            FxaaTex tex = { sampleImage, image };
            float minimumEdge = .0312 * (hdr > .5 ? sqrt(whiteNits/peakNits) : 1);
            float3 c = FxaaPixelShader(uv,0,tex,tex,tex,tex,inverseSize,0,0,0,.5,.166,minimumEdge,0,0,0,0).rgb;
            return EncodeMapped(c*c*(hdr > .5 ? peakNits : 1),p.xy);
        }
        """;

    internal static string Fxaa()
    {
        using var stream = typeof(ColorOutputShaderSource).Assembly.GetManifestResourceStream("Spectrum.Fxaa3_11.h")
            ?? throw new InvalidOperationException("The embedded FXAA shader is missing.");
        using var reader = new StreamReader(stream);
        return "#define FXAA_PC 1\n#define FXAA_HLSL_5 1\n#define FXAA_QUALITY__PRESET 12\n" + reader.ReadToEnd() + "\n" + Source + AntialiasSource;
    }
}
