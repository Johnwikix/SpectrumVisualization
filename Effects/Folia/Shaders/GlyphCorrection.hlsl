// Edge-priority error correction, following msdfgen's MSDFErrorCorrection
// (Viktor Chlumsky, MIT; Licenses/msdfgen.txt). Read immutable FP32 tiles and
// write corrected FP16 atlas tiles; neighboring threads never observe writes.
float3 Raw(int2 p,uint layer){return RawDistances.Load(int4(p,layer,0)).rgb;}
bool Protect(Job job,uint3 pixel,float3 a){
    [loop]for(uint ci=0;ci<job.span.z;ci++){
        Contour c=Contours[job.span.y+ci];
        [loop]for(uint ei=0;ei<c.count;ei++){
            Edge edge=Edges[job.span.w+c.first+ei],prev=Edges[job.span.w+c.first+(ei+c.count-1)%c.count];
            uint common=(uint)edge.meta.x&(uint)prev.meta.x;
            if((common&(common-1))==0){
                int2 corner=(int2)floor(edge.points.xy+job.offset.xy-.5);
                if(all((int2)pixel.xy>=corner)&&all((int2)pixel.xy<=corner+1))return true;
            }
        }
    }
    [unroll]for(int y=-1;y<=1;y++)[unroll]for(int x=-1;x<=1;x++){
        if(x==0&&y==0)continue;int2 q=(int2)pixel.xy+int2(x,y);
        if(any(q<0)||any(q>=(int2)job.rect.zw))continue;
        float3 b=Raw(q,pixel.z);float radius=1.001*length(float2(x,y))/DistanceRange;
        float am=Median(a),bm=Median(b);
        if(abs(am-.5)+abs(bm-.5)>=radius)continue;
        [unroll]for(int c=0;c<3;c++){
            float denominator=a[c]-b[c];if(denominator==0)continue;
            float t=(a[c]-.5)/denominator;
            if(t>0&&t<1){float3 v=lerp(a,b,t);if(Median(v)==v[c]&&a[c]!=am)return true;}
        }
    }
    return false;
}
uint RangeFlags(float at,float bt,float t,float a,float b,float v,float span,bool protect){
    if((a>.5&&b>.5&&v<=.5)||(a<.5&&b<.5&&v>=.5)||(!protect&&Median(float3(a,b,v))!=v)){
        float da=(t-at)*span,db=(bt-t)*span;
        return v>=a-da&&v<=a+da&&v>=b-db&&v<=b+db?1:3;
    }
    return 0;
}
struct CorrectionContext { Job job; uint3 pixel; float3 value; bool protect; bool checkShape; };
bool Artifact(CorrectionContext ctx,int2 direction,float t,float value,uint flags){
    if(flags&2)return true;
    if(!ctx.checkShape||!(flags&1))return false;
    float2 delta=t*direction;int2 lo=(int2)floor(delta),hi=lo+1;float2 f=frac(delta);
    // delta lies between this texel and one of its eight neighbors.
    int2 origin=(int2)ctx.pixel.xy;
    int2 bound=(int2)ctx.job.rect.zw-1;
    float3 oldValue=lerp(lerp(Raw(clamp(origin+lo,0,bound),ctx.pixel.z),Raw(clamp(origin+int2(hi.x,lo.y),0,bound),ctx.pixel.z),f.x),
                         lerp(Raw(clamp(origin+int2(lo.x,hi.y),0,bound),ctx.pixel.z),Raw(clamp(origin+hi,0,bound),ctx.pixel.z),f.x),f.y);
    float3 newValue=oldValue+(1-abs(delta.x))*(1-abs(delta.y))*(Median(ctx.value)-ctx.value);
    float reference=.5+ShapeDistance(ctx.job,(float2)ctx.pixel.xy+.5-ctx.job.offset.xy+delta,true).r/DistanceRange;
    return 1.111111111*abs(Median(newValue)-reference)<abs(Median(oldValue)-reference);
}
bool LinearArtifact(CorrectionContext ctx,int2 direction,float3 b){
    float3 a=ctx.value;float am=Median(a),bm=Median(b);
    if(abs(am-.5)<abs(bm-.5))return false;
    float3 da=a.yzx-a,db=b.yzx-b;
    [loop]for(int i=0;i<3;i++){
        float denominator=da[i]-db[i];if(denominator==0)continue;
        float t=da[i]/denominator;
        if(t>.01&&t<.99){float v=Median(lerp(a,b,t));
            if(Artifact(ctx,direction,t,v,RangeFlags(0,1,t,am,bm,v,1.111111111/DistanceRange,ctx.protect)))return true;}
    }
    return false;
}
bool DiagonalArtifact(CorrectionContext ctx,int2 direction,float3 b,float3 c,float3 d){
    float3 a=ctx.value;float am=Median(a),dm=Median(d);
    if(abs(am-.5)<abs(dm-.5))return false;
    float3 l=b+c-2*a,q=d+a-b-c,extreme=-.5*l/q;
    float3 da=a.yzx-a,dbc=b.yzx-b+c.yzx-c,dd=d.yzx-d;
    float span=1.571348403/DistanceRange;
    [loop]for(int i=0;i<3;i++){
        float3 roots;int count=QuadraticRoots(dd[i]-dbc[i]+da[i],dbc[i]-2*da[i],da[i],roots);
        [loop]for(int j=0;j<2;j++)if(j<count){float t=roots[j];if(t<=.01||t>=.99)continue;
            float v=Median(a+t*(l+t*q));uint flags=RangeFlags(0,1,t,am,dm,v,span,ctx.protect);
            [unroll]for(int k=0;k<2;k++){
                float ex=extreme[(i+k)%3];if(ex<=0||ex>=1)continue;
                float em=Median(a+ex*(l+ex*q));
                flags|=ex>t?RangeFlags(0,ex,t,am,em,v,span,ctx.protect):RangeFlags(ex,1,t,em,dm,v,span,ctx.protect);
            }
            if(Artifact(ctx,direction,t,v,flags))return true;
        }
    }
    return false;
}
bool FindError(CorrectionContext ctx){
    [loop]for(int y=-1;y<=1;y++)[loop]for(int x=-1;x<=1;x++){
        if(x==0&&y==0)continue;int2 q=(int2)ctx.pixel.xy+int2(x,y);
        if(any(q<0)||any(q>=(int2)ctx.job.rect.zw))continue;
        float3 d=Raw(q,ctx.pixel.z);
        if(x==0||y==0){if(LinearArtifact(ctx,int2(x,y),d))return true;}
        else if(DiagonalArtifact(ctx,int2(x,y),Raw((int2)ctx.pixel.xy+int2(x,0),ctx.pixel.z),Raw((int2)ctx.pixel.xy+int2(0,y),ctx.pixel.z),d))return true;
    }
    return false;
}
[numthreads(8,8,1)]
void CorrectMtsdf(uint3 pixel:SV_DispatchThreadID){
    if(pixel.z>=JobCount)return;Job job=Jobs[pixel.z];if(any(pixel.xy>=job.rect.zw))return;
    float4 value=RawDistances.Load(int4(pixel,0));
    CorrectionContext ctx;ctx.job=job;ctx.pixel=pixel;ctx.value=value.rgb;
    ctx.protect=Protect(job,pixel,value.rgb);ctx.checkShape=false;
    bool error=FindError(ctx);
    if(!error){ctx.protect=true;ctx.checkShape=true;error=FindError(ctx);}
    if(error)value.rgb=Median(value.rgb);
    DistanceOut[uint3(job.rect.xy+pixel.xy,job.span.x)]=value;
}
