// Copyright (c) 2026 chthollyphile; AGPL-3.0. Folia dual-filter bloom, D3D12 adaptation.
Texture2D<float4> Source:register(t0);
Texture2D<float4> Add:register(t1);
Texture2D<float4> Backdrop:register(t2);
SamplerState LinearClamp:register(s0);
cbuffer Filter:register(b0) { float4 Pixel; float4 Options; };
struct Vertex {float4 position:SV_Position;float2 uv:TEXCOORD0;};
Vertex VS(uint id:SV_VertexID) { Vertex v;v.uv=float2((id<<1)&2,id&2);v.position=float4(v.uv*float2(2,-2)+float2(-1,1),0,1);return v;}
float4 Tap(float2 uv) {return Source.SampleLevel(LinearClamp,clamp(uv,Pixel.xy*.5,1-Pixel.xy*.5),0);}
float4 Down(Vertex v):SV_Target {
 float2 uv=v.uv,hp=Pixel.xy;
 float4 c=(Tap(uv)*4+Tap(uv-hp)+Tap(uv+hp)+Tap(uv+float2(hp.x,-hp.y))+Tap(uv-float2(hp.x,-hp.y)))/8;
 if(Options.x>.5){float br=max(c.r,max(c.g,c.b)),knee=max(Pixel.w,.0001);float soft=clamp(br-Pixel.z+knee,0,2*knee);soft=soft*soft/(4*knee);float contrib=max(soft,br-Pixel.z)/max(br,.0001);c*=saturate(contrib);}
 return c;
}
float4 Up(Vertex v):SV_Target {
 float2 uv=v.uv,hp=Pixel.xy;
 float4 sum=Tap(uv+float2(-hp.x*2,0))+Tap(uv+float2(-hp.x,hp.y))*2+Tap(uv+float2(0,hp.y*2))+Tap(uv+hp)*2;
 sum+=Tap(uv+float2(hp.x*2,0))+Tap(uv+float2(hp.x,-hp.y))*2+Tap(uv+float2(0,-hp.y*2))+Tap(uv-hp)*2;
 return sum/12+Add.SampleLevel(LinearClamp,uv,0)*Options.y;
}
float4 Combine(Vertex v):SV_Target {
 float4 base=Source.SampleLevel(LinearClamp,v.uv,0),glow=Add.SampleLevel(LinearClamp,v.uv,0)*Options.z;
 if(Options.x>.5) glow.rgb*=float3(1,.95,.98);
 float3 rgb=base.rgb+glow.rgb*(1-base.a*.5);
 float peak=max(rgb.r,max(rgb.g,rgb.b));if(peak>1)rgb=lerp(rgb/peak,1,.2*(1-1/peak));
 float a=base.a+max(glow.r,max(glow.g,glow.b))*(1-base.a);a=min(max(a,max(rgb.r,max(rgb.g,rgb.b))),1);
 return float4(rgb+Backdrop.SampleLevel(LinearClamp,v.uv,0).rgb*Options.w*(1-a),1);
}

// Machi keeps HDR radiance, including overbright signage and the horizontals of the anamorphic streak.
float4 CombineLinear(Vertex v):SV_Target {
 float3 base=Source.SampleLevel(LinearClamp,v.uv,0).rgb;
 float3 bloom=Add.SampleLevel(LinearClamp,v.uv,0).rgb;
 float3 streak=0;
 if(Options.w>.005) {
  [unroll]for(int i=1;i<=4;i++){
   float2 offset=float2(Pixel.x*float(i*i)*2,0);
   float weight=(5-i)*.05;
   streak+=weight*(Add.SampleLevel(LinearClamp,v.uv-offset,0).rgb+Add.SampleLevel(LinearClamp,v.uv+offset,0).rgb);
  }
 }
 return float4(base+bloom*Options.z+streak*float3(.62,.74,1)*Options.w,1);
}
