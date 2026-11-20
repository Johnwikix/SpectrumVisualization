#include "NRD.hlsli"

Texture2D<float4> Diffuse : register(t0);
Texture2D<float4> DiffuseSh : register(t1);
Texture2D<float4> Specular : register(t2);
Texture2D<float4> SpecularSh : register(t3);
Texture2D<float4> DiffuseFactor : register(t4);
Texture2D<float4> SpecularFactor : register(t5);
Texture2D<float4> Deterministic : register(t6);
Texture2D<float4> Reflection : register(t7);
Texture2D<float4> Transmission : register(t8);
Texture2D<float> ViewZ : register(t9);
Texture2D<float> ReflectionZ : register(t10);
Texture2D<float> TransmissionZ : register(t11);
RWTexture2D<float4> Output : register(u0);

[numthreads(8, 8, 1)]
void Resolve(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Output.GetDimensions(width, height);
    uint2 p = id.xy;
    if (any(p >= uint2(width, height))) return;
    float3 color = Deterministic[p].rgb;
    if (abs(ViewZ[p]) < 10000)
    {
        NRD_SG diffuse = RELAX_BackEnd_UnpackSh(Diffuse[p], DiffuseSh[p].xyz);
        NRD_SG specular = RELAX_BackEnd_UnpackSh(Specular[p], SpecularSh[p].xyz);
        color += NRD_SG_ExtractColor(diffuse) * DiffuseFactor[p].rgb;
        color += NRD_SG_ExtractColor(specular) * SpecularFactor[p].rgb;
    }
    if (abs(ReflectionZ[p]) < 10000) color += RELAX_BackEnd_UnpackRadiance(Reflection[p]).rgb;
    if (abs(TransmissionZ[p]) < 10000) color += RELAX_BackEnd_UnpackRadiance(Transmission[p]).rgb;
    Output[p] = float4(max(0, color), 1);
}

Texture2D<float4> ResolvedInput : register(t12);
Texture2D<float4> Raw : register(t13);

[numthreads(8, 8, 1)]
void ResolveScale(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Output.GetDimensions(width, height);
    if (any(id.xy >= uint2(width, height))) return;
    uint inputWidth, inputHeight;
    ResolvedInput.GetDimensions(inputWidth, inputHeight);
    float2 source = (float2(id.xy) + .5) / float2(width, height) * float2(inputWidth, inputHeight) - .5;
    int2 p = int2(floor(source));
    int2 edge = int2(inputWidth, inputHeight) - 1;
    float2 fraction = frac(source);
    float3 a = ResolvedInput.Load(int3(clamp(p, 0, edge), 0)).rgb;
    float3 b = ResolvedInput.Load(int3(clamp(p + int2(1, 0), 0, edge), 0)).rgb;
    float3 c = ResolvedInput.Load(int3(clamp(p + int2(0, 1), 0, edge), 0)).rgb;
    float3 d = ResolvedInput.Load(int3(clamp(p + 1, 0, edge), 0)).rgb;
    Output[id.xy] = float4(lerp(lerp(a, b, fraction.x), lerp(c, d, fraction.x), fraction.y), 1);
}

[numthreads(8, 8, 1)]
void ResolveRaw(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Output.GetDimensions(width, height);
    if (any(id.xy >= uint2(width, height))) return;
    Output[id.xy] = float4(max(0, Raw[id.xy].rgb), 1);
}
