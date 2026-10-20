// Folia Tempera's default lens -> noise -> vignette chain, composed before linear output conversion.
float4 SampleScene(float2 uv) {
    if(any(uv<0)||any(uv>1))return 0;
    return BackdropTexture.SampleLevel(LinearClamp,uv+Jitter.xy/Screen.xy,0);
}
float4 PS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    // Evaluate the lens in scene coordinates, then sample its already jittered source.
    uv-=Jitter.xy/Screen.xy;p.xy-=Jitter.xy;
    float aspect=Screen.x/Screen.y;
    float2 centered=uv-.5;centered.x*=aspect;
    float radiusSquared=dot(centered,centered),curvature=SceneTuning.x*.32;
    float radialScale=1-curvature*radiusSquared+curvature*.16*radiusSquared*radiusSquared;
    float2 lens=centered*radialScale;lens.x/=aspect;lens+=.5;
    float4 c=SampleScene(lens);
    float2 shift=float2(TemperaTuning.z*.0035,0);
    c.r=SampleScene(lens+shift).r;
    c.b=SampleScene(lens-shift).b;
    c.rgb=(c.rgb-.5*c.a)*(1+TemperaTuning.y*.7)+.5*c.a;
    float2 noiseCoordinate=float2(p.x,Screen.y-p.y)*PostProcess.x;
    float noise=frac(sin(dot(noiseCoordinate,float2(12.9898,78.233)))*43758.5453)-.5;
    c.rgb+=noise*TemperaTuning.x*.35*c.a;
    float vignette=saturate(smoothstep(.52,1.08,length(centered))*TemperaTuning.w*.6);
    c=lerp(c,float4(0,0,0,1),vignette);
    return float4(c.rgb+Palette[0].rgb*(1-c.a),1);
}
