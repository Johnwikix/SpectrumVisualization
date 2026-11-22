// Perspective atmosphere, steam, lens and weather in the Machi post passes.
struct SteamEmitter {
    float4 PositionHeight;
    float4 Profile; // base radius, tip radius, extinction per metre, weather response
    float4 Flow; // horizontal drift, turbulence, sunlight visibility
    float4 LightPosition, LightRadiance;
};
cbuffer MachiPost : register(b1) {
    row_major float4x4 Camera, InverseCamera;
    float4 HorizonMist, Weather, Scene;
    float3 SkyTop;
    float FocusDistance;
    float4 Precipitation, RoofY, Roofs[4], Glints[3], Drips[192];
    float2 DepthUvOffset;
    float ChromaticAberrationEnabled, DepthOfFieldEnabled;
    SteamEmitter Steam[2];
    float4 SteamAmbient, SteamSunDirection, SteamSunRadiance;
};
float Noise2(float2 p) {
    float2 cell=floor(p), t=frac(p);
    t=t*t*(3-2*t);
    return lerp(lerp(Hash(cell), Hash(cell+float2(1,0)),t.x),
                lerp(Hash(cell+float2(0,1)),Hash(cell+1),t.x), t.y);
}
float Fbm(float2 p) {
    return .56*Noise2(p)+.28*Noise2(p*2.07+4.3)+.16*Noise2(p*4.13-1.8);
}
float SampleDeviceDepth(float2 uv) {
    return BackgroundHistoryTexture.SampleLevel(LinearClamp,uv+DepthUvOffset,0).x;
}
float ViewDistance(float depth) {
    // The inverse projection's W column is independent of XY for a perspective camera.
    return rcp(depth * InverseCamera[2].w + InverseCamera[3].w);
}
float SurfaceDepth(float2 position) {
    uint width,height;
    BackgroundHistoryTexture.GetDimensions(width,height);
    uint2 size=uint2(width,height);
    int2 texel=int2(floor((position/Screen.xy+DepthUvOffset)*size));
    return BackgroundHistoryTexture.Load(int3(clamp(texel,0,int2(size)-1),0)).x;
}
float4 AtmospherePS(float4 pos:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float3 rgb=BackdropTexture.SampleLevel(LinearClamp,uv,0).rgb;
    float depth=SampleDeviceDepth(uv);
    if (depth>=1) {
        // DXR supplies the sky gradient and the finite solar or lunar disk.
        float2 p=float2(uv.x*Screen.x/Screen.y,1-uv.y)*2.4+float2(Scene.y*.012,0);
        float overcast=max(Weather.x,max(Weather.y,Weather.z));
        float cloud=smoothstep(lerp(.62,.38,overcast),.85,Fbm(p))*smoothstep(0,.55,1-uv.y)*(.35+.65*overcast);
        float wisp=smoothstep(.55,.9,Fbm(p*2.3+float2(Scene.y*.02,3.1)))*(.5+.5*overcast);
        rgb+=HorizonMist.rgb*(cloud*.55+wisp*.18)+float3(.58,.69,1)*Scene.z*(.18+1.1*cloud+.4*wisp);
        rgb=lerp(rgb,HorizonMist.rgb,Weather.z*.7);
    } else {
        float4 world=mul(float4(uv*float2(2,-2)+float2(-1,1),depth,1),InverseCamera);
        world.xyz/=world.w;
        float2 wind=float2(.04,.024)*Scene.y;
        float drift=.65*Fbm(world.xz*.32+wind)+.35*Fbm(world.xz*.9-wind*1.7);
        float h=max(world.y,0);
        float3 mist=HorizonMist.rgb*(1.35-.4*Weather.w);
        rgb=lerp(rgb,mist,saturate((.65*Weather.z+.025*Weather.x)*exp(-h/.55)*(.35+.9*drift)));
        rgb=lerp(rgb,mist,Weather.z*(.3+.7*smoothstep(-10,10,ViewDistance(depth)-FocusDistance)))*(1+.18*Scene.z);
    }
    return float4(rgb,1);
}
// Keep the foreground and backdrop apertures continuous as tilt crosses the focus plane.
float2 BlurRadii(float2 uv) {
    float offset=ViewDistance(SampleDeviceDepth(uv))-FocusDistance;
    float depth=sign(offset)*smoothstep(3.4,10.9,abs(offset));
    float tilt=.3*smoothstep(.2,.55,abs(uv.y-.5));
    return float2(max(depth,tilt*(1-saturate(-depth))),max(-depth,0))*(.006*Screen.y);
}
float4 LensPS(float4 pos:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float2 centered=uv-.5;
    float3 color=LayerTexture.SampleLevel(LinearClamp,uv,0).rgb;
    [branch] if (ChromaticAberrationEnabled>.5) {
        float2 shift=centered*dot(centered,centered)*.003;
        color.r=LayerTexture.SampleLevel(LinearClamp,uv+shift,0).r;
        color.b=LayerTexture.SampleLevel(LinearClamp,uv-shift,0).b;
    }
    [branch] if (DepthOfFieldEnabled>.5) {
        float radius=.006*Screen.y;
        float2 mid=BlurRadii(uv);
        float2 dir=float2(1,0);
        // The center anchors a barely defocused edge. Opposite taps enter the aperture
        // together so changing focus cannot translate a straight pole sideways.
        float4 nearSum=0,farSum=float4(color,1);
        [unroll] for(int i=0;i<8;i++) {
            float ring=(i+.5)/8;
            float r=ring*radius;
            float area=2*ring;
            float2 delta=dir*r/Screen.xy;
            dir=float2(-.73736888*dir.x-.67549029*dir.y,.67549029*dir.x-.73736888*dir.y);
            float2 plus=BlurRadii(uv+delta),minus=BlurRadii(uv-delta);
            float4 a=float4(LayerTexture.SampleLevel(LinearClamp,uv+delta,0).rgb,1);
            float4 b=float4(LayerTexture.SampleLevel(LinearClamp,uv-delta,0).rgb,1);
            float farRadius=min(mid.x,min(plus.x,minus.x));
            farSum+=(a+b)*(area*saturate((farRadius-r+1.2)/1.2));
            float2 nearRadius=float2(plus.y,minus.y);
            float2 weight=saturate((nearRadius-r+1.2)/1.2)*smoothstep(0,1,nearRadius);
            // Within a foreground surface preserve its centroid. At a silhouette
            // the near tap alone can still spread over the background.
            float inside=smoothstep(0,1,min(mid.y,min(plus.y,minus.y)));
            weight=lerp(weight,min(weight.x,weight.y),inside);
            nearSum+=area*(a*weight.x+b*weight.y);
        }
        color=lerp(color,farSum.rgb/farSum.a,saturate(mid.x));
        color=lerp(color,nearSum.rgb/max(nearSum.a,.0001),saturate(nearSum.a*3.14159/16));
    }
    float2 v=centered*float2(Screen.x/Screen.y,1);
    float sky=SampleDeviceDepth(uv)>=1;
    float stars=(1-Weather.w)*(1-max(Weather.x,max(Weather.y,saturate(Weather.z/.42))))*(1-smoothstep(.05,.35,Weather.w));
    if (stars>.001 && sky) {
        float2 grid=float2(uv.x*Screen.x/Screen.y,1-uv.y)*140;
        float2 cell=floor(grid);
        float seed=Hash(cell);
        float2 starAt=.2+.6*float2(Hash(cell+3.7),Hash(cell+9.1));
        float star=step(.984,seed)*(1-smoothstep(0,.24,length(frac(grid)-starAt)))*(.55+.45*sin(Scene.y*(.8+seed*3)+seed*60));
        color+=float3(.85,.9,1)*star*stars*1.3*smoothstep(.1,.55,1-uv.y);
    }
    color*=1-.18*smoothstep(.35,1.15,length(v)*1.15);
    // Expose for the street after dusk. Keep the toe linear so low-reflectance
    // asphalt and dark lacquer retain detail under indirect illumination.
    color=max(color,0)*lerp(1.65,1,Weather.w);
    // PBR Neutral highlight compression for SDR. HDR uses the output transform.
    float peak=max(color.r,max(color.g,color.b));
    if(Scene.w<.5 && peak>.76) {
        float mapped=1-.24*.24/(peak-.52);
        color=lerp(color*(mapped/peak),mapped,1-1/(1+.15*(peak-mapped)));
    }
    return float4(color,1);
}
// Roof bounds and downspout cycles are baked from the corresponding upstream set.
float GroundHeight(float2 xz) {
    float y=Precipitation.x;
    [unroll] for(int i=0;i<4;++i) {
        float4 r=Roofs[i];
        if(i<Precipitation.w && all(xz>r.xy) && all(xz<r.zw)) y=max(y,RoofY[i]);
    }
    return y;
}
struct Particle {float4 pos:SV_Position;float2 uv:TEXCOORD0;nointerpolation uint kind:TEXCOORD1;float strength:TEXCOORD2;float glint:TEXCOORD3;};
// Paired with the instance count in FoliaGpuEffect.NativeStages.
static const uint FlakesPerLayer = 3072, DripStart = FlakesPerLayer * 2;
cbuffer WeatherDraw : register(b2) { uint FirstSkippedParticle; };
Particle WeatherVS(uint vertex:SV_VertexID,uint id:SV_InstanceID) {
    // Keep the original particle seeds and drip indices when either layer is absent.
    id += id >= FirstSkippedParticle ? FlakesPerLayer : 0;
    Particle o;
    float2 q=float2(vertex==1||vertex==2||vertex==4,vertex>=2&&vertex<=4);
    float seed=Hash(float2(id,41)), phase=Hash(float2(id,27));
    o.uv=q; o.strength=1; o.glint=0;
    float2 base=(float2(Hash(float2(id,3)),Hash(float2(id,8)))*2-1)*Scene.x;
    float3 head;
    if (id>=DripStart) {
        uint drip=(id-DripStart)*2;
        float4 d=Drips[drip], timing=Drips[drip+1];
        float t=fmod(Scene.y+timing.y*timing.x,timing.x);
        float falling=sqrt(2*max(0,d.z-d.w)/9.8);
        float3 p=float3(d.x,d.z-.5*9.8*t*t,d.y);
        p.xz+=(q-.5)*.025;
        p.y+=(q.y-.5)*(.03+.05*t/max(falling,.001));
        o.kind=2;
        o.strength=t<falling ? Weather.x : 0;
        o.pos=mul(float4(p,1),Camera);
        return o;
    }
    bool snow=id>=FlakesPerLayer;
    float ground=GroundHeight(base);
    float fall=max(.01,9.5-ground);
    float speed=snow?.75+.45*seed:11*(.85+.3*seed);
    float y=9.5-frac(Scene.y*speed/fall+phase)*fall;
    float2 drift=(9.5-y)*Precipitation.yz*(snow?.6:1);
    if(snow)drift+=float2(1,.6)*(.22*sin(Scene.y*1.1+phase*40)+.08*sin(Scene.y*2.3+seed*23));
    head=float3(base.x+drift.x,y,base.y+drift.y);
    float4 clipPos=mul(float4(head,1),Camera);
    float3 up=normalize(float3(-Precipitation.y,1,-Precipitation.z));
    float4 tail=mul(float4(head+up*.42*(.75+.5*seed),1),Camera);
    o.pos=snow?clipPos:lerp(clipPos,tail,q.y);
    o.pos.xy+=float2((q.x-.5)*(snow?2.4:.75)/Screen.x,snow?(q.y-.5)*2.4/Screen.y:0)*2*o.pos.w;
    o.kind=snow?1:0;
    o.strength=(snow?Weather.y:Weather.x)*smoothstep(0,.6,y-ground);
    if(any(abs(head.xz)>Scene.x+.1))o.strength=0;
    [unroll]for(int i=0;i<3;i++){
        float3 d=head-Glints[i].xyz;
        o.glint+=exp(-dot(d,d)/max(.001,Glints[i].w*Glints[i].w));
    }
    return o;
}
float4 WeatherPS(Particle p):SV_Target {
    float depth=SurfaceDepth(p.pos.xy);
    clip(depth-p.pos.z);
    float edge=length(2*p.uv-1);
    float3 color=lerp(float3(.11,.18,.29),float3(.68,.76,.93),saturate(p.glint)*(1-.9*Weather.w));
    float a;
    if(p.kind==0) a=.24*(1-.85*p.uv.y);
    else if(p.kind==1){a=.72*saturate(1-edge);color=float3(.78,.86,.96);}
    else a=.18;
    a*=p.strength;
    return float4(color*a,a);
}

struct SteamVertex {
    float4 pos:SV_Position;
    nointerpolation uint emitter:TEXCOORD0;
};

float2 SteamDrift(SteamEmitter source) {
    return source.Flow.xy + Precipitation.yz * .6;
}

void SteamBounds(SteamEmitter source, out float3 lower, out float3 upper) {
    float2 drift = SteamDrift(source);
    float radius = max(source.Profile.x, source.Profile.y) + source.Flow.z;
    lower = source.PositionHeight.xyz + float3(min(drift.x, 0) - radius, 0, min(drift.y, 0) - radius);
    upper = source.PositionHeight.xyz + float3(max(drift.x, 0) + radius, source.PositionHeight.w, max(drift.y, 0) + radius);
}

SteamVertex SteamVS(uint vertex:SV_VertexID,uint id:SV_InstanceID) {
    float3 lower, upper;
    SteamBounds(Steam[id], lower, upper);
    float2 minimum = 1, maximum = -1;
    // The projected bounds keep the ray integration off the rest of the target.
    [unroll] for (uint corner = 0; corner < 8; ++corner) {
        float3 basis = float3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1);
        float4 clipPos = mul(float4(lerp(lower, upper, basis), 1), Camera);
        float2 ndc = clipPos.xy / max(clipPos.w, .0001);
        minimum = min(minimum, ndc);
        maximum = max(maximum, ndc);
    }
    float2 quad = float2(vertex == 1 || vertex == 2 || vertex == 4, vertex >= 2 && vertex <= 4);
    SteamVertex o;
    o.pos = float4(clamp(lerp(minimum, maximum, quad), -1, 1), 0, 1);
    o.emitter = id;
    return o;
}

float SteamHash(float3 p) {
    p = frac(p * .1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float SteamNoise(float3 p) {
    float3 cell = floor(p), f = frac(p);
    f = f * f * (3 - 2 * f);
    float4 lower = float4(SteamHash(cell), SteamHash(cell + float3(1, 0, 0)),
                          SteamHash(cell + float3(0, 1, 0)), SteamHash(cell + float3(1, 1, 0)));
    cell.z++;
    float4 upper = float4(SteamHash(cell), SteamHash(cell + float3(1, 0, 0)),
                          SteamHash(cell + float3(0, 1, 0)), SteamHash(cell + float3(1, 1, 0)));
    float4 z = lerp(lower, upper, f.z);
    float2 x = lerp(z.xz, z.yw, f.x);
    return lerp(x.x, x.y, f.y);
}

float SteamDensity(SteamEmitter source, float3 world, float2 drift) {
    float3 local = world - source.PositionHeight.xyz;
    float h = saturate(local.y / source.PositionHeight.w);
    float time = Scene.y;
    float seed = dot(source.PositionHeight.xz, float2(1.82, .71));
    float2 twist = sin(h * float2(7.7, 5.9) + time * float2(-1.4, -1.12) + float2(seed, seed + 1.9));
    float2 plume = local.xz - drift * (h * (2 - h)) - twist * (source.Flow.z * h);
    float radius = lerp(source.Profile.x, source.Profile.y, h);
    float core = saturate(1 - dot(plume, plume) / (radius * radius));
    float density = 0;
    [branch] if (core > 0) {
        // Noise advects through the plume in world space as the steam rises and dilutes.
        float3 field = float3(plume.x * 7.3, local.y * 4.4, plume.y * 7.3) + seed;
        field.y -= time * .86;
        float noise = .68 * SteamNoise(field) + .32 * SteamNoise(field * 2.31 + float3(7, 3, 11));
        float strands = lerp(.13, 1, smoothstep(.27, .77, noise));
        float dilution = smoothstep(0, .07, h) * (1 - smoothstep(.59, 1, h));
        density = source.Profile.z * dilution * core * core * strands;
    }
    return density;
}

float3 SteamWorldAtDepth(float2 ndc, float depth) {
    float4 p = mul(float4(ndc, depth, 1), InverseCamera);
    return p.xyz / p.w;
}

float4 SteamPS(SteamVertex i):SV_Target {
    SteamEmitter source = Steam[i.emitter];
    float2 ndc = i.pos.xy / Screen.xy * float2(2, -2) + float2(-1, 1);
    float3 origin = SteamWorldAtDepth(ndc, 0);
    float3 ray = normalize(SteamWorldAtDepth(ndc, 1) - origin);
    float3 lower, upper;
    SteamBounds(source, lower, upper);
    float3 inverseRay = (2 * step(0, ray) - 1) / max(abs(ray), .00001);
    float3 a = (lower - origin) * inverseRay, b = (upper - origin) * inverseRay;
    float3 first = min(a, b), last = max(a, b);
    float start = max(0, max(first.x, max(first.y, first.z)));
    float end = min(last.x, min(last.y, last.z));
    [branch] if (end <= start) return 0;

    float depth = SurfaceDepth(i.pos.xy);
    // DXR depth is device Z. Clip the ray segment at the first surface
    // before sampling density so thin roofs and rails occlude the whole volume behind them.
    [branch] if (depth < 1) {
        float3 surface = SteamWorldAtDepth(ndc, depth);
        end = min(end, dot(surface - origin, ray) - .008);
    }
    [branch] if (end <= start) return 0;

    const int sampleCount = 16;
    float stepLength = (end - start) / sampleCount;
    float3 samplePosition = origin + ray * (start + .5 * stepLength);
    float2 drift = SteamDrift(source);
    float moisture = max(Weather.x, max(Weather.y, Weather.z));
    float strength = 1 + source.Profile.w * moisture;
    // Single scattering uses daylight and one nearby practical light per emitter.
    const float g = .38;
    float angle = 1 + g*g - 2*g*dot(ray, SteamSunDirection.xyz);
    float phase = ((1 - g*g) / 12.56637) * rsqrt(angle * angle * angle);
    float3 sky = SteamAmbient.rgb * lerp(.48, 1, source.Flow.w)
               + SteamSunRadiance.rgb * (phase * source.Flow.w) + float3(.070, .086, .12) * Scene.z;
    float3 scattering = 0;
    float transmission = 1;
    [loop] for (int stepIndex = 0; stepIndex < sampleCount; ++stepIndex) {
        float density = SteamDensity(source, samplePosition, drift);
        [branch] if (density > .0001) {
            float3 toLight = source.LightPosition.xyz - samplePosition;
            float3 light = sky + source.LightRadiance.rgb / (.24 + 3 * dot(toLight, toLight));
            float segmentTransmission = exp(-density * strength * stepLength);
            scattering += transmission * (1 - segmentTransmission) * light;
            transmission *= segmentTransmission;
        }
        samplePosition += ray * stepLength;
    }
    return float4(scattering, 1 - transmission);
}
