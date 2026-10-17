// Copyright (c) 2026 chthollyphile; AGPL-3.0. Original Sora GL_POINTS math on small instanced quads.
struct StarVertex {float4 position:SV_Position;float2 uv:TEXCOORD0;nointerpolation float intensity:TEXCOORD1;nointerpolation float colorType:TEXCOORD2;};
StarVertex VS(uint vertex:SV_VertexID,uint index:SV_InstanceID) {
 float seed=index*123.456;
 float speedRand=Hash(float2(seed,1.1)),sizeRand=Hash(float2(seed,2.2));
 float yRand=Hash(float2(seed,3.3)),xRand=Hash(float2(seed,4.4));
 float blinkRand=Hash(float2(seed,5.5)),colorRand=Hash(float2(seed,6.6));
 float aspect=Screen.x/Screen.y,time=Time.x;
 float speed=.008+speedRand*.024,size=.0006+.0032*pow(sizeRand,3.8);
 float x=frac(xRand+time*speed)*(aspect+.1)-(aspect*.5+.05);
 float y=-.45+yRand*.9+sin(time*.3+yRand*6.283)*.005;
 float pointSize=max(2*Options.z*Screen.x/Screen.z,size*Screen.y*3.5);
 float2 quad=float2((vertex==1||vertex==2||vertex==4)?1:0,(vertex>=2&&vertex<=4)?1:0);
 float2 center=float2(x/(aspect*.5),y/.5);
 StarVertex v;v.position=float4(center+(quad-.5)*pointSize/Screen.xy*float2(2,-2),0,1);v.uv=quad;
 v.intensity=.3+.7*sin(time*(1+blinkRand*2.5)+xRand*6.283);v.colorType=colorRand;return v;
}
float4 PS(StarVertex v):SV_Target {
 float alpha=smoothstep(.5,.1,length(v.uv-.5))*v.intensity;if(alpha<.01)discard;
 float3 color=v.colorType>.90?Palette[3].rgb:Palette[1].rgb;return float4(color*alpha,alpha);
}
