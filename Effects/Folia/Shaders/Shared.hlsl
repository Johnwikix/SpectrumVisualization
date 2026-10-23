// D3D12 binding / color-space adaptation for the Folia and Paper shader ports.
Texture2DArray<float4> AtlasTexture : register(t0);
Texture2D<float4> CoverTexture : register(t1);
Texture2D<float4> NoiseTexture : register(t2);
Texture2D<float4> BackdropTexture : register(t3);
Texture2D<float4> LayerTexture : register(t4);
// Cappella emoji tiles, or the matching glyph Gaussian coverage atlas in other modes.
Texture2D<float4> ModeAtlasTexture : register(t6);
Texture2D<float4> PreviousStageTexture : register(t8);
Texture2DArray<float> GlyphShadowTexture : register(t9);
Texture2D<float4> BackgroundHistoryTexture : register(t10);
SamplerState LinearClamp : register(s0);
SamplerState LinearWrap : register(s1);
cbuffer Frame : register(b0) {
    float4 Screen;
    float4 Time;
    float4 Parameters;
    float4 Jitter;
    float4 Palette[10];
    float4 TextureSize;
    float4 Options;
    float4 BackgroundDrift;
    float4 BackgroundColors[6];
    float4 LatentFrame;
    float4 LatentStyle;
    float4 LatentDistortion;
    float4 PostProcess;
    float4 PreviousJitter;
    float4 RealtimeMotion;
    float4 SonnetOptions;
    float4 TemperaTuning;
    float4 SceneTuning;
    float4 NomandDither;
    float4 NomandGlass;
    float4 NomandPaper;
    float4 NomandDots;
    float4 NomandLens;
    float4 BackgroundTuning;
};
void Fullscreen(uint id : SV_VertexID, out float4 p : SV_Position, out float2 uv : TEXCOORD0) {
    float2 xy = float2((id << 1) & 2, id & 2);
    p = float4(xy * 2 - 1, 0, 1);
    uv = float2(xy.x, 1 - xy.y);
}
float3 ToLinear(float3 c) {
    return lerp(c / 12.92, pow(max((c + .055) / 1.055, 0), 2.4), step(.04045, c));
}
float3 ToSrgb(float3 c) {
    return lerp(c * 12.92, 1.055 * pow(max(c, 0), 1.0 / 2.4) - .055, step(.0031308, c));
}
float2 Rot(float2 p, float a) { float c = cos(a), s = sin(a); return float2(c*p.x-s*p.y,s*p.x+c*p.y); }
float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453123); }
float2 ImageUV(float2 uv, float scale) {
    float ratio = max(TextureSize.z, 1) / max(TextureSize.w, 1);
    float width = max(Screen.x / ratio, Screen.y) * ratio;
    return (uv - .5) * Screen.xy / float2(width, width / ratio) / scale + .5;
}
float3 SoftLight(float3 base, float3 blend) {
    float3 d=lerp(((16*base-12)*base+4)*base,sqrt(max(base,0)),step(.25,base));
    return lerp(base-(1-2*blend)*base*(1-base),base+(2*blend-1)*(d-base),step(.5,blend));
}

// GLSL's scalar vector constructors and modulo semantics differ from HLSL.
float2 vec2(float a) { return a.xx; }
float2 vec2(float2 a) { return a; }
float2 vec2(float a,float b) { return float2(a,b); }
float3 vec3(float a) { return a.xxx; }
float3 vec3(float3 a) { return a; }
float3 vec3(float a,float b,float c) { return float3(a,b,c); }
float3 vec3(float2 a,float b) { return float3(a,b); }
float3 vec3(float a,float2 b) { return float3(a,b); }
float4 vec4(float a) { return a.xxxx; }
float4 vec4(float4 a) { return a; }
float4 vec4(float a,float b,float c,float d) { return float4(a,b,c,d); }
float4 vec4(float3 a,float b) { return float4(a,b); }
float4 vec4(float a,float3 b) { return float4(a,b); }
float4 vec4(float2 a,float2 b) { return float4(a,b); }
float4 vec4(float2 a,float b,float c) { return float4(a,b,c); }
float2x2 makeMat2(float a,float b,float c,float d) { return float2x2(a,c,b,d); }
#define fract frac
#define mix lerp
#define dFdx ddx
#define dFdy ddy
#define mod(x,y) ((x)-(y)*floor((x)/(y)))
