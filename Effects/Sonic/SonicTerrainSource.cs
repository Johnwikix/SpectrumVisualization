namespace WinExSpectrumTest.Effects.Sonic;

/// <summary>Preserves the existing terrain palette while rasterizing instanced bar faces.</summary>
internal static class SonicTerrainSource
{
    internal const string Code = """
        Texture2D<float4> field : register(t0);
        Texture2D<float4> previousField : register(t1);
        cbuffer Scene : register(b0) { float4 d[13]; float4 previous[13]; float4 jitter; float4 temporal; float4 camera; };
        float ProjectDepth(float z) { return (z-camera.x)*camera.y/(camera.y-camera.x); }
        struct Vertex {
            float4 position : SV_Position;
            float3 world : TEXCOORD0;
            nointerpolation float3 normal : TEXCOORD1;
            nointerpolation float2 center : TEXCOORD2;
            nointerpolation float4 values : TEXCOORD3;
            float4 currentClip : TEXCOORD4;
            float4 previousClip : TEXCOORD5;
            nointerpolation float reactive : TEXCOORD6;
        };
        Vertex TerrainVS(uint id : SV_VertexID, uint instance : SV_InstanceID) {
            const float2 corners[6] = {float2(0,0),float2(0,1),float2(1,0),float2(1,0),float2(0,1),float2(1,1)};
            float2 uv = corners[id % 6];
            uint face = id / 6;
            uint grid = (uint)d[6].w;
            uint2 cell = uint2(instance % grid, instance / grid);
            float4 values = field.Load(int3(cell, 0));
            float2 center = float2(cell) * d[3].w - d[4].w + d[3].w * .5;
            float3 p, normal;
            if (face == 0) { p = float3(uv.x,1,uv.y); normal = float3(0,1,0); }
            else if (face == 1) { p = float3(1,uv.y,uv.x); normal = float3(1,0,0); }
            else if (face == 2) { p = float3(0,uv.x,uv.y); normal = float3(-1,0,0); }
            else if (face == 3) { p = float3(uv.y,uv.x,1); normal = float3(0,0,1); }
            else { p = float3(uv.x,uv.y,0); normal = float3(0,0,-1); }
            float3 world = float3(center.x + (p.x-.5)*d[3].w*.857, p.y*values.x, center.y + (p.z-.5)*d[3].w*.857);
            float3 v = world - d[0].xyz;
            float z = dot(v, d[3].xyz);
            Vertex o;
            o.position = float4(dot(v,d[1].xyz)/(d[0].w*d[1].w), dot(v,d[2].xyz)/d[0].w, ProjectDepth(z), z);
            o.currentClip = o.position;
            o.previousClip = o.position;
            o.reactive = 0;
            if (temporal.w > .5) {
                float4 oldValues = previousField.Load(int3(cell,0));
                float3 oldWorld = float3(world.x,p.y*oldValues.x,world.z);
                float3 oldV = oldWorld-previous[0].xyz;
                float oldZ = dot(oldV,previous[3].xyz);
                o.previousClip = float4(dot(oldV,previous[1].xyz)/(previous[0].w*previous[1].w),
                    dot(oldV,previous[2].xyz)/previous[0].w,ProjectDepth(oldZ),oldZ);
                float3 delta = abs(values.yzw-oldValues.yzw);
                o.reactive = saturate(max(delta.x,max(delta.y,delta.z))*2 + temporal.z);
                o.position.xy += float2(jitter.x,-jitter.y)*2/temporal.xy*o.position.w;
            }
            o.world = world; o.normal = normal; o.center = center; o.values = values;
            return o;
        }
        float random(float2 p) { return frac(sin(dot(p,float2(12.9898,78.233)))*43758.5453123); }
        float3 toSrgb(float3 c) {
            return lerp(c*12.92, 1.055*pow(max(c,0),1.0/2.4)-.055, saturate((c-.0031308)*1e6));
        }
        float3 toLinear(float3 c) {
            return lerp(c/12.92, pow(max((c+.055)/1.055,0),2.4), saturate((c-.04045)*1e6));
        }
        float4 TerrainColor(Vertex v, out float animated) {
            animated = 0;
            float rnd = random(v.center);
            float relativeY = saturate(v.world.y / v.values.x);
            float distFromTop = 1-relativeY;
            float centerDist = length(v.center);
            float normElevation = saturate((v.values.x-1)/8);
            float warmth = d[9].w, brightness = d[10].w, sharpness = d[11].w;
            float presence = d[12].x, brilliance = d[12].y, air = d[12].z;
            float warmBlend = smoothstep(0,1,warmth*1.5+(.5-centerDist/80));
            float3 zoneCore = lerp(d[6].xyz,d[8].xyz,warmBlend);
            float3 zoneEdge = lerp(d[7].xyz,d[9].xyz,warmBlend);
            float3 targetGlow = lerp(zoneCore,zoneEdge,frac(rnd*11));
            float distFade = 1-smoothstep(40,75,centerDist);
            targetGlow = lerp(targetGlow,float3(.4,.8,1),brightness*.6);
            float3 currentGlow = lerp(d[5].xyz,targetGlow,normElevation)*d[5].w*distFade;
            float normalBlend = v.values.y*v.values.y, whiteBlend = v.values.z*v.values.z;
            currentGlow = lerp(currentGlow,d[10].xyz,normalBlend*.85);
            currentGlow = lerp(currentGlow,1,whiteBlend*.9);
            float peakBlend = pow(v.values.w,.85)*d[7].w*d[8].w;
            currentGlow = lerp(currentGlow,d[11].xyz,saturate(peakBlend)*.7);
            float3 bodyColor = lerp(d[4].xyz,d[5].xyz,relativeY*distFade);
            float3 color;
            if (v.normal.y > .5) {
                float topIntensity = smoothstep(0,.4,normElevation) + saturate(peakBlend*.4);
                float twinkleFalloff = smoothstep(60,30,centerDist);
                float twinkle = lerp(twinkleFalloff,1,smoothstep(.01,.1,normElevation));
                if (frac(rnd*31) > .95 && normElevation < .1) topIntensity += air*2*twinkle;
                color = lerp(d[5].xyz,currentGlow,topIntensity);
                float2 uv = (v.world.xz-v.center)/(d[3].w*.857)+.5;
                float2 edges = smoothstep(.05,.01,uv)+smoothstep(.95,.99,uv);
                float edge = min(edges.x+edges.y,1);
                color += currentGlow*edge*.8*(topIntensity+.3);
                float flashChance = smoothstep(.5,1,presence);
                if (frac(rnd*53) > .985-flashChance*.05) {
                    float flash = sin(d[2].w*8+rnd*50)*.5+.5;
                    float oldFlash = sin(previous[2].w*8+rnd*50)*.5+.5;
                    animated = saturate(abs(flash-oldFlash)*presence*(1+sharpness*1.5));
                    color += lerp(float3(1,1,1),float3(.5,1,1),rnd)*flash*presence*(1+sharpness*1.5)*twinkle;
                }
                float phase = sin(d[2].w*1.5+rnd*30)*.5+.5;
                if (edge > .6 && frac(rnd*89) > .992 && phase > .7) {
                    color += brilliance*2*twinkle*phase;
                    animated = max(animated,saturate(brilliance*.5));
                }
            } else {
                float falloff = lerp(1,3,sharpness);
                float sideGlow = smoothstep(.5/falloff,0,distFromTop)*normElevation;
                if (normElevation < .02) sideGlow = 0;
                float3 sideColor = lerp(currentGlow,d[11].xyz,saturate(peakBlend*.4));
                color = lerp(bodyColor,sideColor,sideGlow*1.5);
                float rim = smoothstep(.03,0,distFromTop)*normElevation;
                color += lerp(currentGlow,d[11].xyz,saturate(peakBlend*.35))*rim;
            }
            color = lerp(color,d[11].xyz,saturate(peakBlend)*.15);
            color += d[10].xyz*normalBlend*.4 + whiteBlend*.7;
            color = lerp(color,lerp(d[4].xyz,d[5].xyz,.4),smoothstep(30,65,centerDist)*.5);
            float fade = 1-smoothstep(55,78,centerDist);
            // Preserve the legacy display palette and opaque sky composition.
            return float4(toLinear(color*fade+toSrgb(d[4].xyz)*(1-fade)),1);
        }
        float4 TerrainPS(Vertex v) : SV_Target { float animated; return TerrainColor(v,animated); }
        void ParticleVS(float2 p : POSITION, float4 c : COLOR, out float4 position : SV_Position, out float4 color : COLOR) {
            position = float4(p,0,1); color = c;
        }
        float4 ParticlePS(float4 position : SV_Position, float4 color : COLOR) : SV_Target { return color; }
        struct TemporalOutput { float4 color : SV_Target0; float2 motion : SV_Target1; float reactive : SV_Target2; };
        TemporalOutput TerrainTemporalPS(Vertex v) {
            TemporalOutput o;
            float animated;
            o.color = TerrainColor(v,animated);
            float2 current = v.currentClip.xy/max(v.currentClip.w,.0001);
            float2 previous = v.previousClip.xy/max(v.previousClip.w,.0001);
            o.motion = (previous-current)*float2(.5,-.5)*temporal.xy;
            o.reactive = v.previousClip.w <= camera.x ? 1 : max(v.reactive,animated);
            return o;
        }
        """;
}
