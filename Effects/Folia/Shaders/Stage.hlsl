// Folia's cover blur / drift and Sora point field, composed in perceptual space.
float4 PS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    uv-=Jitter.xy/Screen.xy;
    float3 color=Palette[0].rgb;
    if(Parameters.x<.5 || abs(Parameters.x-2)<.5) {
        bool monet=Parameters.x>1;
        float2 bakedUV=uv;
        float2 image;
        float2 blurStep;
        if(monet) {
            float2 driftUV=(uv-.5-BackgroundDrift.xy)/max(1,BackgroundDrift.z)+.5;
            float2 bakedSize=float2(1920,1080);
            bakedUV=(driftUV-.5)*Screen.xy/(max(Screen.x/bakedSize.x,Screen.y/bakedSize.y)*bakedSize)+.5;
            float ratio=max(TextureSize.z,1)/max(TextureSize.w,1);
            float coverWidth=max(1920/ratio,1080)*ratio;
            float2 coverSize=float2(coverWidth,coverWidth/ratio);
            image=(bakedUV-.5)*bakedSize/coverSize+.5;
            blurStep=3.0/coverSize;
        } else {
            image=ImageUV(uv,1.5);
            blurStep=20.0/Screen.xy;
        }
        if(TextureSize.z>1 && (Parameters.x>1 || Options.x>.5)) {
            color=0; float weights=0;
            [unroll]for(int y=-4;y<=4;y++) [unroll]for(int x=-4;x<=4;x++) {
                float weight=exp(-(x*x+y*y)/8.0); weights+=weight;
                color+=CoverTexture.SampleLevel(LinearClamp,image+float2(x,y)*blurStep,0).rgb*weight;
            }
            color/=weights;
        }
        if(Parameters.x>1) {
            float luminance=dot(color,float3(.2126,.7152,.0722));
            float originalLuminance=luminance;
            color=luminance+(color-luminance)*1.05;
            float3 wash=originalLuminance<=.55 ? lerp(Palette[0].rgb,Palette[3].rgb,originalLuminance/.55) : lerp(Palette[3].rgb,Palette[1].rgb,(originalLuminance-.55)/.45);
            color=lerp(color,wash,.34);
            float2 bake=float2(1920,1080);
            float t=saturate(dot(bakedUV,bake*bake)/dot(bake,bake));
            float4 a=float4(Palette[3].rgb*.5388,.5388),b=float4(Palette[0].rgb*.5892,.5892),c=float4(Palette[1].rgb*.3264,.3264);
            float4 overlay=t<.44 ? lerp(a,b,t/.44) : lerp(b,c,(t-.44)/.56);
            color=color*(1-overlay.a)+overlay.rgb;
            float bloom=saturate(1-length((bakedUV-float2(.18,.34))*float2(1,1080.0/1920))/.55)*.3108;
            color=lerp(color,Palette[3].rgb,bloom);
            color=lerp(color,Palette[0].rgb,saturate((bakedUV.x-.45)/.55)*.4612);
            [unroll]for(int streak=0;streak<18;streak++) {
                float2 start=float2((streak*127)%1920,(streak*211)%1080-40);
                float2 q=bakedUV*bake-start;
                float2 footprint=max(fwidth(bakedUV*bake),.001);
                float streakCoverage=saturate((.5-abs(q.x-.5))/footprint.x+.5)*saturate((min(q.y,1080*.28-q.y))/footprint.y+.5);
                color=lerp(color,Palette[0].rgb,streakCoverage*.1592);
            }
            float readability=uv.x<.34 ? lerp(.18,.32,uv.x/.34) : uv.x<.7 ? lerp(.32,.66,(uv.x-.34)/.36) : lerp(.66,.82,(uv.x-.7)/.3);
            color=lerp(color,Palette[0].rgb,readability);

        }

        if(Parameters.x<.5 && Options.x>.5) {
            color=lerp(color,Palette[0].rgb,.8);
            float t=saturate(dot(uv,Screen.xy*Screen.xy)/dot(Screen.xy,Screen.xy));
            float3 grad=t<.5?Palette[1].rgb:Palette[2].rgb;
            float3 overlay=lerp(2*color*grad,1-2*(1-color)*(1-grad),step(.5,color));
            color=lerp(color,overlay,.4*abs(t*2-1));
        }

    }
    if(Parameters.x>3.5) color=0; // Sora uses 150 small instanced quads, as in the GL_POINTS original.
    return float4(max(color,0),1);
}

float4 LatentCompositePS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    uv-=Jitter.xy/Screen.xy;
    float3 mesh=LayerTexture.SampleLevel(LinearClamp,(uv-.5)/LatentStyle.x+.5,0).rgb;
    float lum=dot(mesh,float3(.213,.715,.072));mesh=(lum+(mesh-lum)*LatentStyle.z)*LatentStyle.w;
    float4 dither=BackdropTexture.SampleLevel(LinearClamp,(uv-.5)/LatentStyle.y+.5,0);
    float3 color=lerp(mesh,SoftLight(mesh,dither.rgb),LatentFrame.w*dither.a);
    return float4(max(lerp(color,Palette[0].rgb,.35),0),1);
}

float4 VignettePS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    uv-=Jitter.xy/Screen.xy;
    float radius=length((uv-.5)*Screen.xy)/(length(Screen.xy)*.5);
    return float4(0,0,0,saturate((radius-.4)/.6)*.6);
}
float Cross2(float2 a,float2 b){return a.x*b.y-a.y*b.x;}
float InversionLuminance(float2 uv){return dot(BackdropTexture.SampleLevel(LinearClamp,uv,0).rgb,float3(.2126,.7152,.0722));}
float4 CopyPS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    return float4(ToLinear(max(BackdropTexture.SampleLevel(LinearClamp,uv,0).rgb,0)),1);
}
struct TemporalOutput { float4 color:SV_Target0;float2 motion:SV_Target1;float reactive:SV_Target2;float depth:SV_Depth; };
TemporalOutput TemporalPS(float4 p:SV_Position,float2 uv:TEXCOORD0) {
    TemporalOutput o; o.color=CopyPS(p,uv);o.motion=0;
    // The stage is a planar emissive surface. Reject changing pixels, while static text retains temporal history.
    float2 sampleUV=uv;
    float3 current=BackdropTexture.SampleLevel(LinearClamp,sampleUV,0).rgb;
    if(Jitter.z>.5) o.reactive=1;
    else {
        float2 previousUV=sampleUV+(PreviousJitter.xy-Jitter.xy)/Screen.xy;
        float3 previous=PreviousStageTexture.SampleLevel(LinearClamp,previousUV,0).rgb;
        float3 low=previous,high=previous;
        // A subpixel raster sample can change edge coverage on a static glyph. Compare
        // with its previous local color envelope before treating that change as animation.
        [unroll]for(int axis=0;axis<2;axis++) [unroll]for(int side=-1;side<=1;side+=2) {
            float2 offset=axis==0?float2(side,0):float2(0,side);
            float3 neighbor=PreviousStageTexture.SampleLevel(LinearClamp,previousUV+offset/Screen.xy,0).rgb;
            low=min(low,neighbor);high=max(high,neighbor);
        }
        float3 delta=abs(current-clamp(current,low,high));
        float difference=max(delta.r,max(delta.g,delta.b));
        float signal=max(max(current.r,max(current.g,current.b)),max(previous.r,max(previous.g,previous.b)));
        o.reactive=saturate(difference/max(signal,.1)*2);
    }
     o.depth=(100.0/(100-.1))-(100*.1/(100-.1))/5; return o;
}

struct Instance { float4 rect,uv,color,transform,shape,effect,glyphData; };
StructuredBuffer<Instance> Instances:register(t5);
Instance ReadInstance(uint id) {
    Instance v=Instances[id];
    // Layout stays in DIPs; physical pixel scaling is parallel GPU work.
    if(Options.z!=1) {
        v.rect*=Options.z;
        if((int)v.shape.x==25) v.transform*=Options.z;
        else { v.transform.z*=Options.z; v.shape.yz*=Options.z; }
    }
    return v;
}
cbuffer Draw:register(b2) { uint InstanceOffset; };
struct SpriteOutput { float4 position:SV_Position;float2 uv:TEXCOORD0;nointerpolation uint instance:TEXCOORD1;float2 pixel:TEXCOORD2; };
SpriteOutput SpriteVS(uint vertex:SV_VertexID,uint id:SV_InstanceID) {
    id+=InstanceOffset;
    Instance v=ReadInstance(id);
    float2 quad=float2((vertex==1||vertex==2||vertex==4)?1:0,(vertex>=2&&vertex<=4)?1:0);
    if((int)v.shape.x==25) {
        uint corner=vertex==0||vertex==5?0:vertex==1?1:vertex==2||vertex==4?2:3;
        float2 xy=corner==0?v.rect.xy:corner==1?v.rect.zw:corner==2?v.transform.xy:v.transform.zw;
        float clipW=corner==0?v.shape.y:corner==1?v.shape.z:corner==2?v.shape.w:v.effect.x;
        SpriteOutput o;o.position=float4((xy/Screen.zw*float2(2,-2)+float2(-1,1)+Jitter.xy/Screen.xy*float2(2,-2))*clipW,0,clipW);
        o.uv=quad;o.instance=id;o.pixel=xy/Screen.zw;return o;
    }
    float2 local=(quad-.5)*v.rect.zw;
    local.x+=local.y*v.transform.y;
    float2 xy=Rot(local,v.transform.x)+v.rect.xy+v.rect.zw*.5;
    SpriteOutput o;o.position=float4(xy/Screen.zw*float2(2,-2)+float2(-1,1)+Jitter.xy/Screen.xy*float2(2,-2),0,1);
    o.uv=quad;o.instance=id;o.pixel=xy/Screen.zw;return o;
}
float Median3(float3 v) { return max(min(v.x,v.y),min(max(v.x,v.y),v.z)); }
float GlyphPixelRange(float2 atlasUV) {
    // Derivatives use the actual raster size, rotation and shear. RGB preserves sharp corners;
    // alpha is the true outline distance used for blur / stroke rather than a magnified mask.
    float2 dx=ddx(atlasUV)*TextureSize.xy,dy=ddy(atlasUV)*TextureSize.xy;
    return Options.y/max(max(length(dx),length(dy)),.0001);
}
float4 SpritePS(SpriteOutput input):SV_Target {
    Instance v=ReadInstance(input.instance);float2 uv=input.uv;
    float alpha=v.color.a;float3 color=v.color.rgb;
    float2 local=(uv-.5)*v.rect.zw;
    float edge=max(fwidth(local.x),fwidth(local.y));
    int kind=(int)v.shape.x;
    if(kind==0||kind==25) {
        float2 atlasUV=v.uv.xy+uv*v.uv.zw;
        // Clamp to this glyph's texel centers: linear filtering must never mix differently colored MSDF channels from a neighbor.
        float2 halfTexel=.5/TextureSize.xy;
        float2 glyphUV=clamp(atlasUV,v.uv.xy+halfTexel,v.uv.xy+v.uv.zw-halfTexel);
        float4 distances=AtlasTexture.SampleLevel(LinearClamp,float3(glyphUV,v.glyphData.x),0);
        float range=GlyphPixelRange(atlasUV);
        float signedDistance=(Median3(distances.rgb)-.5)*range;
        float trueDistance=(distances.a-.5)*range;
        float blur=kind==25?0:v.transform.z*Screen.x/Screen.z;
        float coverage=saturate(signedDistance+.5);
        if(blur>.01) coverage=saturate(.5+trueDistance/max(1,blur*2.5));
        if(v.effect.z>0) coverage=max(saturate(v.effect.z*Screen.x/Screen.z+.5-abs(trueDistance)),coverage*.14);
        float glowStrength=kind==25?v.effect.w:v.transform.w;
        float halo=glowStrength>0 ? GlyphShadowTexture.SampleLevel(LinearClamp,float3(glyphUV,v.glyphData.x),0) : 0;
        float2 backdropUV=input.pixel+Jitter.xy/Screen.xy;
        if(kind==0&&v.effect.w>0) { float3 base=BackdropTexture.SampleLevel(LinearClamp,backdropUV,0).rgb;color=abs(base-v.color.rgb); }
        if(kind==0&&v.effect.w>1.5){
            float2 d=1.5/Screen.xy;float lum=InversionLuminance(backdropUV)*.4;
            lum+=(InversionLuminance(backdropUV+d)+InversionLuminance(backdropUV-d)+InversionLuminance(backdropUV+float2(d.x,-d.y))+InversionLuminance(backdropUV+float2(-d.x,d.y)))*.15;
            float ink=dot(Palette[4].rgb,float3(.2126,.7152,.0722)),paper=dot(Palette[5].rgb,float3(.2126,.7152,.0722));
            color=lerp(Palette[4].rgb,Palette[5].rgb,step(abs(lum-ink)+.015,abs(lum-paper)));
        }
        float reveal=v.effect.y>0 ? 1-smoothstep(v.effect.x-v.effect.y,v.effect.x+v.effect.y,uv.x) : step(uv.x,v.effect.x);
        float opacity=alpha*(kind==25?1:reveal);
        if(kind==0&&v.shape.w>.5) {
            float railY=(input.pixel.y*Screen.w-v.shape.y)/max(v.shape.z-v.shape.y,.001);
            float mask=saturate(railY/.11)*saturate((1-railY)/.12);
            opacity*=mask;
        }
        float glow=halo*.8*glowStrength*(1-coverage);
        return float4(color*(coverage+glow)*opacity,saturate(coverage+glow)*opacity);
    } else if(kind==2||kind==20||kind==21) {
        float angle=atan2(local.y,local.x),r=min(v.rect.z,v.rect.w)*.5;
        float radius=r;
        if(kind==20) radius=r*(.88+.12*step(.25,frac(angle/6.2831853*v.shape.w)));
        float distance=abs(length(local)-radius+v.shape.y*.5)-v.shape.y*.5;
        if(kind==21) distance=max(abs(length(local)-r*.96)-r*.035,abs(sin(angle*v.shape.w*.5))*r*.02-1);
        alpha*=1-smoothstep(-edge,edge,distance);
    } else if(kind==3||kind==24) {
        float radius=v.shape.z;
        if(kind==24 && local.y>0 && (v.shape.w>.5?local.x>0:local.x<0)) radius=6*radius/24;
        float2 q=abs(local)-v.rect.zw*.5+radius;
        float distance=length(max(q,0))+min(max(q.x,q.y),0)-radius;
        alpha*=1-smoothstep(-edge,edge,distance);
    } else if(kind==4) alpha*=exp(-dot(uv-.5,uv-.5)*18);
    else if(kind==17) {
        float r=length(uv-.5)*2;
        alpha*=r<=.18?lerp(1,.72,r/.18):r<=.45?lerp(.72,.2,(r-.18)/.27):lerp(.2,0,saturate((r-.45)/.55));
    } else if(kind==18) {
        float2 q=uv-.5;float r=length(q)*4;
        float halo=r<=.12?lerp(1,.8,r/.12):r<=.4?lerp(.8,.14,(r-.12)/.28):lerp(.14,0,saturate((r-.4)/.6));
        float rays=0;
        [unroll] for(int ray=0;ray<8;ray++) {
            float2 p=Rot(q,-ray*.78539816339);
            float len=(ray%2==0)?.49:.21,thickness=(ray%2==0)?.018:.01,gain=(ray%2==0)?.95:.45;
            float d=abs(p.y)-thickness*(1-saturate(p.x/len));
            float aa=max(fwidth(d),.00001);
            rays+=gain*(1-saturate(p.x/len))*(1-smoothstep(-aa*.5,aa*.5,d))*step(0,p.x)*step(p.x,len);
        }
        alpha*=saturate(halo+rays);
    } else if(kind==19) {
        float r=length(uv-.5)/.48;
        float mask=r<=.78?lerp(.35,.42,r/.78):r<=.9?lerp(.42,.75,(r-.78)/.12):lerp(.75,0,saturate((r-.9)/.1));
        alpha*=mask;
    } else if(kind==22) alpha*=(1-abs(uv.x-.5)*2)*(1-abs(uv.y-.5)*2);
    else if(kind==6) alpha*=smoothstep(.5,.1,length(uv-.5));
    else if(kind==7) alpha*=1-smoothstep(v.shape.y-edge,v.shape.y+edge,min(min(uv.x,1-uv.x)*v.rect.z,min(uv.y,1-uv.y)*v.rect.w));
    else if(kind==8) alpha*=1-smoothstep(-edge,edge,max(abs(local.x)-uv.y*v.rect.z*.5,-local.y-v.rect.w*.5));
    else if(kind==9) { float2 q=abs(uv-.5);float d=max(abs(q.x-q.y)-.2,q.x+q.y-.8);float aa=max(fwidth(d),.00001);alpha*=1-smoothstep(-aa*.5,aa*.5,d); }
    else if(kind==26) alpha*=saturate(1-abs(uv.x-.5)*2)*saturate(1-abs(uv.y-.5)*2);
    else if(kind==16) alpha*=1-smoothstep(.5-edge/max(v.rect.z,v.rect.w),.5+edge/max(v.rect.z,v.rect.w),length(uv-.5));
    else if(kind==5) { alpha*=pow(saturate(1-abs(uv.x-.5)*2),3)*sin(uv.y*3.14159265);color*=2; }
    else if(kind==10||kind==11) {
        float2 imageUV=uv;
        if(kind==10){float aspect=v.rect.z/max(v.rect.w,1),imageAspect=max(TextureSize.z,1)/max(TextureSize.w,1);imageUV=(uv-.5)*float2(min(1,aspect/imageAspect),min(1,imageAspect/aspect))+.5;}
        color=CoverTexture.SampleLevel(LinearClamp,v.uv.xy+imageUV*v.uv.zw,0).rgb;
        if(kind==11) alpha*=1-smoothstep(.48,.5,length(uv-.5));
        else if(v.shape.z>0){float2 q=abs(local)-v.rect.zw*.5+v.shape.z;alpha*=1-smoothstep(-edge,edge,length(max(q,0))+min(max(q.x,q.y),0)-v.shape.z);}
    } else if(kind==13) {
        float4 picture=ModeAtlasTexture.SampleLevel(LinearClamp,v.uv.xy+uv*v.uv.zw,0);
        color=picture.rgb/max(picture.a,.00001);alpha*=picture.a;
    }
    if(kind==15) {
        float t=saturate(length(uv-.5)*2);
        alpha*=t<=.35 ? lerp(.72,.52,t/.35) : t<=.7 ? lerp(.52,.2,(t-.35)/.35) : lerp(.2,0,(t-.7)/.3);
    }
    if(kind==14){
        float2 a=v.uv.xy,b=v.uv.zw,c=v.effect.xy;
        float direction=Cross2(b-a,c-a)>=0?1:-1;
        alpha*=step(0,Cross2(b-a,uv-a)*direction)*step(0,Cross2(c-b,uv-b)*direction)*step(0,Cross2(a-c,uv-c)*direction);
    }
    return float4(color*alpha,alpha);
}

// Pendolo keeps the original static triangle mesh on the GPU. Only 32 part transforms change each frame.
struct ClockVertex { float2 position; float4 color; float part; };
StructuredBuffer<ClockVertex> ClockVertices:register(t7);
cbuffer Clock:register(b3) { float4 ClockParts[32]; float4 ClockViewport; };
struct ClockOutput { float4 position:SV_Position; float4 color:COLOR0; };
ClockOutput ClockworkVS(uint id:SV_VertexID) {
    ClockVertex v=ClockVertices[id];
    float4 t=ClockParts[(uint)(v.part+.5)];
    float2 xy=float2(v.position.x*t.z-v.position.y*t.w,v.position.x*t.w+v.position.y*t.z)+t.xy;
    ClockOutput o;o.position=float4(xy/ClockViewport.xy*float2(2,-2)+float2(-1,1)+Jitter.xy/Screen.xy*float2(2,-2),0,1);o.color=v.color;return o;
}
float4 ClockworkPS(ClockOutput input):SV_Target { return float4(input.color.rgb*input.color.a,input.color.a); }
