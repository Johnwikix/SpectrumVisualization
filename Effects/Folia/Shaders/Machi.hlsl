// Machi's orthographic depth outline and signed foreground/background lens gather.
// Both passes work in linear HDR before the common SDR/HDR output transform.
cbuffer MachiPost : register(b1) {
    row_major float4x4 Camera, InverseCamera;
    float4 HorizonMist, Weather, Scene, SkyTop;
    float4 Precipitation, RoofY, Roofs[4], Glints[3], Drips[192];
    float4 ConeApex, ConeTarget, ConeColor, GlowA, GlowB, GlowColorA, GlowColorB, Steam[64];
    float2 DepthUvOffset;
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
float MetricDepth(float2 uv) {
    return BackgroundHistoryTexture.SampleLevel(LinearClamp,uv+DepthUvOffset,0).x*199.9;
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
    float2 px=1/Screen.xy;
    float d=MetricDepth(uv);
    if (d>199.88) {
        rgb=lerp(SkyTop.rgb,HorizonMist.rgb,uv.y*uv.y);
        float2 p=float2(uv.x*Screen.x/Screen.y,1-uv.y)*2.4+float2(Scene.y*.012,0);
        float overcast=max(Weather.x,max(Weather.y,Weather.z));
        float cloud=smoothstep(lerp(.62,.38,overcast),.85,Fbm(p))*smoothstep(0,.55,1-uv.y)*(.35+.65*overcast);
        float wisp=smoothstep(.55,.9,Fbm(p*2.3+float2(Scene.y*.02,3.1)))*(.5+.5*overcast);
        rgb+=HorizonMist.rgb*(cloud*.55+wisp*.18)+float3(.58,.69,1)*Scene.z*(.18+1.1*cloud+.4*wisp);
        rgb=lerp(rgb,HorizonMist.rgb,Weather.z*.7);
    } else {
        float edge=abs(MetricDepth(uv-float2(px.x,0))+MetricDepth(uv+float2(px.x,0))-2*d)
                  +abs(MetricDepth(uv-float2(0,px.y))+MetricDepth(uv+float2(0,px.y))-2*d)
                  +.5*(abs(MetricDepth(uv-px)+MetricDepth(uv+px)-2*d)
                      +abs(MetricDepth(uv+float2(px.x,-px.y))+MetricDepth(uv+float2(-px.x,px.y))-2*d));
        rgb=lerp(rgb,ToLinear(float3(11,15,26)/255),.82*smoothstep(.05,.13,edge));
        float4 world=mul(float4(uv*float2(2,-2)+float2(-1,1),d/199.9,1),InverseCamera);
        world.xyz/=world.w;
        float2 wind=float2(.04,.024)*Scene.y;
        float drift=.65*Fbm(world.xz*.32+wind)+.35*Fbm(world.xz*.9-wind*1.7);
        float h=max(world.y,0);
        float2 dA=world.xz-GlowA.xz,dB=world.xz-GlowB.xz;
        float3 mist=HorizonMist.rgb*(1.35-.4*Weather.w);
        float3 lit=mist + (1-Weather.w)*(.55*GlowColorA.rgb*exp(-dot(dA,dA)/(GlowA.w*GlowA.w)-h/2.2)
            +.42*GlowColorB.rgb*exp(-dot(dB,dB)/(GlowB.w*GlowB.w)-h/3));
        rgb=lerp(rgb,lit,saturate((HorizonMist.a+.06*Weather.x)*exp(-h/.55)*(.35+.9*drift)));
        rgb=lerp(rgb,mist,Weather.z*(.3+.7*smoothstep(50,70,d)))*(1+.18*Scene.z);
    }
    return float4(rgb,1);
}
// Keep the foreground and backdrop apertures continuous as tilt crosses the focus plane.
float2 BlurRadii(float2 uv) {
    float offset=MetricDepth(uv)-59.9;
    float depth=sign(offset)*smoothstep(3.4,10.9,abs(offset));
    float tilt=.3*smoothstep(.2,.55,abs(uv.y-.5));
    return float2(max(depth,tilt*(1-saturate(-depth))),max(-depth,0))*(.012*Screen.y);
}
float4 LensPS(float4 pos:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float2 centered=uv-.5;
    float2 shift=centered*dot(centered,centered)*.014;
    float3 center=float3(LayerTexture.SampleLevel(LinearClamp,uv+shift,0).r,
        LayerTexture.SampleLevel(LinearClamp,uv,0).g, LayerTexture.SampleLevel(LinearClamp,uv-shift,0).b);
    float radius=.012*Screen.y;
    float2 mid=BlurRadii(uv);
    float2 dir=float2(1,0);
    // The center anchors a barely defocused edge. Opposite taps enter the aperture
    // together so changing focus cannot translate a straight pole sideways.
    float4 nearSum=0,farSum=float4(center,1);
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
    float3 color=lerp(center,farSum.rgb/farSum.a,saturate(mid.x));
    color=lerp(color,nearSum.rgb/max(nearSum.a,.0001),saturate(nearSum.a*3.14159/16));
    float2 v=centered*float2(Screen.x/Screen.y,1);
    float sky=MetricDepth(uv)>199.88;
    float stars=(1-Weather.w)*(1-max(Weather.x,max(Weather.y,saturate(Weather.z/.42))))*(1-smoothstep(.05,.35,Weather.w));
    if (stars>.001 && sky) {
        float2 grid=float2(uv.x*Screen.x/Screen.y,1-uv.y)*140;
        float2 cell=floor(grid);
        float seed=Hash(cell);
        float2 starAt=.2+.6*float2(Hash(cell+3.7),Hash(cell+9.1));
        float star=step(.984,seed)*(1-smoothstep(0,.24,length(frac(grid)-starAt)))*(.55+.45*sin(Scene.y*(.8+seed*3)+seed*60));
        color+=float3(.85,.9,1)*star*stars*1.3*smoothstep(.1,.55,1-uv.y);
    }
    color*=lerp(1,1-smoothstep(.35,1.15,length(v)*1.15),.55);
    color=max(color,0);
    // Khronos PBR Neutral, as used by the source scene. The HDR10 output stage
    // applies its own display-dependent highlight shoulder above paper white.
    float black=min(color.r,min(color.g,color.b));
    color-=black<.08 ? black-6.25*black*black : .04;
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
Particle WeatherVS(uint vertex:SV_VertexID,uint id:SV_InstanceID) {
    Particle o;
    float2 q=float2(vertex==1||vertex==2||vertex==4,vertex>=2&&vertex<=4);
    float seed=Hash(float2(id,41)), phase=Hash(float2(id,27));
    o.uv=q; o.strength=1; o.glint=0;
    float2 base=(float2(Hash(float2(id,3)),Hash(float2(id,8)))*2-1)*Scene.x;
    float3 head;
    if (id>=2048) {
        float cycle=Scene.y*2*(.8+.4*seed)+phase;
        float life=frac(cycle);
        float2 center=(float2(Hash(float2(id,floor(cycle))),Hash(float2(floor(cycle),id+33)))*2-1)*(Scene.x-.05);
        float3 p=float3(center.x,GroundHeight(center)+.012,center.y);
        float size=.06+.24*life;
        o.kind=2;
        if(id>=2304) {
            uint drip=(id-2304)*2;
            float4 d=Drips[drip], timing=Drips[drip+1];
            float t=fmod(Scene.y+timing.y*timing.x,timing.x);
            float falling=sqrt(2*max(0,d.z-d.w)/9.8);
            p=float3(d.x,d.z-.5*9.8*t*t,d.y);
            if(t<falling){
                o.kind=3; size=.025;
                p.y+=(q.y-.5)*(.03+.05*t/max(falling,.001));
            } else {
                life=(t-falling)/.28;
                size=.04+.2*saturate(life);
                p.y=d.w+.014;
            }
        }
        p.xz+=(q-.5)*size;
        o.strength=(o.kind==3 ? 1 : saturate(1-life))*Weather.x;
        o.pos=mul(float4(p,1),Camera);
        return o;
    }
    bool snow=id>=1024;
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
    else if(p.kind==2)a=.32*smoothstep(.62,.86,edge)*(1-smoothstep(.86,1,edge));
    else a=.18;
    a*=p.strength;
    return float4(color*a,a);
}

cbuffer VolumeDraw : register(b2) { uint DrawSteam; };
struct VolumeVertex {
    float4 pos:SV_Position;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;
    float3 normal:TEXCOORD2;float phase:TEXCOORD3;nointerpolation uint kind:TEXCOORD4;
};
VolumeVertex VolumeVS(uint vertex:SV_VertexID,uint id:SV_InstanceID) {
    VolumeVertex o;
    o.normal=0; o.phase=0;
    if(DrawSteam==0) {
        o.kind=0;
        uint corner=vertex%3;
        float angle=(vertex/3+(corner==2?1:0))*(6.2831853/20);
        float3 pole=ConeTarget.xyz-ConeApex.xyz;
        float3 axis=normalize(pole);
        float3 tangent=normalize(cross(axis,float3(1,0,0)));
        float3 radial=tangent*cos(angle)+cross(axis,tangent)*sin(angle);
        o.world=corner==0?ConeApex.xyz:ConeTarget.xyz+radial*ConeApex.w;
        o.normal=normalize(radial-axis*ConeApex.w/length(pole));
        o.uv=float2(0,corner==0);
    } else {
        o.kind=1;
        uint corner=vertex;
        o.uv=float2(corner==1||corner==2||corner==4,corner>=2&&corner<=4);
        float4 source=Steam[id*2];
        float2 seed=Steam[id*2+1].xy;
        float life=frac(Scene.y/source.w+seed.x);
        float2 drift=Precipitation.yz*life*1.8;
        float3 center=source.xyz+float3(drift.x+sin(life*6+seed.x*20)*.08,life*1.6,drift.y);
        float3 right=normalize(mul(float4(1,0,0,0),InverseCamera).xyz);
        float3 up=normalize(mul(float4(0,1,0,0),InverseCamera).xyz);
        o.world=center+(right*(o.uv.x-.5)+up*(o.uv.y-.5))*(.25+life*.9)*seed.y;
        o.phase=seed.x*31;
        o.normal.x=smoothstep(0,.15,life)*(1-smoothstep(.55,1,life));
    }
    o.pos=mul(float4(o.world,1),Camera);
    return o;
}
float4 VolumePS(VolumeVertex i):SV_Target {
    clip(SurfaceDepth(i.pos.xy)-i.pos.z);
    if(i.kind==0) {
        float3 dir=normalize(mul(float4(0,0,1,0),InverseCamera).xyz);
        float facing=dot(normalize(i.normal),dir);
        float noise=.7+.6*Noise2(float2(i.world.x*1.3+i.world.z,i.world.y*2-Scene.y*.6));
        float light=.55*(1-smoothstep(.3,.62,Weather.w))*(1-.6*Weather.w);
        return float4(ConeColor.rgb*facing*facing*pow(saturate(i.uv.y),1.6)*noise*ConeTarget.w*light,0);
    }
    float shape=1-smoothstep(.15,.5,length(i.uv-.5));
    float puff=.6*Noise2(i.uv*3+float2(i.phase,Scene.y*.2))+.4*Noise2(i.uv*6.5-float2(Scene.y*.3,i.phase));
    return float4(lerp(HorizonMist.rgb,ToLinear(float3(221,230,242)/255),.5)*(.32*shape*smoothstep(.25,.75,puff)*i.normal.x),0);
}
