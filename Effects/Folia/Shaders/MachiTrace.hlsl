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
    row_major float4x4 RayBasis;
    row_major float4x4 ViewProjection;
    row_major float4x4 PreviousViewProjection;
    row_major float4x4 WorldToView;
    float4 SkyWetness, SunDirectionFrame, SunRadianceFar, SizeJitter, Lighting, SkyBottom;
    float2 AnalyticLightControl;
    float NoVacancy, TraceGuideMode;
    float4 HemiSkyRain, HemiGroundTime;
    float3 CityGlow;
    float BoardWhiteRadiance;
    float4 Puddles;
    float4 RainShelters[4];
    uint SamplesPerPixel, MaxBounces;
    float PixelWidthAtUnitDepth;
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
    uint visible;
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
// The packed source RGB is normal-incidence irradiance integrated over the disk.
static const float CelestialRadius = .526 * Pi / 360;
static const float DiskRadianceScale = 1 / (Pi * sin(CelestialRadius) * sin(CelestialRadius));
static const float Epsilon = .001;
static const uint OpticalLightSamples = 4;

uint PathSeed(uint2 pixel, uint vertex, uint effect)
{
    return Hash32Combine(Hash32Combine(Hash32(pixel.x + 0x035f9f29u), pixel.y), Hash32Combine(vertex, effect));
}
float3 PathSample(uint3 path, uint vertex, uint effect)
{
    return burley_shuffled_scrambled_sobol_3D(path.z, PathSeed(path.xy, vertex, effect));
}
float3 CelestialDisk(float3 direction)
{
    return dot(direction, SunDirectionFrame.xyz) >= cos(CelestialRadius) ? SunRadianceFar.rgb * DiskRadianceScale : 0;
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

bool IsTransparentLyric(uint instanceId, uint primitive, float2 bary)
{
    uint offset = Instances[instanceId].firstIndex + primitive * 3;
    Vertex a = Vertices[Indices[offset]];
    if ((uint)a.positionMaterial.w != 8) return false;
    Vertex b = Vertices[Indices[offset + 1]], c = Vertices[Indices[offset + 2]];
    float2 uv = lerp(float2(a.normalU.w, a.v.x), float2(b.normalU.w, b.v.x), bary.x)
        + (float2(c.normalU.w, c.v.x) - float2(a.normalU.w, a.v.x)) * bary.y;
    return Lyrics.SampleLevel(LinearClamp, uv, 0).a < .04;
}

Hit Trace(float3 origin, float3 direction, float limit)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = direction;
    ray.TMin = Epsilon;
    ray.TMax = limit;
    Hit hit = {0, 0xffffffff, float2(0, 0), 0};
#if MACHI_INLINE_TRACE
    // Keep the path state in ray generation while the hardware traverses triangles.
    RayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES> query;
    query.TraceRayInline(Scene, RAY_FLAG_NONE, 0xff, ray);
    while (query.Proceed())
    {
        if (!IsTransparentLyric(query.CandidateInstanceID(), query.CandidatePrimitiveIndex(), query.CandidateTriangleBarycentrics()))
            query.CommitNonOpaqueTriangleHit();
    }
    if (query.CommittedStatus() == COMMITTED_TRIANGLE_HIT)
    {
        hit.t = query.CommittedRayT();
        hit.primitive = query.CommittedPrimitiveIndex();
        hit.bary = query.CommittedTriangleBarycentrics();
        hit.instance = query.CommittedInstanceID();
    }
#else
    TraceRay(Scene, RAY_FLAG_NONE, 0xff, 0, 0, 0, ray, hit);
#endif
    return hit;
}
bool Occluded(float3 origin, float3 direction, float limit)
{
    RayDesc ray;
    ray.Origin = origin;
    ray.Direction = direction;
    ray.TMin = Epsilon;
    ray.TMax = limit;
#if MACHI_INLINE_TRACE
    // The first accepted blocker is sufficient. Reject transparent lyrics before committing it.
    RayQuery<RAY_FLAG_SKIP_PROCEDURAL_PRIMITIVES | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH> query;
    query.TraceRayInline(Scene, RAY_FLAG_NONE, 0xff, ray);
    while (query.Proceed())
    {
        if (!IsTransparentLyric(query.CandidateInstanceID(), query.CandidatePrimitiveIndex(), query.CandidateTriangleBarycentrics()))
            query.CommitNonOpaqueTriangleHit();
    }
    return query.CommittedStatus() == COMMITTED_TRIANGLE_HIT;
#else
    Hit hit = {0, 0, float2(0, 0), 0};
    // The first accepted blocker is sufficient. Any-hit still rejects transparent hologram texels.
    TraceRay(Scene, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER,
        0xff, 0, 0, 0, ray, hit);
    return hit.primitive != 0xffffffff;
#endif
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
void Miss(inout Hit hit) { hit.primitive = 0xffffffff; }

[shader("anyhit")]
void AnyHit(inout Hit hit, BuiltInTriangleIntersectionAttributes attrs)
{
    if (IsTransparentLyric(InstanceID(), PrimitiveIndex(), attrs.barycentrics)) IgnoreHit();
}

uint TriangleOffset(Hit hit) { return Instances[hit.instance].firstIndex + hit.primitive * 3; }

bool GroundMaterial(uint material) { return material == 4 || material == 9; }

float4 TileMip(float2 uv, uint tile, uint level, float2 dimensions)
{
    uint n = 256u >> level;
    // Layout shared with Build/FoliaMachi/surface-tiles.mjs.
    float2 origin = float2(tile * 390 + (level == 0 ? 0 : 258), dimensions.y - 258);
    if (level > 0) origin.y += 256 - 2 * n + (level - 1) * 2;
    return Sign.SampleLevel(LinearClamp, (origin + 1 + frac(uv) * n) / dimensions, 0);
}

float WorldPixelWidth(float3 position)
{
    return abs(mul(float4(position, 1), WorldToView).z) * PixelWidthAtUnitDepth;
}

void SurfaceTexture(inout Surface s, float3 incident)
{
    uint width, height;
    Sign.GetDimensions(width, height);
    if (width != 2048 || height < 258) return;
    uint tile = s.material == 4 ? 0 : s.material == 9 ? 1 : s.material - 12;
    float3 a = abs(s.face);
    float3 u = a.x > max(a.y, a.z) ? float3(0,0,1) : float3(1,0,0);
    float3 v = a.y >= max(a.x, a.z) ? float3(0,0,1) : float3(0,1,0);
    float period = tile == 0 ? .92 : tile == 1 ? 2 : tile == 2 ? 1.2 : 2.6;
    float2 uv = float2(dot(s.p, u), dot(s.p, v)) / period;
    float pixelWidth = WorldPixelWidth(s.p);
    float footprint = pixelWidth * 256 / (period * max(.18, abs(dot(normalize(incident), s.face))));
    float level = clamp(log2(max(footprint, 1)), 0, 4);
    uint mip = (uint)level;
    float4 texel = TileMip(uv, tile, mip, float2(width,height));
    if (mip < 4)
        texel = lerp(texel, TileMip(uv, tile, mip + 1, float2(width,height)), frac(level));
    s.base *= .38 + texel.r;
    s.roughness = clamp(s.roughness + .36*(texel.a-.5), .04, .98);
    float3 slope = u*(texel.g-.5) + v*(texel.b-.5);
    s.n = normalize(s.n - slope);
    s.previousNormal = normalize(s.previousNormal - slope);
}

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

static const float SnowThresholdRange = .33;
static const float SnowGrainRamp = 1.0 / 3.0;
static const float SnowGrainsPerMeter = 17;

float2 SnowGrain(int2 cell, float2 position)
{
    uint hash = Hash32(asuint(cell.x) * 0x9e3779b9u + asuint(cell.y) * 0x85ebca6bu + 0x035f9f29u);
    float2 center = .25 + (.50 / 255) * float2(hash & 255u, (hash >> 8) & 255u);
    float shape = .96 + (.08 / 63) * ((hash >> 16) & 63u);
    float threshold = ((hash >> 22) + .5) * (SnowThresholdRange / 1024);
    float2 offset = position - center;
    return float2(dot(offset, offset * float2(shape, 2 - shape)), threshold);
}

float SnowCoverage(Surface s, float3 incident)
{
    float accumulation = saturate(Lighting.y);
    float upward = smoothstep(.6, .9, s.n.y);
    [branch] if (accumulation <= 0 || accumulation >= 1 || upward <= 0)
        return accumulation * upward;

    // Keep the thin layer's original threshold distribution and let the thick layer close gradually.
    float depth = accumulation * (1 - (1 - SnowThresholdRange - SnowGrainRamp)
        * smoothstep(.11, 1, accumulation));
    // Integrate the clamped opacity ramp over the uniform threshold distribution.
    float2 ramp = max(depth - float2(0, SnowThresholdRange), 0);
    float2 clipped = max(ramp - SnowGrainRamp, 0);
    float2 area = ramp * ramp - clipped * clipped;
    float mean = saturate((area.x - area.y) / (2 * SnowThresholdRange * SnowGrainRamp));

    float facing = max(.18, abs(dot(normalize(incident), s.face)));
    float footprint = WorldPixelWidth(s.p) * SnowGrainsPerMeter / facing;
    [branch] if (footprint >= 1.35) return mean * upward;

    // The hash grid places grains. Jittered centers and aspect ratios give them irregular outlines.
    float2 position = s.p.xz * SnowGrainsPerMeter;
    int2 cell = (int2)floor(position);
    float2 local = position - cell;
    float2 grains[9];
    float nearest = 2;
    [unroll] for (int i = 0; i < 9; i++)
    {
        int2 neighbor = int2(i % 3 - 1, i / 3 - 1);
        grains[i] = SnowGrain(cell + neighbor, local - neighbor);
        nearest = min(nearest, grains[i].x);
    }

    // Blend competing grains across their edges. The cap excludes any site outside the 3x3 window.
    float inverseWidth = rcp(min(.30, .035 + .80 * footprint));
    float2 coverage = 0;
    [unroll] for (int i = 0; i < 9; i++)
    {
        float weight = saturate(1 - (grains[i].x - nearest) * inverseWidth);
        weight *= weight;
        float cover = saturate((depth - grains[i].y) / SnowGrainRamp);
        coverage += float2(weight * cover, weight);
    }
    float visibility = 1 - smoothstep(.52, 1.35, footprint);
    return lerp(mean, coverage.x / coverage.y, visibility) * upward;
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
void WetGround(inout Surface s, float exposed)
{
    if (exposed <= 0 || max(SkyWetness.w, Puddles.w) <= 0 || s.face.y <= .5) return;
    float2 p = s.p.xz;
    float shelter = 0;
    [unroll] for (uint i = 0; i < 4; i++)
    {
        float2 inset = min(p - RainShelters[i].xy, RainShelters[i].zw - p);
        shelter = max(shelter, smoothstep(.05, .95, min(inset.x, inset.y)));
    }
    float fill = Puddles.w * (1 - .94 * shelter);
    float film = max(SkyWetness.w, fill) * (1 - .82 * shelter);
    float basin = .61 * PuddleNoise(p * .34 + Puddles.xy) + .29 * PuddleNoise(p * .89 - Puddles.xy)
        + .10 * PuddleNoise(p * 2.7 + Puddles.xy);
    float bank = lerp(.82, .40, fill) + .06 * (.5 - Puddles.z) + (s.material == 9 ? .07 : 0);
    // Gravel crests interrupt the shallow margin. Higher water joins the same fixed depressions.
    float depth = basin - bank - .10 * length(s.n.xz);
    float footprint = WorldPixelWidth(s.p);
    float edgeWidth = clamp(.24 * footprint, .004, .022);
    float pool = smoothstep(-edgeWidth, .045 + edgeWidth, depth) * smoothstep(0, .14, fill);
    float dampBank = smoothstep(-.14, -.015, depth) * film;
    s.base *= 1 - .17 * dampBank;
    s.wetCoverage = lerp(film * (.26 + .23 * basin + .17 * dampBank), 1, pool) * exposed;
    s.waterRoughness = lerp(.30 - .07 * dampBank, .045, pool);
    float2 wobble = float2(PuddleNoise(p * 2.7 + 3.1), PuddleNoise(p * 2.7 - 11.4)) - .5;
    float2 slope = wobble * lerp(.009, .003, pool);
    if (HemiSkyRain.w > 0 && pool > 0 && shelter < 1)
    {
        float time = SkyBottom.w;
        float2 ripple = RainRipple(p, 2.3, time, 0) + .7 * RainRipple(p, 3.9, time * 1.13, 19);
        slope += ripple * (.018 * pool * (1 - shelter));
    }
    float3 up = float3(0, 1, 0);
    float3 rippleNormal = float3(slope.x, 0, slope.y) * (film * exposed);
    float exposedPool = pool * exposed;
    s.n = normalize(lerp(s.n, up, exposedPool) + rippleNormal);
    // Reuse the small local perturbation for the prior rigid normal. The
    // moving ripple itself is stochastic shading, not geometric screen motion.
    s.previousNormal = normalize(lerp(s.previousNormal, up, exposedPool) + rippleNormal);
}

float PanelDots(float2 uv, uint material, Vertex a, Vertex b, Vertex c, SceneInstance instance, float3 position)
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
    // Differentiate clip.xy / clip.w at the hit, including foreshortening across the sign.
    float4 clip = mul(float4(position, 1), ViewProjection);
    if (clip.w < .001) return .73;
    float4 abClip = mul(float4(ab, 0), ViewProjection);
    float4 acClip = mul(float4(ac, 0), ViewProjection);
    float2 ndc = clip.xy / clip.w;
    float2 scale = .5 * SizeJitter.xy / clip.w;
    float2 abScreen = (abClip.xy - ndc * abClip.w) * scale;
    float2 acScreen = (acClip.xy - ndc * acClip.w) * scale;
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
    if (s.material == 2)
    {
        if (frontFace)
        {
            float3 pixels = Lyrics.SampleLevel(LinearClamp, uv, 0).rgb;
            float diffuser = PanelDots(uv, s.material, a, b, c, instance, s.p);
            s.emission = BoardWhiteRadiance * pixels * diffuser;
        }
        s.base *= .14;
        s.roughness = .45;
    }
    if (s.material == 6)
    {
        float3 letters = max(Lyrics.SampleLevel(LinearClamp, uv, 0).rgb, 0) * 1.55;
        float peak = max(letters.r, max(letters.g, letters.b));
        letters *= min(1, 1.25 / max(peak, 1e-6));
        s.emission = s.base * .06 + letters * PanelDots(uv, s.material, a, b, c, instance, s.p);
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
    bool ground = GroundMaterial(s.material);
    bool textured = ground || (s.material >= 14 && s.material <= 16);
    float snow = s.material == 0 || textured ? SnowCoverage(s, direction) : 0;
    if (textured)
    {
        float3 smoothNormal = s.n;
        float3 previousSmoothNormal = s.previousNormal;
        SurfaceTexture(s, direction);
        if (ground) WetGround(s, 1 - snow);
        if (snow > 0)
        {
            s.n = normalize(lerp(s.n, smoothNormal, .65 * snow));
            s.previousNormal = normalize(lerp(s.previousNormal, previousSmoothNormal, .65 * snow));
        }
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
    if (snow > 0)
    {
        s.base = lerp(s.base, float3(.77, .84, .91), snow);
        s.roughness = lerp(s.roughness, .82, snow);
    }
    return s;
}

float3 Offset(Surface s, float3 direction)
{
    return s.p + s.face * (dot(direction, s.face) >= 0 ? Epsilon * 3 : -Epsilon * 3);
}
float3 DryF0(Surface s)
{
    // The pigment binder sits under varnish with a similar refractive index.
    // Its interface is weaker than an exposed dielectric/air boundary.
    return lerp(s.material == 13 ? .004 : .04, s.base, s.metal);
}
static const float CoatRoughness = .09;
float3 F0(Surface s) { return s.material == 13 ? .04 + (.96 * .96) * DryF0(s) : lerp(DryF0(s), WaterF0, s.wetCoverage); }
float GuideRoughness(Surface s) { return s.material == 13 ? CoatRoughness : lerp(s.roughness, s.waterRoughness, s.wetCoverage); }
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
    if (s.material == 13)
    {
        float coat = Fresnel(.04, nv).x;
        reflectance = coat + (1-coat)*(1-coat)*dry;
    }
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

// A clear dielectric varnish overlays the pigmented, partially metallic base.
// Sample the same mixture of normal distributions used for the MIS density.
float CoatProbability(Surface s, float nv)
{
    float coat = Fresnel(.04, nv).x;
    float base = dot(approxSpecularIntegralGGX(DryF0(s), GgxAlpha(s), nv), float3(.2126,.7152,.0722));
    return coat / max(coat + (1-coat)*(1-coat)*base, 1e-6);
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
    if (s.material == 13)
    {
        float2 coat = EvaluateGgx(CoatRoughness * CoatRoughness, nh, nv, nl);
        float transmission = (1 - Fresnel(.04, nv).x) * (1 - Fresnel(.04, nl).x);
        diffuse *= transmission;
        specular = specular * transmission + Fresnel(.04, vh).x * coat.x;
        specularPdf = lerp(dryPdf, coat.y, CoatProbability(s, nv));
    }
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
    if (s.material == 13 && layerSample < CoatProbability(s, saturate(dot(s.n, view))))
        alpha = CoatRoughness * CoatRoughness;
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

void DirectSun(Surface s, float3 view, float2 sample, out float3 d, out float3 r)
{
    d = r = 0;
    if (!any(SunRadianceFar.rgb > 0)) return;
    // Uniform solid angle on the cap. The half-angle form avoids subtracting near-equal cosines.
    float height = 2 * sin(.5 * CelestialRadius) * sin(.5 * CelestialRadius) * sample.x;
    float radial = sqrt(height * (2 - height));
    float phi = 2 * Pi * sample.y;
    float3 l = FromLocal(float3(radial * cos(phi), radial * sin(phi), 1 - height), SunDirectionFrame.xyz);
    float cosine = dot(s.n, l);
    if (cosine > 0)
    {
        float3 ds, rs;
        EvaluateBsdf(s, view, l, ds, rs);
        if (any(ds + rs > 0))
        {
            float3 energy = SunRadianceFar.rgb * (cosine / (cos(.5 * CelestialRadius) * cos(.5 * CelestialRadius)))
                * AnalyticVisibility(s, l, SunRadianceFar.w);
            d += ds * energy;
            r += rs * energy;
        }
    }
}

void DirectEmitters(Surface s, float3 view, float3 lightSample, bool separated, float primarySpecularRate,
    bool allAnalytic, out float3 d, out float3 r, uint emitterSamples = 1)
{
    d = r = 0;
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
    if (!Instances[emitter.instance].visible) return;
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
    if (Occluded(Offset(s, l), l, distance - Epsilon * 7)) return;
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
    uint3 path, uint stream, uint remaining, uint firstLightSamples)
{
    Plane result = (Plane)0;
    result.roughness = 1;
    result.normal = -direction;
    result.hitDistance = SunRadianceFar.w;
    bool priorAnalytic = true;
    [loop]
    for (uint step = 0; step <= remaining; step++)
    {
        Hit hit = Trace(origin, direction, SunRadianceFar.w);
        if (hit.primitive == 0xffffffff)
        {
            result.color += throughput * EnvironmentRadiance(direction);
            // Non-delta vertices already integrate the disk by direct light sampling.
            if (bsdfPdf == 0) result.color += throughput * CelestialDisk(direction);
            break;
        }
        Surface s = Evaluate(hit, origin);
        bool firstSurface = result.hitDistance == SunRadianceFar.w;
        uint vertex = stream * 64 + step;
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
            uint lightSamples = step == 0 ? firstLightSamples : 1;
            float weight = bsdfPdf > 0 ? PowerHeuristic(bsdfPdf, emitterPdf * lightSamples) : 1;
            result.color += throughput * s.emission * weight;
        }
        // At the budget boundary the last ray may still see a light or the sky.
        if (step == remaining) break;
        if (s.material != 1)
        {
            float3 d, r, sunD, sunR;
            DirectEmitters(s, -direction, PathSample(path, vertex, 3), false, 0, false, d, r);
            DirectSun(s, -direction, PathSample(path, vertex, 4).xy, sunD, sunR);
            result.color += throughput * (d + r + sunD + sunR);
        }
        priorAnalytic = false;
        if (!Scatter(s, PathSample(path, vertex, 1), direction, throughput, bsdfPdf)) break;
        // Apply roulette after non-delta scattering. Applying it at each layer
        // of a pane increases variance without sampling any new optical lobe.
        if (s.material != 1)
        {
            float survive = clamp(max(throughput.r, max(throughput.g, throughput.b)), .18, .92);
            if (PathSample(path, vertex, 6).x >= survive) break;
            throughput /= survive;
        }
        origin = Offset(s, direction);
    }
    result.color = min(max(result.color, 0), 320);
    return result;
}
Plane FollowOptical(float3 origin, float3 direction, float3 throughput, uint3 path, uint stream)
{
    Plane result = (Plane)0;
    result.hitDistance = SunRadianceFar.w;
    result.roughness = 1;
    result.normal = -direction;
    [loop]
    for (uint depth = 1; depth <= MaxBounces; depth++)
    {
        Hit hit = Trace(origin, direction, SunRadianceFar.w);
        if (hit.primitive == 0xffffffff)
        {
            result.deterministic = throughput * (EnvironmentRadiance(direction) + CelestialDisk(direction));
            return result;
        }
        Surface s = Evaluate(hit, origin);
        uint vertex = stream * 64 + depth;
        if (s.material == 1)
        {
            if (depth == MaxBounces) return result;
            // Keep the transmitted guide stable while sampling the secondary interface's reflected energy.
            float eta = s.entering ? 1 / 1.5 : 1.5;
            float fresnel = GlassFresnel(saturate(dot(s.n, -direction)), eta);
            if (fresnel < 1 && PathSample(path, vertex, 7).x < fresnel)
            {
                float3 reflected = reflect(direction, s.n);
                Plane side = Follow(Offset(s, reflected), reflected, throughput, 0, path, stream * 64 + depth,
                    MaxBounces - depth - 1, 1);
                result.color += side.color;
            }
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
        result.deterministic = throughput * s.emission;
        if (depth == MaxBounces) return result;
        result.deterministic += throughput * TotalAnalyticLighting(s, -direction);
        float3 sunDiffuse, sunSpecular;
        DirectSun(s, -direction, PathSample(path, vertex, 4).xy, sunDiffuse, sunSpecular);
        result.color += throughput * (sunDiffuse + sunSpecular);
        // Stratify the triangle alias-table sample. Use the same number of
        // light samples in the matching BSDF-path MIS weight.
        [loop]
        for (uint stratum = 0; stratum < OpticalLightSamples; stratum++)
        {
            float3 sample = PathSample(path, vertex, 3 + 32 * stratum);
            sample.x = (sample.x + stratum) / OpticalLightSamples;
            float3 d, r;
            DirectEmitters(s, -direction, sample, true, -1, true, d, r, OpticalLightSamples);
            result.color += throughput * (d + r) / OpticalLightSamples;
        }
        // Sample each lobe once and use its conditional PDF for MIS.
        [loop]
        for (uint lobe = 0; lobe < 2; lobe++)
        {
            float3 sample = PathSample(path, vertex, 1 + lobe);
            float3 outgoing = lobe == 0 ? CosineSample(s.n, sample.xy) : SampleSpecular(s, -direction, sample.xy, sample.z);
            float3 diffuse, specular;
            float2 pdf = EvaluateBsdf(s, -direction, outgoing, diffuse, specular);
            float probability = lobe == 0 ? pdf.x : pdf.y;
            if (probability <= 0) continue;
            float3 bsdf = lobe == 0 ? diffuse : specular;
            float3 weight = throughput * bsdf * (saturate(dot(s.n, outgoing)) / probability);
            Plane samplePath = Follow(Offset(s, outgoing), outgoing, weight, probability, path,
                stream * 2 + lobe + 9, MaxBounces - depth - 1, OpticalLightSamples);
            result.color += samplePath.color;
        }
        result.color = min(max(result.color, 0), 320);
        return result;
    }
    return result;
}

float2 MotionFromClip(float4 current, float4 previous)
{
    // A virtual reflection can cross the eye plane. Send its history lookup
    // off screen and keep even a grazing projection representable in RG16F.
    if (min(current.w, previous.w) <= .0001) return -SizeJitter.xy;
    float2 delta = previous.xy / previous.w - current.xy / current.w;
    return clamp(delta, -2, 2) * float2(.5, -.5) * SizeJitter.xy;
}
float2 Reproject(float3 p, float3 previous)
{
    return MotionFromClip(mul(float4(p, 1), ViewProjection), mul(float4(previous, 1), PreviousViewProjection));
}
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
    // Generating the ray directly avoids cancellation from unprojecting device Z=1.
    float3 eyeRay = mul(float3(ndc, 1), (float3x3)RayBasis);
    float nearDistance = RayBasis[2].w;
    float rayLength = length(eyeRay);
    float3 origin = RayBasis[3].xyz + eyeRay * nearDistance;
    float3 primary = eyeRay / rayLength;
    // 0: NRD guides. 1: raw only. 2: raw with temporal AA. 3: DLSS Ray Reconstruction guides.
    bool writeDenoiser = TraceGuideMode == 0;
    bool writeRayReconstruction = TraceGuideMode == 3;
    bool needsGeometryGuides = writeDenoiser || writeRayReconstruction;
    bool writeMotion = writeDenoiser || TraceGuideMode == 2;

    uint firstSample = (uint)SunDirectionFrame.w * SamplesPerPixel;
    float inverseSpp = rcp((float)SamplesPerPixel);
    float skyY = (pixel.y + .5) / SizeJitter.y;
    float3 d = 0, r = 0, dFactor = 1, sFactor = 1;
    float3 deterministic = lerp(SkyWetness.rgb, SkyBottom.rgb, skyY * skyY) + CelestialDisk(primary);
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
    Hit hit = Trace(origin, primary, (SunRadianceFar.w - nearDistance) * rayLength);
    if (hit.primitive != 0xffffffff)
    {
        Surface s = Evaluate(hit, origin);
        deterministic = s.emission;
        if (needsGeometryGuides)
        {
            worldNormal = s.n;
            roughness = GuideRoughness(s);
            rawAlbedo = s.material == 1 ? 0 : s.base * (1 - s.metal);
            rawSpecular = F0(s);
            z = SignedViewZ(s.p);
        }
        depth = DeviceDepth(s.p);
        if (writeMotion || writeRayReconstruction) motion = Reproject(s.p, s.previousP);
        if (s.material == 1)
        {
            float3 reflected = reflect(primary, s.n);
            float eta = s.entering ? 1 / 1.5 : 1.5;
            float fresnel = GlassFresnel(saturate(dot(s.n, -primary)), eta);
            float3 transmissionDirection = fresnel < 1 ? refract(primary, s.n, eta) : primary;
            float3 transmissionOrigin = Offset(s, transmissionDirection);
            float3 transmissionWeight = (1 - fresnel) * GlassTint(s.base);
            float3 reflectionWeight = fresnel;
            const float3 luma = float3(.2126, .7152, .0722);
            reflectionDominance = dot(reflectionWeight - transmissionWeight, luma);
            [loop]
            for (uint optical = 0; optical < (fresnel < 1 ? 2 : 1); optical++)
            {
                float3 direction = optical == 0 ? reflected : transmissionDirection;
                float3 originOnPane = optical == 0 ? Offset(s, direction) : transmissionOrigin;
                float3 throughput = optical == 0 ? reflectionWeight : transmissionWeight;
                Plane branch = (Plane)0;
                float3 radiance = 0;
                [loop]
                for (uint i = 0; i < SamplesPerPixel; i++)
                {
                    Plane sample = FollowOptical(originOnPane, direction, throughput, uint3(pixel, firstSample + i), optical + 1);
                    // Ideal transmission/reflection geometry is shared by all samples.
                    if (i == 0) branch = sample;
                    radiance += sample.color;
                }
                branch.color = radiance * inverseSpp;
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
            deterministic += TotalAnalyticLighting(s, view);
            if (writeDenoiser) NRD_MaterialFactors(s.n, view, rawAlbedo, rawSpecular, roughness, dFactor, sFactor);
            float3 momentD = 0, momentS = 0;
            uint diffuseRays = 0, specularRays = 0;
            [loop]
            for (uint i = 0; i < SamplesPerPixel; i++)
            {
                uint3 path = uint3(pixel, firstSample + i);
                float3 sampleD, sampleS, sunD, sunS;
                DirectEmitters(s, view, PathSample(path, 0, 3), true, wetRoad ? -1 : specularRate, true, sampleD, sampleS);
                DirectSun(s, view, PathSample(path, 0, 4).xy, sunD, sunS);
                sampleD += sunD;
                sampleS += sunS;
                float3 outgoingD = -primary, outgoingS = -primary;
                bool selectSpecular = PathSample(path, 0, 5).x < specularRate;
                // Wet surfaces sample both lobes. Other surfaces importance-sample one.
                [loop]
                for (uint lobe = 0; lobe < (wetRoad ? 2 : 1); lobe++)
                {
                    bool specular = wetRoad ? lobe == 1 : selectSpecular;
                    float3 samples = PathSample(path, 0, specular ? 2 : 1);
                    float3 direction = specular ? SampleSpecular(s, view, samples.xy, samples.z) : CosineSample(s.n, samples.xy);
                    float probability = wetRoad ? 1 : specular ? specularRate : 1 - specularRate;
                    float3 sampledDiffuse, sampledSpecular;
                    float2 conditionalPdf = EvaluateBsdf(s, view, direction, sampledDiffuse, sampledSpecular);
                    float pdf = (specular ? conditionalPdf.y : conditionalPdf.x) * probability;
                    if (pdf <= 0) continue;
                    float3 value = specular ? sampledSpecular : sampledDiffuse;
                    float3 weight = value * saturate(dot(s.n, direction)) / pdf;
                    Plane sample = Follow(Offset(s, direction), direction, weight, pdf, path, specular ? 4 : 3, MaxBounces - 1, 1);
                    if (specular)
                    {
                        sampleS += sample.color;
                        outgoingS = direction;
                        sDistance += sample.hitDistance;
                        specularRays++;
                    }
                    else
                    {
                        sampleD += sample.color;
                        outgoingD = direction;
                        dDistance += sample.hitDistance;
                        diffuseRays++;
                    }
                }
                d += sampleD;
                r += sampleS;
                if (writeDenoiser)
                {
                    momentD += outgoingD * _NRD_Luminance(sampleD / dFactor);
                    momentS += outgoingS * _NRD_Luminance(sampleS / sFactor);
                }
            }
            if (writeDenoiser)
            {
                // RELAX stores the luminance-weighted first moment in SH1.
                dDirection = momentD / max(1e-8, _NRD_Luminance(d / dFactor));
                sDirection = momentS / max(1e-8, _NRD_Luminance(r / sFactor));
                dDistance /= max(1u, diffuseRays);
            }
            if (needsGeometryGuides) sDistance /= max(1u, specularRays);
            d *= inverseSpp;
            r *= inverseSpp;
            // Keep the NRD hit distance stable when wet microfacets sample
            // different geometry. This ray supplies a guide, never radiance.
            if (writeDenoiser && Lighting.w == 0 && s.wetCoverage > .45)
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
    else if (writeMotion || writeRayReconstruction)
    {
        // A sky direction reprojects under camera rotation, independently of translation.
        motion = MotionFromClip(mul(float4(primary, 0), ViewProjection), mul(float4(primary, 0), PreviousViewProjection));
    }

    bool validR = false, validT = false;
    float reflectedZ = 0, transmittedZ = 0;
    float2 reflectionMotion = 0, transmissionMotion = 0;
    if (needsGeometryGuides)
    {
        reflectedZ = SignedViewZ(reflection.position);
        transmittedZ = SignedViewZ(transmission.position);
        validR = reflection.hitDistance < SunRadianceFar.w && reflectedZ < -.1 && reflectedZ > -SunRadianceFar.w;
        validT = transmission.hitDistance < SunRadianceFar.w && transmittedZ < -.1 && transmittedZ > -SunRadianceFar.w;
        reflectionMotion = validR ? Reproject(reflection.position, reflection.previousPosition) : motion;
        transmissionMotion = validT ? Reproject(transmission.position, transmission.previousPosition) : 0;
        ReflectionMotion[pixel] = reflectionMotion;
    }
    if (writeDenoiser)
    {
        float4 dSh, sSh;
        Diffuse[pixel] = RELAX_FrontEnd_PackSh(d / dFactor, dDistance, dDirection, dSh, true);
        Specular[pixel] = RELAX_FrontEnd_PackSh(r / sFactor, sDistance, sDirection, sSh, true);
        DiffuseSh[pixel] = dSh;
        SpecularSh[pixel] = sSh;
        Normal[pixel] = NRD_FrontEnd_PackNormalAndRoughness(worldNormal, roughness, 0);
        ViewZ[pixel] = z;
        DiffuseFactor[pixel] = float4(dFactor, 1);
        SpecularFactor[pixel] = float4(sFactor, 1);

        Reflection[pixel] = RELAX_FrontEnd_PackRadianceAndHitDist(validR ? reflection.color : 0, reflection.hitDistance, true);
        ReflectionNormal[pixel] = NRD_FrontEnd_PackNormalAndRoughness(reflection.normal, reflection.roughness, 0);
        ReflectionZ[pixel] = validR ? reflectedZ : -1e6;
        Transmission[pixel] = RELAX_FrontEnd_PackRadianceAndHitDist(validT ? transmission.color : 0, transmission.hitDistance, true);
        TransmissionNormal[pixel] = NRD_FrontEnd_PackNormalAndRoughness(transmission.normal, transmission.roughness, 0);
        TransmissionZ[pixel] = validT ? transmittedZ : -1e6;
        TransmissionMotion[pixel] = transmissionMotion;
        Deterministic[pixel] = float4(min(NRD_FP16_MAX, deterministic + reflection.deterministic + transmission.deterministic
            + (validR ? 0 : reflection.color) + (validT ? 0 : transmission.color)), 1);
    }
    if (writeMotion) Motion[pixel] = motion;
    Noisy[pixel] = float4(min(NRD_FP16_MAX, deterministic + reflection.deterministic + transmission.deterministic
        + d + r + reflection.color + transmission.color), 1);
    Depth[pixel] = depth;
    if (writeRayReconstruction)
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
