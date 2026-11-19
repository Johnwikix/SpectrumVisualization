// Native rasterized Machi convenience-store scene. The panel is a texture on the actual 3D mesh.
struct MachiVertex { float4 positionMaterial,color,normalUvX,uvY; };
StructuredBuffer<MachiVertex> MachiVertices : register(t7);
cbuffer MachiFrame : register(b1) { row_major float4x4 ViewProjection; row_major float4x4 PreviousViewProjection; };

struct MachiOutput {
    float4 position:SV_Position;
    float4 previous:TEXCOORD0;
    float3 world:TEXCOORD1;
    float2 uv:TEXCOORD2;
    nointerpolation float3 color:COLOR0;
    nointerpolation float3 normal:NORMAL0;
    nointerpolation uint material:TEXCOORD3;
};

MachiOutput MachiVS(uint id:SV_VertexID) {
    MachiVertex v=MachiVertices[id];
    float4 worldPosition=float4(v.positionMaterial.xyz,1);
    MachiOutput o;
    o.position=mul(worldPosition,ViewProjection);
    o.previous=mul(worldPosition,PreviousViewProjection);
    o.position.xy+=Jitter.xy/Screen.xy*float2(2,-2)*o.position.w;
    o.world=worldPosition.xyz;
    o.color=v.color.rgb;
    o.normal=v.normalUvX.xyz;
    o.uv=float2(v.normalUvX.w,v.uvY.x);
    o.material=(uint)v.positionMaterial.w;
    return o;
}

struct MachiTargets { float4 color:SV_Target0; float4 geometry:SV_Target1; };
MachiTargets MachiPS(MachiOutput input) {
    float3 normal=input.normal;
    float3 color=input.color*(.52+.46*saturate(dot(normal,normalize(float3(.28,.79,.47)))));
    float reactive=0;
    if (input.material==2) {
        float2 cellCount=float2(400,126);
        float2 cell=input.uv*cellCount;
        float2 cellSize=max(fwidth(cell),.0001);
        float2 centered=(floor(cell)+.5)/cellCount;
        float dotPattern=smoothstep(.5,.29,length(frac(cell)-.5));
        float pattern=saturate(1-max(cellSize.x,cellSize.y)*1.6);
        float2 sampleAt=lerp(input.uv,centered,pattern);
        float3 text=BackgroundHistoryTexture.SampleLevel(LinearClamp,sampleAt,0).rgb;
        float strip=smoothstep(.760,.780,input.uv.y);
        float separator=1-smoothstep(.003,.013,abs(input.uv.y-.745));
        color=lerp(float3(.033,.075,.124),float3(.056,.063,.111),strip);
        color+=separator*float3(.035,.22,.27);
        color+=text*lerp(1,.51+dotPattern*.49,pattern)*(1.16+NativeLyric.y*.15);
        color*=.96+.04*cos(input.uv.y*30-Time.y*2.3);
        reactive=.64;
    } else if (input.material==1) {
        float shelves=1-smoothstep(.075,.115,abs(frac(input.uv.y*5+.15)-.5));
        float side=saturate(input.uv.y*.54+.44);
        color=input.color*side*.86;
        color+=.18*shelves*float3(.65,.49,.25);
        color=lerp(color,float3(.27,.49,.57),smoothstep(.39,.56,input.uv.y+input.uv.x*.35)*.25);
    } else if (input.material==3) {
        color=input.color*(.91+NativeLyric.y*.22);
        reactive=NativeLyric.y*.55;
    } else if (input.material==4 && normal.y>.5) {
        float light=exp(-dot(input.world.xz-float2(-2,1),input.world.xz-float2(-2,1))*.15);
        float street=exp(-dot(input.world.xz-float2(5,-1),input.world.xz-float2(5,-1))*.28);
        color+=float3(.47,.33,.17)*(light*.37+street*.43);
    }
    float2 previous=(input.previous.xy/max(input.previous.w,.0001)*float2(.5,-.5)+.5)*Screen.xy;
    float2 current=input.position.xy-Jitter.xy;
    MachiTargets result;
    result.color=float4(color,1);
    // Store distance from the far plane so the FP16 mantissa preserves small depth differences.
    result.geometry=float4(previous-current,1-input.position.z,reactive);
    return result;
}

float4 MachiSkyPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    uv-=Jitter.xy/Screen.xy;
    float2 centered=(uv-float2(.58,.54))*float2(Screen.x/Screen.y,1);
    float halo=exp(-dot(centered,centered)*4.5);
    float3 color=lerp(float3(.026,.033,.078),float3(.103,.143,.224),halo);
    float2 field=uv*float2(121,73);
    float star=smoothstep(.982,1,Hash(floor(field)))*exp(-dot(frac(field)-.5,frac(field)-.5)*75);
    return float4(color+star*.12*(1-uv.y),1);
}
