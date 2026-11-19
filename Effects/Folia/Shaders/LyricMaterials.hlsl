// D3D12 adaptation of the Hanabi, Metaphor and Nebbia film layers in folia-major (AGPL-3.0).
float NoiseCell(float2 cell) {
    float3 value=frac(float3(cell.xyx)*.1031);
    value+=dot(value,value.yzx+31.32);
    return frac((value.x+value.y)*value.z);
}

float4 HanabiSkyPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    uv-=Jitter.xy/Screen.xy;
    float2 aspect=float2(Screen.x/Screen.y,1);
    float2 centered=(uv-float2(.51,.49))*aspect;
    float3 lower=lerp(float3(.105,.048,.17),Palette[3].rgb*.26,.48);
    float3 color=lerp(float3(.035,.029,.099),lower,saturate(uv.y));
    float t=Time.y;
    float2 grid=(uv+float2(0,t*.012))*float2(92,92/aspect.x);
    float starSeed=NoiseCell(floor(grid));
    float2 local=frac(grid)-.5;
    float star=exp(-dot(local,local)*85)*smoothstep(.88,1,starSeed);
    float band=exp(-abs(centered.y+sin(centered.x*2.8+t*.1)*.075)*8-abs(centered.x)*2.6);
    color+=lerp(float3(.38,.27,.58),Palette[3].rgb,.4)*(band*.22+star*.69);
    return float4(color,1);
}

float4 InkDevelopPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float2 stable=pixel.xy-Jitter.xy;
    float steps=floor(Time.y*8)*.125;
    float2 flow=float2(sin(uv.y*26+steps*.46),sin(uv.x*32-steps*.38));
    float3 field=LayerTexture.SampleLevel(LinearClamp,uv+flow*1.2/Screen.xy,0).rgb;
    float grain=NoiseCell(floor(stable));
    float2 screenDots=Rot(stable,.42)/4.3;
    float face=length(frac(screenDots)-.5);
    float hard=smoothstep(.48,.70,field.r+(grain-.5)*.13);
    float radius=clamp(.04+field.g*.66,.03,.62);
    float dotCoverage=(1-smoothstep(radius-.095,radius+.095,face))*smoothstep(.04,.26,field.g);
    float mark=hard+(1-hard)*dotCoverage*.82;
    float3 paper=float3(.91,.858,.765), ink=float3(.071,.064,.073);
    if (NativeLyric.w>.5) {
        paper=float3(.087,.083,.104);
        ink=float3(.92,.87,.78);
    }
    float3 accent=lerp(Palette[3].rgb,float3(.62,.15,.21),.4);
    if (NativeLyric.w>.5) accent=lerp(accent,float3(1,.69,.52),.55);
    float tint=saturate(field.b/max(field.r,.075)*1.22);
    float3 color=lerp(paper,lerp(ink,accent,tint),mark);
    float fiber=NoiseCell(floor(stable/float2(4,1)));
    return float4(max(0,color+(grain-.5)*.058+(fiber-.5)*.016),1);
}

float3 NebbiaCity(float2 uv,float phase) {
    float horizon=.70;
    float3 color=lerp(float3(.029,.055,.107),float3(.18,.22,.36),saturate(uv.y/horizon));
    float roadStep=saturate((uv.y-horizon)/.30);
    color=lerp(color,float3(.05,.072,.12),roadStep);
    [unroll] for (int layer=0;layer<2;layer++) {
        float x=uv.x*(8+layer*8)+phase*(.35+layer*.41)+3.57*layer;
        float cell=floor(x), local=frac(x);
        float seed=NoiseCell(float2(cell,layer+7));
        float roof=horizon-.095-seed*(.29+layer*.14);
        float block=step(roof,uv.y)*step(uv.y,horizon)*step(.065,local)*step(local,.93);
        float3 face=lerp(float3(.061,.081,.154),float3(.11,.13,.21),seed);
        float2 tiles=float2(local*3,(uv.y-roof)*51);
        float2 aperture=abs(frac(tiles)-.5);
        float window=(1-smoothstep(.21,.35,max(aperture.x,aperture.y)))*
            step(.50,NoiseCell(float2(cell*3+floor(tiles.x),floor(tiles.y)+layer*20)));
        float3 neon=lerp(float3(.46,.74,1),float3(1,.47,.24),seed);
        face+=window*neon*(.34+layer*.14);
        color=lerp(color,face,block);
    }
    float lights=uv.x*5+phase*.54;
    float2 lamp=float2(frac(lights)-.5,(uv.y-.442)*4);
    float glow=exp(-dot(lamp,lamp)*20)*.57;
    float reflection=exp(-abs(lamp.x)*(22-15*roadStep))*roadStep*(.32+.16*sin(uv.y*110+phase*5));
    color+=float3(1,.57,.27)*(glow+max(0,reflection));
    float street=exp(-abs(uv.x-.45+sin(phase*.4)*.29)*48)*exp(-abs(uv.y-.72)*57);
    return color+float3(.40,.67,1)*street*.74;
}

float4 NebbiaPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float t=Time.y;
    float2 stable=uv-Jitter.xy/Screen.xy;
    float shake=sin(t*23)*NativeLyric.z*.006;
    float2 outside=float2(stable.x,stable.y+shake);
    float mask=BackgroundHistoryTexture.SampleLevel(LinearClamp,uv,0).r;
    float2 offset=float2(1.5,1.5)/Screen.xy;
    float around=BackgroundHistoryTexture.SampleLevel(LinearClamp,uv+offset,0).r+
        BackgroundHistoryTexture.SampleLevel(LinearClamp,uv-offset,0).r;
    float seam=abs(mask-around*.5);
    float2 grid=stable*float2(41,25);
    float seed=NoiseCell(floor(grid)+float2(17,8));
    float2 drop=frac(grid)-float2(.15+seed*.7,frac(seed*9+t*(.12+seed*.38)));
    drop.x*=2.4;
    float bead=(1-smoothstep(.10,.21,length(drop)))*smoothstep(.62,.91,seed);
    float trail=(1-smoothstep(.024,.067,abs(drop.x)))*smoothstep(-.48,-.12,drop.y)*step(drop.y,-.061)*.32*seed;
    outside+=drop*(bead+trail)*.021;
    float phase=t*.17;
    float3 clear=NebbiaCity(outside,phase);
    float3 fog=(NebbiaCity(outside+float2(.018,.018),phase)+NebbiaCity(outside-float2(.018,.018),phase))*.5;
    float2 aim=(stable-NativeComposition.xy)*float2(Screen.x/Screen.y,1);
    float light=exp(-dot(aim,aim)*9);
    float dust=NoiseCell(floor(pixel.xy-Jitter.xy))-.5;
    fog=lerp(fog,float3(.51,.56,.67),.54+light*.19);
    float3 color=lerp(fog,clear,saturate(mask*.94+bead*.38+trail*.20));
    color+=float3(.58,.70,.89)*(seam*.72+bead*.21+trail*.09+NativeLyric.y*bead*.3);
    float2 frame=abs(stable-.5);
    float edge=smoothstep(.44,.50,frame.x)+smoothstep(.41,.51,frame.y);
    color=lerp(color,float3(.042,.052,.088),saturate(edge*.77))+dust*.033;
    return float4(max(0,color),1);
}
