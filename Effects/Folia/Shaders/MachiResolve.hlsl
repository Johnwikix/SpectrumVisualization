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

Texture2D<float4> Reconstructed : register(t12);
Texture2D<float4> Raw : register(t13);

[numthreads(8, 8, 1)]
void ResolveRayReconstruction(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Output.GetDimensions(width, height);
    if (any(id.xy >= uint2(width, height))) return;
    Output[id.xy] = float4(max(0, Deterministic[id.xy].rgb + Reconstructed[id.xy].rgb), 1);
}

[numthreads(8, 8, 1)]
void ResolveRaw(uint3 id : SV_DispatchThreadID)
{
    uint width, height;
    Output.GetDimensions(width, height);
    if (any(id.xy >= uint2(width, height))) return;
    Output[id.xy] = float4(max(0, Deterministic[id.xy].rgb + Raw[id.xy].rgb), 1);
}
