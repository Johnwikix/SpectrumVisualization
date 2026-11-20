#include "NRD.hlsli"
#include "Microfacet.hlsli"
#include "NoiseAndSequences.hlsli"

struct Vertex { float4 positionMaterial, color, normalU, v; };
struct Hit { float t; uint primitive; float2 bary; uint instance; };
struct Surface
{
    float3 p, previousP, n, previousNormal, face, base, emission;
    float roughness, metal, waterRoughness, wetCoverage;
    uint material, entering;
};
struct Plane
{
    float3 position, previousPosition, normal, color, deterministic;
    float3 albedo, specularAlbedo;
    float hitDistance, roughness;
};

cbuffer Camera : register(b0)
{
    row_major float4x4 InverseViewProjection;
    row_major float4x4 ViewProjection;
    row_major float4x4 PreviousViewProjection;
    row_major float4x4 WorldToView;
    float4 SkyWetness, SunDirectionFrame, SunRadianceFar, SizeJitter, Lighting, SkyBottom;
    float2 AnalyticLightControl;
    float NoVacancy;
    float4 HemiSkyRain, HemiGroundTime, CityGlow, Puddles;
}

RaytracingAccelerationStructure Scene : register(t0);
StructuredBuffer<Vertex> Vertices : register(t1);
StructuredBuffer<uint> Indices : register(t2);
Texture2D<float4> Lyrics : register(t3);
Texture2D<float4> Sign : register(t4);
struct SceneInstance
{
    row_major float4x4 current;
    row_major float4x4 previous;
    uint firstIndex;
    uint kind;
    uint2 padding;
};
struct SceneEmitter { uint instance, primitive, alias; float threshold, areaPdf; };
struct AnalyticEmitter
{
    float4 positionRange, directionOuter, radianceInner;
    uint alias;
    float threshold, probability;
    uint padding;
};
StructuredBuffer<SceneInstance> Instances : register(t5);
StructuredBuffer<SceneEmitter> Emitters : register(t6);
StructuredBuffer<float> EmitterPdf : register(t7);
StructuredBuffer<AnalyticEmitter> AnalyticLights : register(t8);
SamplerState LinearClamp : register(s0);
RWTexture2D<float4> Diffuse : register(u0);
RWTexture2D<float4> DiffuseSh : register(u1);
RWTexture2D<float4> Specular : register(u2);
RWTexture2D<float4> SpecularSh : register(u3);
RWTexture2D<float4> Normal : register(u4);
RWTexture2D<float> ViewZ : register(u5);
RWTexture2D<float2> Motion : register(u6);
RWTexture2D<float4> DiffuseFactor : register(u7);
RWTexture2D<float4> SpecularFactor : register(u8);
RWTexture2D<float4> Deterministic : register(u9);
RWTexture2D<float4> Reflection : register(u10);
RWTexture2D<float4> ReflectionNormal : register(u11);
RWTexture2D<float> ReflectionZ : register(u12);
RWTexture2D<float2> ReflectionMotion : register(u13);
RWTexture2D<float4> Transmission : register(u14);
RWTexture2D<float4> TransmissionNormal : register(u15);
RWTexture2D<float> TransmissionZ : register(u16);
RWTexture2D<float2> TransmissionMotion : register(u17);
RWTexture2D<float4> Noisy : register(u18);
RWTexture2D<float4> Albedo : register(u19);
RWTexture2D<float4> SpecularAlbedo : register(u20);
RWTexture2D<float4> LinearNormal : register(u21);
RWTexture2D<float> Depth : register(u22);
RWTexture2D<float> SpecularDistance : register(u23);
RWTexture2D<float> ReconstructionDepth : register(u24);
RWTexture2D<float2> ReconstructionMotion : register(u25);

static const float WaterEta = 1.0 / 1.333;
static const float WaterF0 = ((1.333 - 1.0) / (1.333 + 1.0)) * ((1.333 - 1.0) / (1.333 + 1.0));
static const float Pi = 3.141592653589793;
static const float Epsilon = .001;
static const uint OpticalLightSamples = 4;

uint Hash(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352d;
    x ^= x >> 15;
    x *= 0x846ca68b;
    return x ^ (x >> 16);
}
uint PathSeed(uint2 pixel, uint vertex, uint effect)
{
    return Hash32Combine(Hash32Combine(Hash32(pixel.x + 0x035f9f29u), pixel.y), vertex * 8u + effect);
}
float3 PathSample(uint2 pixel, uint vertex, uint effect)
{
    return burley_shuffled_scrambled_sobol_3D((uint)SunDirectionFrame.w, PathSeed(pixel, vertex, effect));
}
float3 BackgroundSky(float3 direction)
{
    return lerp(SkyBottom.rgb, SkyWetness.rgb, saturate(.5 + .5 * direction.y));
}
float3 EnvironmentRadiance(float3 direction)
{
    // This radiance lights the scene only along unoccluded secondary paths.
    return lerp(HemiGroundTime.rgb, HemiSkyRain.rgb, saturate(.5 + .5 * direction.y)) + CityGlow.rgb;
}
float3 FromLocal(float3 position, float3 normal)
{
    float3 tangent = normalize(cross(abs(normal.y) < .95 ? float3(0,1,0) : float3(1,0,0), normal));
    return tangent * position.x + cross(normal, tangent) * position.y + normal * position.z;
}
float3 ToLocal(float3 direction, float3 normal)
{
    float3 tangent = normalize(cross(abs(normal.y) < .95 ? float3(0,1,0) : float3(1,0,0), normal));
    return float3(dot(direction, tangent), dot(direction, cross(normal, tangent)), dot(direction, normal));
}
float3 CosineSample(float3 axis, float2 sample)
{
    float phi = 2 * Pi * sample.x;
    float z = sqrt(max(sample.y, 1e-6));
    float radius = sqrt(max(1 - z*z, 0));
    return FromLocal(float3(cos(phi) * radius, sin(phi) * radius, z), axis);
}
Hit Trace(float3 origin, float3 direction, float limit)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = direction;
    ray.TMin = Epsilon;
    ray.TMax = limit;
    Hit hit = {0, 0xffffffff, float2(0, 0), 0};
    TraceRay(Scene, RAY_FLAG_NONE, 0xff, 0, 0, 0, ray, hit);
    return hit;
}
[shader("closesthit")]
void ClosestHit(inout Hit hit, BuiltInTriangleIntersectionAttributes attrs)
{
    hit.t = RayTCurrent();
    hit.primitive = PrimitiveIndex();
    hit.bary = attrs.barycentrics;
    hit.instance = InstanceID();
}
[shader("miss")]
void Miss(inout Hit hit) {}

[shader("anyhit")]
void AnyHit(inout Hit hit, BuiltInTriangleIntersectionAttributes attrs)
{
    SceneInstance instance = Instances[InstanceID()];
    if (instance.kind == 5 && SkyBottom.w > 0)
    {
        float3 position = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
        if (any(abs(position.xz) > SkyBottom.w)) IgnoreHit();
        return;
    }
    uint offset = instance.firstIndex + PrimitiveIndex() * 3;
    Vertex a = Vertices[Indices[offset]];
    if ((uint)a.positionMaterial.w != 8) return;
    Vertex b = Vertices[Indices[offset + 1]], c = Vertices[Indices[offset + 2]];
    float2 uv = lerp(float2(a.normalU.w, a.v.x), float2(b.normalU.w, b.v.x), attrs.barycentrics.x)
        + (float2(c.normalU.w, c.v.x) - float2(a.normalU.w, a.v.x)) * attrs.barycentrics.y;
    if (Lyrics.SampleLevel(LinearClamp, uv, 0).a < .04) IgnoreHit();
}

uint TriangleOffset(Hit hit) { return Instances[hit.instance].firstIndex + hit.primitive * 3; }

bool GroundMaterial(uint material) { return material == 4 || material == 9; }

float PuddleHash(float2 cell)
{
    precise float phase = cell.x * 127.1 + cell.y * 311.7;
    return frac(sin(phase) * 43758.5453);
}
float PuddleNoise(float2 position)
{
    float2 cell = floor(position);
    float2 f = frac(position);
    float2 smooth = f * f * (3 - 2 * f);
    return lerp(lerp(PuddleHash(cell), PuddleHash(cell + float2(1, 0)), smooth.x),
        lerp(PuddleHash(cell + float2(0, 1)), PuddleHash(cell + 1), smooth.x), smooth.y);
}
float2 RainRipple(float2 position, float scale, float time, float salt)
{
    float2 gridPosition = position * scale;
    float2 cell = floor(gridPosition);
    float2 center = cell + .22 + .56 * float2(PuddleHash(cell + salt), PuddleHash(cell + salt + 7.13));
    float period = .75 + .9 * PuddleHash(cell + salt + 3.31);
    float progress = time / period + PuddleHash(cell + salt + 11.7);
    float phase = frac(progress);
    if (HemiSkyRain.w < 1 && PuddleHash(cell + salt + floor(progress) * .37) > HemiSkyRain.w) return 0;
    float2 delta = gridPosition - center;
    float distance = length(delta);
    float wave = distance - phase * .46;
    if (abs(wave) >= .07) return 0;
    float ring = 1 - smoothstep(0, .07, abs(wave));
    return (delta / max(distance, 1e-4)) * sin(wave * 48) * ring * (1 - phase);
}
void WetGround(inout Surface s)
{
    float wetness = SkyWetness.w * (1 - saturate(Lighting.y * 1.5));
    if (wetness <= 0 || s.n.y <= .5) return;
    float2 p = s.p.xz;
    float n = PuddleNoise(p * .62 + Puddles.xy) * .62 + PuddleNoise(p * 1.7 - Puddles.xy * 1.3) * .38;
    float edge = lerp(.78, .46, Puddles.z) + (s.material == 9 ? .08 : 0);
    float puddle = smoothstep(edge, edge + .05, n) * smoothstep(.15, .85, wetness);
    s.wetCoverage = wetness;
    // Rough film follows the pavement relief. Standing water has a narrower
    // microfacet distribution, with resolved waves carried by the normal.
    s.waterRoughness = lerp(.28, .055, puddle);
    float2 wobble = float2(PuddleNoise(p * 2.7 + 3.1), PuddleNoise(p * 2.7 - 11.4)) - .5;
    float2 slope = wobble * lerp(.012, .005, puddle);
    if (HemiSkyRain.w > 0)
    {
        float time = HemiGroundTime.w;
        float2 ripple = RainRipple(p, 2.3, time, 0) + .7 * RainRipple(p, 3.9, time * 1.13, 19);
        slope += ripple * lerp(.004, .018, puddle);
    }
    s.n = normalize(s.n + float3(slope.x, 0, slope.y) * wetness);
    // Reuse the small local perturbation for the prior rigid normal. The
    // moving ripple itself is stochastic shading, not geometric screen motion.
    s.previousNormal = normalize(s.previousNormal + float3(slope.x, 0, slope.y) * wetness);
}

float PanelDots(float2 uv, uint material, Vertex a, Vertex b, Vertex c, SceneInstance instance)
{
    float2 cells = a.v.yz * 1024;
    if (any(cells < 1)) return 1;
    float start = material == 6 ? 320 : 0;
    float span = material == 6 ? 79 : 319;
    float2 coordinates = float2(uv.x, (uv.y * 560 - start - .5) / span) * cells;
    float2 abUv = float2(b.normalU.w - a.normalU.w, (b.v.x - a.v.x) * (560 / span)) * cells;
    float2 acUv = float2(c.normalU.w - a.normalU.w, (c.v.x - a.v.x) * (560 / span)) * cells;
    float3 ab = mul(float4(b.positionMaterial.xyz - a.positionMaterial.xyz, 0), instance.current).xyz;
    float3 ac = mul(float4(c.positionMaterial.xyz - a.positionMaterial.xyz, 0), instance.current).xyz;
    float2 abScreen = mul(float4(ab, 0), ViewProjection).xy * .5 * SizeJitter.xy;
    float2 acScreen = mul(float4(ac, 0), ViewProjection).xy * .5 * SizeJitter.xy;
    float determinant = abScreen.x * acScreen.y - acScreen.x * abScreen.y;
    float2 dX = abUv * acScreen.y - acUv * abScreen.y;
    float2 dY = acUv * abScreen.x - abUv * acScreen.x;
    float footprint = max(1e-3, max(length(dX), length(dY)) / max(abs(determinant), 1e-6));
    float mask = 1 - smoothstep(.30 - .28 * footprint, .30 + .28 * footprint, length(frac(coordinates) - .5));
    return lerp(.73, .45 + .9 * mask, 1 - smoothstep(.22, .9, footprint));
}

Surface Evaluate(Hit hit, float3 rayOrigin)
{
    uint offset = TriangleOffset(hit);
    Vertex a = Vertices[Indices[offset]], b = Vertices[Indices[offset + 1]], c = Vertices[Indices[offset + 2]];
    float3 weights = float3(1 - hit.bary.x - hit.bary.y, hit.bary);
    float3 localPosition = a.positionMaterial.xyz * weights.x + b.positionMaterial.xyz * weights.y + c.positionMaterial.xyz * weights.z;
    SceneInstance instance = Instances[hit.instance];
    Surface s;
    s.p = mul(float4(localPosition, 1), instance.current).xyz;
    float3 direction = s.p - rayOrigin;
    s.previousP = mul(float4(localPosition, 1), instance.previous).xyz;
    float3 crossNormal = cross(b.positionMaterial.xyz - a.positionMaterial.xyz, c.positionMaterial.xyz - a.positionMaterial.xyz);
    s.face = normalize(mul(float4(crossNormal, 0), instance.current).xyz);
    bool frontFace = dot(s.face, direction) < 0;
    if (!frontFace) s.face = -s.face;
    float3 localNormal = a.normalU.xyz * weights.x + b.normalU.xyz * weights.y + c.normalU.xyz * weights.z;
    s.n = normalize(mul(float4(localNormal, 0), instance.current).xyz);
    float orientation = dot(s.n, direction) > 0 ? -1 : 1;
    s.entering = orientation > 0;
    s.n *= orientation;
    s.previousNormal = normalize(mul(float4(localNormal * orientation, 0), instance.previous).xyz);
    s.material = (uint)a.positionMaterial.w;
    float4 color = a.color * weights.x + b.color * weights.y + c.color * weights.z;
    float2 uv = float2(a.normalU.w, a.v.x) * weights.x + float2(b.normalU.w, b.v.x) * weights.y
        + float2(c.normalU.w, c.v.x) * weights.z;
    s.base = saturate(color.rgb);
    s.emission = 0;
    s.waterRoughness = 0;
    s.wetCoverage = 0;
    uint materialParameters = (uint)a.v.w;
    s.roughness = materialParameters == 0 ? .72 : max(.03, (materialParameters >> 4) / 15.0);
    s.metal = (materialParameters & 15) / 15.0;
    if (s.material == 1)
        s.roughness = .035;
    if (s.material == 2 || s.material == 6)
    {
        float3 letters = max(Lyrics.SampleLevel(LinearClamp, uv, 0).rgb, 0) * 1.55;
        float peak = max(letters.r, max(letters.g, letters.b));
        letters *= min(1, 1.25 / max(peak, 1e-6));
        s.emission = s.base * .06 + letters * PanelDots(uv, s.material, a, b, c, instance);
        s.base *= .14;
        s.roughness = .45;
    }
    if (s.material == 3)
        s.emission = s.base * color.w * Lighting.z;
    if (s.material == 11 && frontFace)
        s.emission = float3(a.normalU.w, a.v.xy) * Lighting.z;
    if (s.material == 12)
    {
        float phase = frac((a.normalU.w - 6 * HemiGroundTime.w) / 4);
        if (frontFace) s.emission = s.base * lerp(.18, 1.9, step(.5, phase)) * Lighting.z;
        s.base = 0;
    }
    if (s.material == 8)
    {
        float4 hologram = Lyrics.SampleLevel(LinearClamp, uv, 0);
        s.emission = hologram.rgb * float3(.28, 1.05, 1.27) * 2.8;
        s.base = 0;
    }
    if (GroundMaterial(s.material))
    {
        float speckle = ((Hash(asuint(floor(s.p.x * 195)) ^ Hash(asuint(floor(s.p.z * 195)))) >> 8) * (1.0 / 16777216.0));
        s.base *= .88 + .24 * speckle;
        WetGround(s);
    }
    if (s.material == 5 || s.material == 7 || s.material == 10)
    {
        float2 alternateUv = a.v.yz * weights.x + b.v.yz * weights.y + c.v.yz * weights.z;
        if (s.material == 10 && NoVacancy > .5)
            uv = alternateUv;
        float4 sign = Sign.SampleLevel(LinearClamp, uv, 0);
        sign.rgb *= sign.a;
        if (s.material == 7)
        {
            float2 overlayUv = alternateUv;
            if (all(overlayUv >= 0) && all(overlayUv <= 1))
            {
                float4 overlay = Lyrics.SampleLevel(LinearClamp, overlayUv, 0);
                sign.rgb = sign.rgb * (1 - overlay.a) + overlay.rgb;
                sign.a = sign.a * (1 - overlay.a) + overlay.a;
            }
        }
        s.base = s.base * (1 - sign.a) + sign.rgb;
        s.emission = sign.rgb * color.w * Lighting.z;
    }
    if (s.material == 0 || GroundMaterial(s.material))
    {
        float grain = Hash(asuint(floor(s.p.x * 18)) ^ Hash(asuint(floor(s.p.z * 18)))) / 4294967295.0;
        float snow = saturate((Lighting.y - grain * .33) * 3) * smoothstep(.6, .9, s.n.y);
        s.base = lerp(s.base, float3(.77, .84, .91), snow);
        s.roughness = lerp(s.roughness, .82, snow);
    }
    return s;
}

float3 Offset(Surface s, float3 direction)
{
    return s.p + s.face * (dot(direction, s.face) >= 0 ? Epsilon * 3 : -Epsilon * 3);
}
float3 DryF0(Surface s) { return lerp(.04, s.base, s.metal); }
float3 F0(Surface s) { return lerp(DryF0(s), WaterF0, s.wetCoverage); }
float GuideRoughness(Surface s) { return lerp(s.roughness, s.waterRoughness, s.wetCoverage); }
float3 Fresnel(float3 f0, float cosine) { return f0 + (1 - f0) * pow(1 - saturate(cosine), 5); }
float3 GlassTint(float3 baseColor) { return lerp(.93, 1, sqrt(max(baseColor, 0))); }
float GlassFresnel(float cosine, float eta)
{
    float sin2 = eta * eta * max(0, 1 - cosine * cosine);
    if (sin2 >= 1) return 1;
    float transmittedCosine = sqrt(1 - sin2);
    float rs = (eta * cosine - transmittedCosine) / max(eta * cosine + transmittedCosine, 1e-7);
    float rp = (cosine - eta * transmittedCosine) / max(cosine + eta * transmittedCosine, 1e-7);
    return .5 * (rs * rs + rp * rp);
}

// Keep the virtual surface used by the denoisers on the transmitted side of
// each interface. Sampling Fresnel here makes identical pixels randomly report
// an interior shelf, an exterior reflector, or no surface at all.
void ContinueOpticalPath(Surface s, inout float3 direction, inout float3 throughput)
{
    float eta = s.entering ? 1 / 1.5 : 1.5;
    float fresnel = GlassFresnel(saturate(dot(s.n, -direction)), eta);
    if (fresnel >= 1)
        direction = reflect(direction, s.n);
    else
    {
        direction = refract(direction, s.n, eta);
        throughput *= (1 - fresnel) * GlassTint(s.base);
    }
}
float GgxAlpha(Surface s) { return max(.0025, s.roughness * s.roughness); }
float StableGgxNdf(float alpha, float nh)
{
    precise float nh2 = nh * nh;
    precise float d = (1 - nh2) + alpha * alpha * nh2;
    float ratio = alpha / max(d, 1e-8);
    return ratio * ratio / Pi;
}
float PowerHeuristic(float a, float b)
{
    float scale = max(a, b);
    if (scale <= 0) return 0;
    a /= scale;
    b /= scale;
    return a * a / (a * a + b * b);
}
float WaterAlpha(Surface s) { return max(.0025, s.waterRoughness * s.waterRoughness); }

float SpecularProbability(Surface s, float3 view)
{
    float nv = saturate(dot(s.n, view));
    const float3 luminance = float3(.2126, .7152, .0722);
    float dry = saturate(dot(approxSpecularIntegralGGX(DryF0(s), GgxAlpha(s), nv), luminance));
    float reflectance = s.wetCoverage > 0 ? lerp(dry, GlassFresnel(nv, WaterEta), s.wetCoverage) : dry;
    float diffuse = dot(s.base, luminance) * (1 - s.metal) * (1 - reflectance);
    return clamp(reflectance / max(reflectance + diffuse, 1e-6), .06, .94);
}

// Conditional probability of choosing the water or dry microfacet distribution.
// The BRDF and MIS PDF still evaluate both distributions at the resulting ray.
float WaterSpecularProbability(Surface s, float nv)
{
    if (s.wetCoverage <= 0) return 0;
    if (s.wetCoverage >= 1) return 1;
    const float3 luminance = float3(.2126, .7152, .0722);
    float wet = s.wetCoverage * GlassFresnel(nv, WaterEta);
    float dry = (1 - s.wetCoverage) * dot(approxSpecularIntegralGGX(DryF0(s), GgxAlpha(s), nv), luminance);
    return wet / max(wet + dry, 1e-6);
}

float2 EvaluateGgx(float alpha, float nh, float nv, float nl)
{
    float ndf = StableGgxNdf(alpha, nh);
    return float2(ndf * evalMaskingSmithGGXCorrelated(alpha, nv, nl) / max(4 * nv * nl, 1e-6),
        ndf * evalG1GGX(alpha * alpha, nv) / max(4 * nv, 1e-6));
}

float2 EvaluateBsdf(Surface s, float3 view, float3 l, out float3 diffuse, out float3 specular)
{
    diffuse = specular = 0;
    float nv = dot(s.n, view);
    float nl = dot(s.n, l);
    if (nv <= 0 || nl <= 0 || dot(s.face, l) <= 0) return 0;
    float3 h = normalize(view + l);
    float nh = saturate(dot(s.n, h));
    float vh = saturate(dot(view, h));
    float3 substrate = s.base * (1 - s.metal);
    float dryPdf = 0;
    if (s.wetCoverage < 1)
    {
        float2 dry = EvaluateGgx(GgxAlpha(s), nh, nv, nl);
        float3 fresnel = Fresnel(DryF0(s), vh);
        specular = dry.x * fresnel * (1 - s.wetCoverage);
        diffuse = substrate * (1 - fresnel) * ((1 - s.wetCoverage) / Pi);
        dryPdf = dry.y;
    }
    float specularPdf = dryPdf;
    if (s.wetCoverage > 0)
    {
        float2 wet = EvaluateGgx(WaterAlpha(s), nh, nv, nl);
        specular += wet.x * GlassFresnel(vh, WaterEta) * s.wetCoverage;
        // Two transmissions through the air/water boundary. The squared index
        // ratio converts the substrate's Lambertian radiance back to air.
        float transmission = (1 - GlassFresnel(nv, WaterEta)) * (1 - GlassFresnel(nl, WaterEta));
        diffuse += substrate * (WaterEta * WaterEta * transmission * s.wetCoverage / Pi);
        specularPdf = lerp(dryPdf, wet.y, WaterSpecularProbability(s, nv));
    }
    return float2(nl / Pi, specularPdf);
}

float3 SampleSpecular(Surface s, float3 view, float2 sample, float layerSample)
{
    float alpha = layerSample < WaterSpecularProbability(s, saturate(dot(s.n, view))) ? WaterAlpha(s) : GgxAlpha(s);
    float3 micro = FromLocal(sampleGGX_VNDF(alpha, ToLocal(view, s.n), sample), s.n);
    return reflect(-view, micro);
}

float3 AnalyticVisibility(Surface s, float3 direction, float limit)
{
    float3 origin = Offset(s, direction);
    float3 transmission = 1;
    [loop]
    for (uint i = 0; i < 5 && limit > Epsilon; i++)
    {
        // A glass hit must be the nearest intersection. Accepting arbitrary
        // candidates can jump across an opaque occluder in front of a pane.
        Hit h = Trace(origin, direction, limit);
        if (h.primitive == 0xffffffff) return transmission;
        Vertex blocker = Vertices[Indices[TriangleOffset(h)]];
        if ((uint)blocker.positionMaterial.w != 1) return 0;
        limit -= h.t + Epsilon * 3;
        origin += direction * (h.t + Epsilon * 3);
        transmission *= .96 * GlassTint(saturate(blocker.color.rgb));
    }
    return limit <= Epsilon ? transmission : 0;
}

SceneEmitter SelectEmitter(float sample)
{
    float column = sample * Lighting.x;
    SceneEmitter candidate = Emitters[min((uint)column, (uint)Lighting.x - 1)];
    if (frac(column) >= candidate.threshold) return Emitters[candidate.alias];
    return candidate;
}

AnalyticEmitter SelectAnalytic(float sample)
{
    float column = sample * AnalyticLightControl.x;
    AnalyticEmitter candidate = AnalyticLights[min((uint)column, (uint)AnalyticLightControl.x - 1)];
    if (frac(column) >= candidate.threshold) return AnalyticLights[candidate.alias];
    return candidate;
}

void AnalyticLighting(Surface s, float3 view, AnalyticEmitter light, out float3 diffuse, out float3 specular)
{
    diffuse = specular = 0;
    float3 delta = light.positionRange.xyz - s.p;
    float distanceSquared = dot(delta, delta);
    float distance = sqrt(distanceSquared);
    if (distance < Epsilon * 8 || (light.positionRange.w > 0 && distance >= light.positionRange.w)) return;
    float3 direction = delta / distance;
    float cosine = dot(s.n, direction);
    if (cosine <= 0) return;

    float attenuation = rcp(max(distanceSquared, .015));
    if (light.positionRange.w > 0)
    {
        float ratio = distance / light.positionRange.w;
        float fade = saturate(1 - ratio * ratio * ratio * ratio);
        attenuation *= fade * fade;
    }
    if (light.directionOuter.w >= 0)
    {
        float cone = dot(-direction, light.directionOuter.xyz);
        attenuation *= smoothstep(light.directionOuter.w, max(light.directionOuter.w + .0001, light.radianceInner.w), cone);
    }
    if (attenuation <= 0) return;
    EvaluateBsdf(s, view, direction, diffuse, specular);
    if (!any(diffuse + specular > 0)) return;
    float3 energy = light.radianceInner.rgb * Lighting.z * attenuation * cosine * AnalyticVisibility(s, direction, distance - Epsilon * 7);
    diffuse *= energy;
    specular *= energy;
}

float3 TotalAnalyticLighting(Surface surface, float3 view)
{
    float3 total = 0;
    [loop]
    for (uint i = 0; i < (uint)AnalyticLightControl.x; i++)
    {
        float3 d, r;
        AnalyticLighting(surface, view, AnalyticLights[i], d, r);
        total += d + r;
    }
    return total;
}

void DirectSun(Surface s, float3 view, out float3 d, out float3 r)
{
    d = r = 0;
    float3 l = SunDirectionFrame.xyz;
    float cosine = dot(s.n, l);
    if (cosine > 0 && any(SunRadianceFar.rgb > 0))
    {
        float3 ds, rs;
        EvaluateBsdf(s, view, l, ds, rs);
        if (any(ds + rs > 0))
        {
            float3 energy = SunRadianceFar.rgb * cosine * AnalyticVisibility(s, l, SunRadianceFar.w);
            d += ds * energy;
            r += rs * energy;
        }
    }
}

void Direct(Surface s, float3 view, float3 lightSample, bool separated, float primarySpecularRate,
    bool allAnalytic, out float3 d, out float3 r, uint emitterSamples = 1, bool includeSun = true)
{
    d = r = 0;
    if (includeSun) DirectSun(s, view, d, r);
    float3 l;
    float cosine;
    float analyticChance = allAnalytic ? 0 : AnalyticLightControl.y;
    if (lightSample.x < analyticChance)
    {
        AnalyticEmitter light = SelectAnalytic(lightSample.x / analyticChance);
        float3 da, ra;
        AnalyticLighting(s, view, light, da, ra);
        float sampling = rcp(max(light.probability * analyticChance, 1e-6));
        d += da * sampling;
        r += ra * sampling;
        return;
    }
    if (Lighting.x == 0) return;

    SceneEmitter emitter = SelectEmitter((lightSample.x - analyticChance) / max(1 - analyticChance, 1e-6));
    float radial = sqrt(lightSample.y);
    Hit lightHit = {0, emitter.primitive, radial * float2(1 - lightSample.z, lightSample.z), emitter.instance};
    Surface light = Evaluate(lightHit, s.p);
    float3 delta = light.p - s.p;
    float distance = length(delta);
    l = delta / max(distance, 1e-6);
    cosine = dot(s.n, l);
    float lightCosine = abs(dot(light.face, -l));
    if (cosine <= 0 || lightCosine <= 0 || distance < Epsilon * 8 || !any(light.emission > 0)) return;

    float3 ds, rs;
    float2 conditionalPdf = EvaluateBsdf(s, view, l, ds, rs);
    if (!any(ds + rs > 0)) return;
    // A direct connection to an area light has no intervening dielectric.
    // The BSDF path samples refraction and receives full weight after a delta event.
    if (Trace(Offset(s, l), l, distance - Epsilon * 7).primitive != 0xffffffff) return;
    float lightPdf = (1 - analyticChance) * emitter.areaPdf * distance * distance / lightCosine;
    float3 contribution = light.emission * (cosine / max(lightPdf, 1e-6));
    if (separated)
    {
        float diffuseRate = primarySpecularRate < 0 ? 1 : 1 - primarySpecularRate;
        float specularRate = primarySpecularRate < 0 ? 1 : primarySpecularRate;
        d += ds * contribution * PowerHeuristic(lightPdf * emitterSamples, conditionalPdf.x * diffuseRate);
        r += rs * contribution * PowerHeuristic(lightPdf * emitterSamples, conditionalPdf.y * specularRate);
    }
    else
    {
        float combined = lerp(conditionalPdf.x, conditionalPdf.y, SpecularProbability(s, view));
        float weight = PowerHeuristic(lightPdf * emitterSamples, combined);
        d += ds * contribution * weight;
        r += rs * contribution * weight;
    }
}

bool Scatter(Surface s, float3 samples, inout float3 direction, inout float3 throughput, out float bsdfPdf)
{
    float3 view = -direction;
    bsdfPdf = 0;
    if (s.material == 1)
    {
        float eta = s.entering ? 1 / 1.5 : 1.5;
        float fresnel = GlassFresnel(saturate(dot(s.n, view)), eta);
        if (samples.z < fresnel)
            direction = reflect(direction, s.n);
        else
        {
            direction = refract(direction, s.n, eta);
            throughput *= GlassTint(s.base);
        }
        return true;
    }

    float probability = SpecularProbability(s, view);
    bool specular = samples.z < probability;
    float layerSample = (samples.z / probability);
    direction = specular ? SampleSpecular(s, view, samples.xy, layerSample) : CosineSample(s.n, samples.xy);
    float3 d, r;
    float2 pdf = EvaluateBsdf(s, view, direction, d, r);
    bsdfPdf = lerp(pdf.x, pdf.y, probability);
    if (bsdfPdf <= 0) return false;
    throughput *= (d + r) * (saturate(dot(s.n, direction)) / bsdfPdf);
    return true;
}

Plane Follow(float3 origin, float3 direction, float3 throughput, float bsdfPdf,
    uint2 pixel, uint stream, bool optical)
{
    Plane result = (Plane)0;
    result.roughness = 1;
    result.normal = -direction;
    result.hitDistance = SunRadianceFar.w;
    uint bounceLimit = optical ? 2 : 3;
    const uint interfaceLimit = 7;
    uint opaqueBounces = 0, interfaces = 0;
    bool priorAnalytic = true;
    [loop]
    for (uint step = 0; step < bounceLimit + interfaceLimit; step++)
    {
        Hit hit = Trace(origin, direction, SunRadianceFar.w);
        if (hit.primitive == 0xffffffff)
        {
            result.color += throughput * EnvironmentRadiance(direction);
            break;
        }
        Surface s = Evaluate(hit, origin);
        bool firstSurface = result.hitDistance == SunRadianceFar.w;
        uint vertex = step < bounceLimit ? stream * (optical ? 8 : 4) + step : 512 + stream * 16 + step;
        if (firstSurface)
        {
            result.hitDistance = hit.t;
            result.position = s.p;
            result.previousPosition = s.previousP;
            result.normal = s.n;
            result.roughness = GuideRoughness(s);
        }
        if (any(s.emission > 0))
        {
            float analyticChance = priorAnalytic ? 0 : AnalyticLightControl.y;
            float emitterPdf = (1 - analyticChance) * EmitterPdf[TriangleOffset(hit) / 3]
                * hit.t * hit.t / max(abs(dot(s.face, direction)), 1e-6);
            uint lightSamples = optical && step == 0 ? OpticalLightSamples : 1;
            float weight = bsdfPdf > 0 ? PowerHeuristic(bsdfPdf, emitterPdf * lightSamples) : 1;
            result.color += throughput * s.emission * weight;
        }
        if (s.material != 1)
        {
            float3 d, r;
            Direct(s, -direction, PathSample(pixel, vertex, 3), false, 0, false, d, r);
            result.color += throughput * (d + r);
            if (++opaqueBounces == bounceLimit) break;
        }
        // A finite glass slab needs two delta events before seeing its far side.
        // Cap chains of interfaces separately from diffuse and GGX scattering.
        else if (++interfaces > interfaceLimit) break;
        priorAnalytic = false;
        if (!Scatter(s, PathSample(pixel, vertex, 1), direction, throughput, bsdfPdf)) break;
        // Apply roulette after non-delta scattering. Applying it at each layer
        // of a pane increases variance without sampling any new optical lobe.
        if (!optical && s.material != 1)
        {
            float survive = clamp(max(throughput.r, max(throughput.g, throughput.b)), .18, .92);
            if (PathSample(pixel, vertex, 6).x >= survive) break;
            throughput /= survive;
        }
        origin = Offset(s, direction);
    }
    result.color = min(max(result.color, 0), 320);
    return result;
}
Plane FollowOptical(float3 origin, float3 direction, float3 throughput, uint2 pixel, uint stream)
{
    Plane result = (Plane)0;
    result.hitDistance = SunRadianceFar.w;
    result.roughness = 1;
    result.normal = -direction;
    [loop]
    for (uint interfaceIndex = 0; interfaceIndex < 7; interfaceIndex++)
    {
        Hit hit = Trace(origin, direction, SunRadianceFar.w);
        if (hit.primitive == 0xffffffff)
        {
            result.deterministic = throughput * EnvironmentRadiance(direction);
            return result;
        }
        Surface s = Evaluate(hit, origin);
        if (s.material == 1)
        {
            ContinueOpticalPath(s, direction, throughput);
            origin = Offset(s, direction);
            continue;
        }

        result.hitDistance = hit.t;
        result.position = s.p;
        result.previousPosition = s.previousP;
        result.normal = s.n;
        result.roughness = GuideRoughness(s);
        if (Lighting.w > 0)
        {
            result.albedo = s.base * (1 - s.metal);
            result.specularAlbedo = F0(s);
        }
        // Preserve camera-visible emission along the ideal glass path.
        float3 sunDiffuse, sunSpecular;
        DirectSun(s, -direction, sunDiffuse, sunSpecular);
        result.deterministic = throughput * (s.emission + sunDiffuse + sunSpecular + TotalAnalyticLighting(s, -direction));
        uint vertex = stream * 32 + interfaceIndex;
        // Stratify the triangle alias-table sample. Use the same number of
        // light samples in the matching BSDF-path MIS weight.
        [loop]
        for (uint stratum = 0; stratum < OpticalLightSamples; stratum++)
        {
            float3 sample = PathSample(pixel, vertex, 3 + 32 * stratum);
            sample.x = (sample.x + stratum) / OpticalLightSamples;
            float3 d, r;
            Direct(s, -direction, sample, true, -1, true, d, r, OpticalLightSamples, false);
            result.color += throughput * (d + r) / OpticalLightSamples;
        }
        // Sample each lobe once and use its conditional PDF for MIS.
        [loop]
        for (uint lobe = 0; lobe < 2; lobe++)
        {
            float3 sample = PathSample(pixel, vertex, 1 + lobe);
            float3 outgoing = lobe == 0 ? CosineSample(s.n, sample.xy) : SampleSpecular(s, -direction, sample.xy, sample.z);
            float3 diffuse, specular;
            float2 pdf = EvaluateBsdf(s, -direction, outgoing, diffuse, specular);
            float probability = lobe == 0 ? pdf.x : pdf.y;
            if (probability <= 0) continue;
            float3 bsdf = lobe == 0 ? diffuse : specular;
            float3 weight = throughput * bsdf * (saturate(dot(s.n, outgoing)) / probability);
            Plane samplePath = Follow(Offset(s, outgoing), outgoing, weight, probability, pixel,
                stream * 2 + lobe + 9, true);
            result.color += samplePath.color;
        }
        result.color = min(max(result.color, 0), 320);
        return result;
    }
    return result;
}

float2 Project(float3 position, row_major float4x4 matrix)
{
    float4 clip = mul(float4(position, 1), matrix);
    return clip.xy / clip.w * float2(.5, -.5) * SizeJitter.xy;
}
float2 Reproject(float3 p, float3 previous) { return Project(previous, PreviousViewProjection) - Project(p, ViewProjection); }
float SignedViewZ(float3 p) { return mul(float4(p, 1), WorldToView).z; }
void ReflectPlane(Surface s, inout Plane plane)
{
    plane.position -= 2 * dot(plane.position - s.p, s.n) * s.n;
    plane.previousPosition -= 2 * dot(plane.previousPosition - s.previousP, s.previousNormal) * s.previousNormal;
    plane.normal = reflect(plane.normal, s.n);
}

float DeviceDepth(float3 position)
{
    float4 clip = mul(float4(position, 1), ViewProjection);
    return saturate(clip.z / clip.w);
}

[shader("raygeneration")]
void RayGen()
{
    uint2 pixel = DispatchRaysIndex().xy;
    float2 ndc = (float2(pixel) + .5 + SizeJitter.zw) / SizeJitter.xy * float2(2, -2) + float2(-1, 1);
    float4 nearPlane = mul(float4(ndc, 0, 1), InverseViewProjection);
    float4 farPlane = mul(float4(ndc, 1, 1), InverseViewProjection);
    float3 origin = nearPlane.xyz / nearPlane.w;
    float3 primary = normalize(farPlane.xyz / farPlane.w - origin);

    float3 d = 0, r = 0, dFactor = 1, sFactor = 1, deterministic = BackgroundSky(primary);
    float3 dDirection = -primary, sDirection = -primary;
    float dDistance = 0, sDistance = 0;
    float3 worldNormal = -primary;
    float roughness = 1, z = -1e6, depth = 1;
    float2 motion = 0;
    float3 rawAlbedo = 0, rawSpecular = 0;
    float reflectionDominance = 0;
    Plane reflection = (Plane)0, transmission = (Plane)0;
    reflection.hitDistance = transmission.hitDistance = SunRadianceFar.w;
    reflection.normal = transmission.normal = -primary;
    reflection.roughness = transmission.roughness = 1;
    Hit hit = Trace(origin, primary, SunRadianceFar.w);
    if (hit.primitive != 0xffffffff)
    {
        Surface s = Evaluate(hit, origin);
        deterministic = s.emission;
        worldNormal = s.n;
        roughness = GuideRoughness(s);
        rawAlbedo = s.material == 1 ? 0 : s.base * (1 - s.metal);
        rawSpecular = F0(s);
        z = SignedViewZ(s.p);
        depth = DeviceDepth(s.p);
        motion = Reproject(s.p, s.previousP);
        if (s.material == 1)
        {
            float3 reflected = reflect(primary, s.n);
            float eta = s.entering ? 1 / 1.5 : 1.5;
            float fresnel = GlassFresnel(saturate(dot(s.n, -primary)), eta);
            float3 transmissionDirection = fresnel < 1 ? refract(primary, s.n, eta) : primary;
            float3 transmissionOrigin = Offset(s, transmissionDirection);
            float3 transmissionWeight = (1 - fresnel) * GlassTint(s.base);
            float3 reflectionWeight = fresnel;
            if (s.entering && fresnel < 1)
            {
                // The exported panes are thin closed slabs. Sum internal
                // reflections for a parallel rear interface and preserve the
                // actual exit position for refraction and reprojection.
                Hit rearHit = Trace(transmissionOrigin, transmissionDirection, .08);
                if (rearHit.primitive != 0xffffffff && rearHit.instance == hit.instance &&
                    (uint)Vertices[Indices[TriangleOffset(rearHit)]].positionMaterial.w == 1)
                {
                    Surface rear = Evaluate(rearHit, transmissionOrigin);
                    if (!rear.entering && dot(rear.n, s.n) > .999)
                    {
                        float3 tint = GlassTint(s.base) * GlassTint(rear.base);
                        transmissionWeight = (1 - fresnel) * (1 - fresnel) * tint /
                            max(1 - fresnel * fresnel * tint, 1e-5);
                        reflectionWeight += fresnel * transmissionWeight;
                        transmissionDirection = primary;
                        transmissionOrigin = Offset(rear, primary);
                    }
                }
            }
            const float3 luma = float3(.2126, .7152, .0722);
            reflectionDominance = dot(reflectionWeight - transmissionWeight, luma);
            [loop]
            for (uint optical = 0; optical < (fresnel < 1 ? 2 : 1); optical++)
            {
                float3 direction = optical == 0 ? reflected : transmissionDirection;
                float3 originOnPane = optical == 0 ? Offset(s, direction) : transmissionOrigin;
                float3 throughput = optical == 0 ? reflectionWeight : transmissionWeight;
                Plane branch = FollowOptical(originOnPane, direction, throughput, pixel, optical + 1);
                if (optical == 0) reflection = branch;
                else transmission = branch;
            }
            // Reflect the first non-glass world point and normal into the incident camera space.
            ReflectPlane(s, reflection);
            sDistance = reflection.hitDistance;
        }
        else
        {
            float3 view = -primary;
            bool wetRoad = s.wetCoverage > .02;
            float specularRate = clamp(SpecularProbability(s, view), .25, .75);
            Direct(s, view, PathSample(pixel, 0, 3), true, wetRoad ? -1 : specularRate, true, d, r);
            deterministic += TotalAnalyticLighting(s, view);
            NRD_MaterialFactors(s.n, view, rawAlbedo, rawSpecular, roughness, dFactor, sFactor);
            // The wet road always traces the diffuse and specular paths.
            // Use the ground's normal, depth and motion for both sides of the
            // puddle transition. Its rough reflection has no single virtual surface.
            bool selectSpecular = PathSample(pixel, 0, 5).x < specularRate;
            [loop]
            for (uint lobe = 0; lobe < (wetRoad ? 2 : 1); lobe++)
            {
                bool specular = wetRoad ? lobe == 1 : selectSpecular;
                float3 samples = PathSample(pixel, 0, specular ? 2 : 1);
                float3 direction = specular ? SampleSpecular(s, view, samples.xy, samples.z) : CosineSample(s.n, samples.xy);
                float probability = wetRoad ? 1 : specular ? specularRate : 1 - specularRate;
                float3 sampledDiffuse, sampledSpecular;
                float2 conditionalPdf = EvaluateBsdf(s, view, direction, sampledDiffuse, sampledSpecular);
                float pdf = (specular ? conditionalPdf.y : conditionalPdf.x) * probability;
                if (pdf <= 0) continue;
                float3 value = specular ? sampledSpecular : sampledDiffuse;
                float3 weight = value * saturate(dot(s.n, direction)) / pdf;
                Plane sample = Follow(Offset(s, direction), direction, weight, pdf, pixel, specular ? 4 : 3,
                    false);
                if (!specular)
                {
                    d += sample.color;
                    dDirection = direction;
                    dDistance = sample.hitDistance;
                }
                else
                {
                    r += sample.color;
                    sDirection = direction;
                    sDistance = sample.hitDistance;
                }
            }
            // Keep the NRD hit distance stable when wet microfacets sample
            // different geometry. This ray supplies a guide, never radiance.
            if (Lighting.w == 0 && s.wetCoverage > .45)
            {
                float3 direction = reflect(primary, s.n);
                if (dot(direction, s.face) > 0)
                {
                    Hit guideHit = Trace(Offset(s, direction), direction, SunRadianceFar.w);
                    if (guideHit.primitive != 0xffffffff) sDistance = guideHit.t;
                }
            }
        }
    }

    float4 dSh, sSh;
    Diffuse[pixel] = RELAX_FrontEnd_PackSh(d / dFactor, dDistance, dDirection, dSh, true);
    Specular[pixel] = RELAX_FrontEnd_PackSh(r / sFactor, sDistance, sDirection, sSh, true);
    DiffuseSh[pixel] = dSh;
    SpecularSh[pixel] = sSh;
    Normal[pixel] = NRD_FrontEnd_PackNormalAndRoughness(worldNormal, roughness, 0);
    ViewZ[pixel] = z;
    Motion[pixel] = motion;
    DiffuseFactor[pixel] = float4(dFactor, 1);
    SpecularFactor[pixel] = float4(sFactor, 1);

    float reflectedZ = SignedViewZ(reflection.position), transmittedZ = SignedViewZ(transmission.position);
    bool validR = reflection.hitDistance < SunRadianceFar.w && abs(reflectedZ) < SunRadianceFar.w;
    bool validT = transmission.hitDistance < SunRadianceFar.w && abs(transmittedZ) < SunRadianceFar.w;
    Reflection[pixel] = RELAX_FrontEnd_PackRadianceAndHitDist(validR ? reflection.color : 0, reflection.hitDistance, true);
    ReflectionNormal[pixel] = NRD_FrontEnd_PackNormalAndRoughness(reflection.normal, reflection.roughness, 0);
    ReflectionZ[pixel] = validR ? reflectedZ : -1e6;
    float2 reflectionMotion = validR ? Reproject(reflection.position, reflection.previousPosition) : motion;
    ReflectionMotion[pixel] = reflectionMotion;
    Transmission[pixel] = RELAX_FrontEnd_PackRadianceAndHitDist(validT ? transmission.color : 0, transmission.hitDistance, true);
    TransmissionNormal[pixel] = NRD_FrontEnd_PackNormalAndRoughness(transmission.normal, transmission.roughness, 0);
    TransmissionZ[pixel] = validT ? transmittedZ : -1e6;
    float2 transmissionMotion = validT ? Reproject(transmission.position, transmission.previousPosition) : 0;
    TransmissionMotion[pixel] = transmissionMotion;
    Deterministic[pixel] = float4(deterministic + reflection.deterministic + transmission.deterministic
        + (validR ? 0 : reflection.color) + (validT ? 0 : transmission.color), 1);
    Noisy[pixel] = float4(deterministic + reflection.deterministic + transmission.deterministic
        + d + r + reflection.color + transmission.color, 1);
    Depth[pixel] = depth;
    if (Lighting.w < 0)
    {
        // FSR's near/far linearization expects perspective device depth.
        const float nearPlane = .1;
        float farPlane = SunRadianceFar.w;
        float viewDistance = lerp(nearPlane, farPlane, depth);
        ReconstructionDepth[pixel] = farPlane * depth / viewDistance;
    }
    if (Lighting.w > 0)
    {
        // RR has one G-buffer. Choose its optical lobe from the Fresnel
        // weights, independently of the stochastic sample's brightness.
        // The primary device depth above still supplies weather and DOF.
        bool useReflection = validR && (!validT || reflectionDominance > 0);
        if (useReflection || validT)
        {
            Plane guide = transmission;
            if (useReflection) guide = reflection;
            depth = DeviceDepth(guide.position);
            motion = useReflection ? reflectionMotion : transmissionMotion;
            worldNormal = guide.normal;
            roughness = guide.roughness;
            rawAlbedo = guide.albedo;
            rawSpecular = guide.specularAlbedo;
        }
        ReconstructionDepth[pixel] = depth;
        ReconstructionMotion[pixel] = motion;
        Albedo[pixel] = float4(rawAlbedo, 1);
        SpecularAlbedo[pixel] = float4(rawSpecular, 1);
        LinearNormal[pixel] = float4(worldNormal, roughness);
        SpecularDistance[pixel] = sDistance;
    }
}
