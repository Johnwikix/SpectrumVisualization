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
    float3 color=BackgroundTuning.z>.5 ? mesh : Palette[0].rgb;
    if(BackgroundTuning.w>.5) color=BackgroundTuning.z>.5 ? lerp(mesh,SoftLight(mesh,dither.rgb),LatentFrame.w*dither.a) : lerp(Palette[0].rgb,dither.rgb,LatentFrame.w*dither.a);
    return float4(max(lerp(color,Palette[0].rgb,.35),0),1);
}

float4 VignettePS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    uv-=Jitter.xy/Screen.xy;
    float radius=length((uv-.5)*Screen.xy)/(length(Screen.xy)*.5);
    return float4(0,0,0,saturate((radius-.4)/.6)*lerp(.6,.3,SceneTuning.z));
}
float Cross2(float2 a,float2 b){return a.x*b.y-a.y*b.x;}
float InversionLuminance(float2 uv){return dot(BackdropTexture.SampleLevel(LinearClamp,uv,0).rgb,float3(.2126,.7152,.0722));}
float4 CopyPS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float3 color = max(BackdropTexture.SampleLevel(LinearClamp,uv,0).rgb,0);
    return float4(NativeLyric.x == 4 ? color : ToLinear(color),1);
}
float4 BackgroundSnapshotPS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    return BackdropTexture.SampleLevel(LinearClamp,uv,0);
}
float4 BackgroundBlendPS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    return float4(BackgroundHistoryTexture.SampleLevel(LinearClamp,uv,0).rgb*SceneTuning.w,SceneTuning.w);
}
struct TemporalOutput { float4 color:SV_Target0;float2 motion:SV_Target1;float reactive:SV_Target2;float depth:SV_Depth; };
TemporalOutput TemporalPS(float4 p:SV_Position,float2 uv:TEXCOORD0) {
    TemporalOutput o; o.color=CopyPS(p,uv);o.motion=0;
    o.depth=(100.0/(100-.1))-(100*.1/(100-.1))/5;
    // Reject changing pixels while static text or reprojectable geometry retains history.
    float2 sampleUV=uv;
    float3 current=BackdropTexture.SampleLevel(LinearClamp,sampleUV,0).rgb;
    if(Jitter.z>.5) o.reactive=1;
    else {
        float2 previousUV=sampleUV+(o.motion+PreviousJitter.xy-Jitter.xy)/Screen.xy;
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
    return o;
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
float4 SampleGlyph(Instance glyph,float2 uv,out float2 glyphUV,out float range) {
    float2 atlasUV=glyph.uv.xy+uv*glyph.uv.zw;
    // Keep bilinear taps within the glyph's MSDF tile.
    float2 halfTexel=.5/TextureSize.xy;
    glyphUV=clamp(atlasUV,glyph.uv.xy+halfTexel,glyph.uv.xy+glyph.uv.zw-halfTexel);
    range=GlyphPixelRange(atlasUV);
    return AtlasTexture.SampleLevel(LinearClamp,float3(glyphUV,glyph.glyphData.x),0);
}
float4 SpritePS(SpriteOutput input):SV_Target {
    Instance v=ReadInstance(input.instance);float2 uv=input.uv;
    float alpha=v.color.a;float3 color=v.color.rgb;
    float2 local=(uv-.5)*v.rect.zw;
    float edge=max(fwidth(local.x),fwidth(local.y));
    int kind=(int)v.shape.x;
    if(kind==0||kind==25) {
        float2 glyphUV;
        float range;
        float4 distances=SampleGlyph(v,uv,glyphUV,range);
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
        float3 rgb=color*(coverage+glow);
        if(v.glyphData.z>0) {
            uint packed=(uint)v.glyphData.z-1;
            float3 haloColor=float3((packed>>16)&255,(packed>>8)&255,packed&255)/255.0;
            rgb=color*coverage+haloColor*glow;
        }
        return float4(rgb*opacity,v.glyphData.y>.5?0:saturate(coverage+glow)*opacity);
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
    } else if(kind>=32 && kind<=36) {
        float boundary=min(v.rect.z*.5-abs(local.x),v.rect.w*.5-abs(local.y));
        if(kind==33) {
            float2 q=(uv-.5)*2;
            float loops=max(abs(q.y)*2.2+abs(abs(q.x)-.44)*2.1-.82,length(q)*1.4-.95);
            float knot=length(q)-.25;
            float d=min(loops,knot);
            alpha*=1-smoothstep(-.05,.05,d);
            color=lerp(color,1,smoothstep(-.20,-.06,d)*.85);
        } else if(kind==36) {
            float2 q=(uv-.5)*2;
            float a=atan2(q.y,q.x);
            float d=length(q)-(.62+.16*cos(a*5));
            alpha*=1-smoothstep(-.035,.04,d);
            color=lerp(color,1,smoothstep(-.16,-.04,d)*.86);
            color=lerp(color,1,(1-smoothstep(.15,.24,length(q)))*.75);
        } else {
            float2 p=uv*v.rect.zw/max(Options.z,1);
            float2 grid=abs(frac(p/12)-.5);
            float marks=kind==32?1-smoothstep(.1,.16,length(grid)):
                kind==34?1-smoothstep(.02,.06,min(grid.x,grid.y)):0;
            color=lerp(color,1,marks*.52);
            color=lerp(color,1,(1-smoothstep(1,2.3,boundary))*.91);
        }
    } else if(kind>=37 && kind<=40) {
        float h=v.rect.w, x=uv.x*v.rect.z+v.uv.x, y=uv.y*h;
        float aa=max(edge*.65,.05);
        if(kind==37) {
            float r=max(3,h*.21), tile=(frac(x/(2*r))-.5)*2*r;
            float scallop=length(float2(tile,abs(y-h*.5)-h*.25))-r;
            float body=min(abs(y-h*.5)-h*.25,scallop);
            float hole=min(length(float2(tile,abs(y-h*.5)-h*.25-r*.45))-r*.3,
                length(float2(tile,abs(y-h*.5)-h*.175))-max(.8,h*.035));
            float ribbon=1-smoothstep(-aa,aa,abs(y-h*.5)-h*.1);
            float highlight=1-smoothstep(-aa,aa,abs(y-h*.452)-max(.5,h*.016));
            color=lerp(1,color,ribbon);
            color=lerp(color,1,highlight*.45);
            alpha*=max(ribbon,.96*(1-smoothstep(-aa,aa,max(body,-hole))));
        } else if(kind==38) {
            float pitch=max(10,h*.9), dashWidth=max(2,h*.11);
            float dx=max(abs(frac(x/pitch)*pitch-(pitch*.55+dashWidth)*.5)-(pitch*.55-dashWidth)*.5,0);
            float foreground=1-smoothstep(-aa,aa,length(float2(dx,y-h*.5))-dashWidth*.5);
            float shadow=.35*(1-smoothstep(-aa,aa,length(float2(dx,y-h*.5-dashWidth*.6))-dashWidth*.5));
            float coverage=foreground+shadow*(1-foreground);
            color=lerp(float3(.118,.063,.188),color,foreground/max(coverage,.001));
            alpha*=coverage;
        } else if(kind==39) {
            float frame=h*1.18, pitch=frame*.25;
            float2 hole=float2(abs(frac(x/pitch)-.5)*pitch-pitch*.24,abs(abs(uv.y-.5)-.375)*h-h*.055);
            float frameX=frac(x/frame), tile=abs(frameX-.5)-.43;
            float interior=1-smoothstep(-aa,aa,max(tile*frame,abs(y-h*.5)-h*.25));
            float variant=fmod(floor(x/frame)+3000,3);
            float3 mat=variant==0?color:variant==1?float3(1,.56,.71):float3(.56,.94,.78);
            color=lerp(float3(.169,.122,.239),mat,interior);
            float center=length(float2((frameX-.5)*frame,y-h*.5));
            color=lerp(color,1,.9*(1-smoothstep(h*.10,h*.125,center)));
            alpha*=.92*smoothstep(-aa,aa,max(hole.x,hole.y));
        } else {
            float2 p=uv*float2(160,48);
            float tooth=(1-abs(frac(p.y/12)*2-1))*4.2;
            float pattern;
            if(v.shape.w<.5)
                pattern=.55*(1-smoothstep(.24,.31,abs(frac((p.x+p.y)/18)-.5)));
            else if(v.shape.w<1.5)
                pattern=.65*(1-smoothstep(2.7,3.7,length((frac(p/16)-.5)*16)));
            else
                pattern=.38*(1-fmod(floor(p.x/12)+floor(p.y/12),2));
            color=lerp(color,1,pattern);
            alpha*=lerp(.78,1,pattern)*smoothstep(-.8,.8,min(p.x,160-p.x)-tooth);
        }
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

struct InkFields {
    float4 amount:SV_Target0;
    float4 color:SV_Target1;
};

InkFields InkFieldPS(SpriteOutput input) {
    Instance v=ReadInstance(input.instance);
    InkFields result;
    result.color=0;
    int kind=(int)v.shape.x-27;
    if (kind>=0) {
        float2 scale=max(v.uv.xy,.0001);
        float2 local=(input.uv*2-1)*v.uv.zw;
        local.y=-local.y;
        float2 turn=v.glyphData.zw;
        float2 w=v.glyphData.xy+float2(local.x*turn.x-local.y*turn.y,local.x*turn.y+local.y*turn.x);
        float t=floor(Time.y*8)/8;
        float2 shake=float2(sin(w.y*1.7+t*.35)+sin(w.x*.9-t*.2),sin(w.x*1.3-t*.3)+sin(w.y*1.1+t*.25))*.5
            +.32*float2(sin(w.y*7.1+w.x*2.3+t*.6),sin(w.x*6.3-w.y*1.9-t*.5))
            +.12*float2(sin(w.y*37+w.x*17)+sin(w.x*53),sin(w.x*41-w.y*13)+sin(w.y*59));
        local+=float2(shake.x*turn.x+shake.y*turn.y,shake.y*turn.x-shake.x*turn.y)*.05;
        float3 mix=v.color.rgb;
        float core;
        float halo;
        if (kind==0) {
            float2 q=local/scale;
            float k=max(0,1-dot(q,q));
            core=k*k*k*1.45;
            halo=k*k*(mix.y*.6+.55*(mix.x+mix.z));
            result.amount=float4(core*mix.x,halo,core*mix.z,1);
        } else if (kind==1) {
            float band=abs(length(local/scale)-.88);
            core=1-smoothstep(.012,.03,band);
            halo=1-smoothstep(.02,.12,band);
            result.amount=float4(core*mix.x,core*mix.y+halo*.35,core*mix.z,1);
        } else {
            const float STROKE=.016;
            float reach=STROKE;
            float distance;
            float cut=1;
            if (kind==2) {
                reach=scale.y;
                distance=max(abs(local.y),abs(local.x)-scale.x+reach);
                if (v.shape.w>.5) cut=step(frac((local.x/scale.x*.5+.5)*v.shape.w+.25),.55);
            } else if (kind==3) {
                reach=STROKE*scale.y;
                distance=abs(length(local)-.9*scale.x);
                cut=step(abs(atan2(local.y,local.x)),v.shape.w*3.14159265+.001);
            } else {
                float phase=1.57079633*v.shape.w*(local.x/scale.x+1);
                float amplitude=scale.y*.75;
                float slope=amplitude*cos(phase)*1.57079633*v.shape.w/scale.x;
                distance=abs(local.y-amplitude*sin(phase))/sqrt(1+slope*slope);
                cut=step(abs(local.x),scale.x);
            }
            float soft=min(reach,STROKE);
            core=(1-smoothstep(reach-soft*.8,reach+soft*1.4,distance))*cut;
            halo=(1-smoothstep(reach,reach+.11,distance))*cut*max(min(1,mix.x+mix.z)*.55,mix.y*.4);
            result.amount=float4(core*mix.x,core*mix.y+halo,core*mix.z,1);
        }
        result.color=float4(Palette[3].rgb,1)*(core*mix.z);
        return result;
    }
    if ((int)v.shape.x!=0) { result.amount=0; return result; }
    float2 sampleUv=v.uv.xy+input.uv*v.uv.zw;
    float2 inset=.5/TextureSize.xy;
    float2 at=clamp(sampleUv,v.uv.xy+inset,v.uv.xy+v.uv.zw-inset);
    float4 distances=AtlasTexture.SampleLevel(LinearClamp,float3(at,v.glyphData.x),0);
    float range=GlyphPixelRange(sampleUv);
    float tight=saturate(.5+(Median3(distances.rgb)-.5)*range);
    float soft=smoothstep(-.39,.35,distances.a-.5);
    float age=v.transform.w;
    float on=step(0,age);
    age=max(0,age);
    float set=on*(1-exp(-age*4.5));
    float bulge=on*exp(-age*1.5);
    float melt=v.effect.w,keep=1-melt;
    float ink=tight*set*keep*keep+soft*(bulge*1.25+.3*set+.9*melt*set)*keep;
    float tone=(tight*(1-set)*.85+soft*(.3+.45*set))*v.glyphData.w*keep;
    if(v.glyphData.z>.5) {
        ink=tight*on*keep*keep*1.12;
        tone=min(.55,soft*1.6)*on*keep;
    }
    float tinted=v.glyphData.y*ink;
    result.amount=float4(ink-tinted,tone,tinted,1);
    result.color=float4(v.color.rgb,1)*tinted;
    return result;
}
float4 WindowMaskPS(SpriteOutput input):SV_Target {
    Instance ink=ReadInstance(input.instance);
    if(ink.shape.x==28) {
        float d=length((input.uv-.5)*2);
        return float4(0,(1-smoothstep(.73,1,d))*ink.color.a,0,0);
    }
    if(ink.shape.x==29) {
        float d=length((input.uv-.5)*2);
        float mask=d<.45?lerp(1,.7,d/.45):.7*saturate((1-d)/.55);
        return float4(0,0,mask*ink.color.a,ink.shape.y*(1-smoothstep(.98,1,d)));
    }
    if(ink.shape.x!=27) return float4(SpritePS(input).a,0,0,0);
    float2 local=(input.uv-.5)*ink.rect.zw;
    local.x=max(abs(local.x)-(ink.rect.z-ink.rect.w)*.5,0);
    float d=length(local), r=ink.shape.y*.5;
    float aa=max(.5,max(fwidth(local.x),fwidth(local.y)));
    float wide=saturate(.5+(r*1.3-d)/aa);
    float mid=saturate(.5+(r-d)/aa);
    float core=saturate(.5+(r*.62-d)/aa);
    float coverage=wide*.38;
    coverage=lerp(coverage,.72,mid);
    coverage=lerp(coverage,1,core);
    return float4(coverage*ink.color.a,0,0,0);
}

SpriteOutput MachiPanelVS(uint vertex:SV_VertexID,uint id:SV_InstanceID) {
    id+=InstanceOffset;
    Instance glyph=Instances[id];
    float2 quad=float2((vertex==1||vertex==2||vertex==4)?1:0,(vertex>=2&&vertex<=4)?1:0);
    float2 local=(quad-.5)*glyph.rect.zw;
    float sine,cosine;
    sincos(glyph.transform.x,sine,cosine);
    float2 panelPosition=glyph.rect.xy+glyph.rect.zw*.5+
        float2(cosine*local.x-sine*local.y,sine*local.x+cosine*local.y);
    float2 uv=panelPosition/float2(1024,560);
    SpriteOutput o;
    o.position=float4(uv*float2(2,-2)+float2(-1,1),0,1);
    o.uv=quad;o.instance=id;o.pixel=uv;
    return o;
}

float4 MachiPanelPS(SpriteOutput input) : SV_Target {
    Instance glyph=Instances[input.instance];
    float band=glyph.glyphData.w;
    float lower=band==0 ? 0 : (band==1 ? 320 : 320+40*band);
    float upper=band==0 ? 320 : (band==1 ? 400 : (band==4 ? 560 : lower+40));
    clip(min(input.position.y-lower,upper-input.position.y));
    if (band==0 && glyph.glyphData.y>0) {
        float2 glyphUV;
        float range;
        float4 distances=SampleGlyph(glyph,input.uv,glyphUV,range);
        float coverage=saturate((Median3(distances.rgb)-.5)*range+.5)*glyph.color.a;
        float progress=glyph.effect.x;
        float3 color=glyph.effect.yzw;
        if (progress>=1) color=glyph.color.rgb;
        else if (progress>=0) {
            // Sweep across the outline, excluding the distance field's transparent padding.
            float padding=glyph.glyphData.z;
            float x=(input.uv.x-padding)/max(1-2*padding,.001);
            float edge=max(fwidth(x),.012);
            float fill=smoothstep(-edge,edge,progress-x);
            float3 swept=lerp(glyph.shape.yzw,glyph.color.rgb,smoothstep(.84,1,progress));
            color=lerp(color,swept,fill);
        }
        return float4(color*coverage,coverage);
    }
    float4 ink=SpritePS(input);
    // The main LED face feeds DXR in linear light. Preserve MSDF coverage when decoding palette colors.
    if (band==0 && ink.a>0) ink.rgb=ToLinear(saturate(ink.rgb/ink.a))*ink.a;
    return ink;
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
    ClockOutput o;o.position=float4(xy/ClockViewport.xy*float2(2,-2)+float2(-1,1)+Jitter.xy/Screen.xy*float2(2,-2),0,1);
    o.color=v.color;
    // Jewel faces and dial strokes share the existing mesh; light them from live transients.
    float jewel=saturate(max(v.color.r,max(v.color.g,v.color.b))*2);
    o.color.rgb*=1+(RealtimeMotion.x*.8+RealtimeMotion.y*1.4)*jewel;
    return o;
}
float4 ClockworkPS(ClockOutput input):SV_Target { return float4(input.color.rgb*input.color.a,input.color.a); }
