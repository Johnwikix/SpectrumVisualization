// Filters the transparent paragraph surface in viewport coordinates, then alpha-composites it.
// The common background and the fixed viewport frame never pass through this chain.
float4 Scene(float2 uv) {
    if(any(uv<0)||any(uv>1))return 0;
    return LayerTexture.SampleLevel(LinearClamp,uv,0);
}
float4 PS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float2 sampleUV=uv;
    float2 stableUV=uv-Jitter.xy/Screen.xy;
    float2 centered=(stableUV-.5)*float2(Screen.x/Screen.y,1);
    if(SonnetOptions.x>.5) {
        float r=dot(centered,centered);
        float radial=1-.3*.32*r+.3*.32*.16*r*r;
        sampleUV=.5+centered*radial/float2(Screen.x/Screen.y,1)+Jitter.xy/Screen.xy;
    }
    float strip=floor((uv.y*Screen.y-Jitter.y)/12);
    float tear=PostProcess.y;
    float random=Hash(float2(strip,floor(PostProcess.w*24)));
    sampleUV.x+=(random-.5)*tear*.075*step(.64,random);
    float4 c=Scene(sampleUV);
    if(PostProcess.x>.001) {
        float2 stepUV=PostProcess.x/Screen.xy;
        c*=.2;
        c+=(Scene(sampleUV+float2(stepUV.x,0))+Scene(sampleUV-float2(stepUV.x,0))
           +Scene(sampleUV+float2(0,stepUV.y))+Scene(sampleUV-float2(0,stepUV.y)))*.12;
        c+=(Scene(sampleUV+stepUV)+Scene(sampleUV-stepUV)
           +Scene(sampleUV+float2(stepUV.x,-stepUV.y))+Scene(sampleUV+float2(-stepUV.x,stepUV.y)))*.08;
    }
    if(SonnetOptions.x>.5) {
        // Original opt-in defaults: subtle grain, lens dispersion and vignette; contrast/print shifts are neutral.
        float2 dispersion=centered*.6*.0015;
        c.r=lerp(c.r,Scene(sampleUV+dispersion).r,.6);
        c.b=lerp(c.b,Scene(sampleUV-dispersion).b,.6);
        float noise=Hash((p.xy-Jitter.xy)+floor(Time.y*24))-.5;
        c.rgb=max(0,c.rgb+noise*.07*c.a);
        c.rgb*=1-saturate(smoothstep(.52,1.08,length(centered))*.85*.6);
    }
    return c*saturate(PostProcess.z);
}
