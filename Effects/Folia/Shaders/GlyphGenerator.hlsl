// Source-space MTSDF generation from original line/quadratic/cubic Bezier edges.
// Edge coloring and winding stay on the CPU; distance evaluation runs on D3D12.
// Curve search and selectors follow msdfgen (Viktor Chlumsky, MIT; Licenses/msdfgen.txt).
struct Edge { float4 points,control,domains,meta,tangents; };
struct Contour { uint first,count; int winding; uint reserved; };
struct Job { uint4 rect; uint4 span; float4 offset; };
StructuredBuffer<Edge> Edges:register(t0);
StructuredBuffer<Contour> Contours:register(t1);
StructuredBuffer<Job> Jobs:register(t2);
Texture2DArray<float4> Distances:register(t3);
Texture2DArray<float> Horizontal:register(t4);
Texture2DArray<float4> RawDistances:register(t5);
RWTexture2DArray<float4> DistanceOut:register(u0);
RWTexture2DArray<float> ShadowOut:register(u1);
RWTexture2DArray<float> HorizontalOut:register(u2);
RWTexture2DArray<float4> RawOut:register(u3);
cbuffer Generate:register(b0) { uint JobCount; float DistanceRange; float Sigma; uint Radius; };
float Cross(float2 a,float2 b){return a.x*b.y-a.y*b.x;}
float Median(float3 d){return max(min(d.x,d.y),min(max(d.x,d.y),d.z));}
float2 Unit(float2 p){return p*rsqrt(max(dot(p,p),1e-20));}
float NonzeroSign(float x){return x<0?-1:1;}
int QuadraticRoots(float a,float b,float c,out float3 roots){
    roots=0;if(abs(a)<1e-12){if(abs(b)<1e-12)return 0;roots.x=-c/b;return 1;}
    float discriminant=b*b-4*a*c;if(discriminant<0)return 0;
    float q=sqrt(max(discriminant,0));roots.xy=float2(-b+q,-b-q)/(2*a);return discriminant>0?2:1;
}
int CubicRoots(float a,float b,float c,float d,out float3 roots){
    if(a==0||abs(b/a)>=1e6)return QuadraticRoots(b,c,d,roots);
    float x=b/a,y=c/a,z=d/a,q=(x*x-3*y)/9,r=(x*(2*x*x-9*y)+27*z)/54;
    float delta=r*r-q*q*q;x/=3;roots=0;
    if(delta<0){float angle=acos(clamp(r/sqrt(q*q*q),-1,1));roots=-2*sqrt(q)*cos((angle+float3(0,6.283185307,-6.283185307))/3)-x;return 3;}
    float u=-NonzeroSign(r)*pow(abs(r)+sqrt(max(delta,0)),1.0/3),v=u==0?0:q/u;
    roots.x=u+v-x;if(abs(u-v)<1e-6*max(abs(u+v),1e-12)){roots.y=-.5*(u+v)-x;return 2;}return 1;
}
float2 EdgeEnd(Edge e){return e.meta.y<1.5?e.points.zw:e.meta.y<2.5?e.control.xy:e.control.zw;}
void EdgeDistance(Edge e,float2 p,out float d,out float alignment,out float param){
    float2 a=e.points.xy,b=e.points.zw,c=e.control.xy,end=EdgeEnd(e),qa=a-p;
    if(e.meta.y<1.5){
        float2 ab=b-a,ap=-qa;param=dot(ap,ab)/max(dot(ab,ab),1e-20);
        float2 eq=(param>.5?b:a)-p;float endpoint=length(eq),ortho=Cross(ap,Unit(ab));
        if(param>0&&param<1&&abs(ortho)<endpoint){d=ortho;alignment=0;return;}
        d=NonzeroSign(Cross(ap,ab))*endpoint;alignment=abs(dot(Unit(ab),Unit(eq)));return;
    }
    float2 ad=e.tangents.xy,bd=e.tangents.zw;
    d=NonzeroSign(Cross(ad,qa))*length(qa);param=-dot(qa,ad)/max(e.meta.z,1e-20);
    float endDistance=length(end-p);
    if(endDistance<abs(d)){d=NonzeroSign(Cross(bd,end-p))*endDistance;param=1-dot(end-p,bd)/max(e.meta.w,1e-20);}
    if(e.meta.y<2.5){
        float2 ab=b-a,br=c-b-ab;float3 roots;
        int count=CubicRoots(dot(br,br),3*dot(ab,br),2*dot(ab,ab)+dot(qa,br),dot(qa,ab),roots);
        [unroll]for(int i=0;i<3;i++)if(i<count&&roots[i]>0&&roots[i]<1){
            float t=roots[i];float2 qe=qa+2*t*ab+t*t*br;float distance=length(qe);
            if(distance<=abs(d)){d=NonzeroSign(Cross(ab+t*br,qe))*distance;param=t;}
        }
    }else{
        float2 ab=b-a,br=c-b-ab,as=(end-c)-(c-b)-br;
        [loop]for(int i=0;i<=4;i++){
            float t=i*.25;float2 qe=qa+3*t*ab+3*t*t*br+t*t*t*as,d1=3*ab+6*t*br+3*t*t*as,d2=6*br+6*t*as;
            float denominator=dot(d1,d1)+dot(qe,d2);float next=t-dot(qe,d1)/(abs(denominator)>1e-12?denominator:1e-12);
            if(next>0&&next<1){
                [loop]for(int step=0;step<4;step++){
                    t=next;qe=qa+3*t*ab+3*t*t*br+t*t*t*as;d1=3*ab+6*t*br+3*t*t*as;
                    if(step==3)break;d2=6*br+6*t*as;denominator=dot(d1,d1)+dot(qe,d2);next=t-dot(qe,d1)/(abs(denominator)>1e-12?denominator:1e-12);
                    if(next<=0||next>=1)break;
                }
                float distance=length(qe);if(distance<abs(d)){d=NonzeroSign(Cross(d1,qe))*distance;param=t;}
            }
        }
    }
    alignment=param>=0&&param<=1?0:abs(dot(param<.5?ad:bd,Unit(param<.5?qa:end-p)));
}
struct Selector { float3 distance,alignment,negative,positive,pseudo; };
Selector EmptySelector(){Selector s;s.distance=-1e10;s.alignment=0;s.negative=-1e10;s.positive=1e10;s.pseudo=-1e10;return s;}
Selector Merge(Selector a,Selector b){
    bool3 replace=abs(b.distance)<abs(a.distance) || (abs(b.distance)==abs(a.distance) && b.alignment<a.alignment);
    a.distance=replace?b.distance:a.distance;a.alignment=replace?b.alignment:a.alignment;
    a.pseudo=replace?b.pseudo:a.pseudo;a.negative=max(a.negative,b.negative);a.positive=min(a.positive,b.positive);return a;
}
void AddEdge(inout Selector s,Edge e,float2 p){
    float2 ap=p-e.points.xy,bp=p-EdgeEnd(e);float d,align,t;EdgeDistance(e,p,d,align,t);
    float pseudo=d;
    if(t<0&&dot(ap,e.tangents.xy)<0&&abs(Cross(ap,e.tangents.xy))<=abs(d))pseudo=Cross(ap,e.tangents.xy);
    if(t>1&&dot(bp,e.tangents.zw)>0&&abs(Cross(bp,e.tangents.zw))<=abs(d))pseudo=Cross(bp,e.tangents.zw);
    uint color=(uint)e.meta.x;bool3 channels=bool3(color&1,color&2,color&4);
    bool3 replace=channels && (abs(d)<abs(s.distance) || (abs(d)==abs(s.distance)&&align<s.alignment));
    s.distance=replace?d:s.distance;s.alignment=replace?align:s.alignment;s.pseudo=replace?pseudo:s.pseudo;
    float pa=Cross(ap,e.tangents.xy),pb=Cross(bp,e.tangents.zw);
    if(dot(ap,e.domains.xy)>.0001 && dot(ap,-e.tangents.xy)>.0001 && abs(pa)<abs(d)){
        if(pa<=0)s.negative=channels?max(s.negative,pa):s.negative;if(pa>=0)s.positive=channels?min(s.positive,pa):s.positive;
    }
    if(dot(bp,e.domains.zw)<-.0001 && dot(bp,e.tangents.zw)>.0001 && abs(pb)<abs(d)){
        if(pb<=0)s.negative=channels?max(s.negative,pb):s.negative;if(pb>=0)s.positive=channels?min(s.positive,pb):s.positive;
    }
}
float4 Resolve(Selector s){
    float3 d=s.distance<0?s.negative:s.positive;
    d=abs(s.pseudo)<abs(d)?s.pseudo:d;
    float alpha=s.distance.x,align=s.alignment.x;
    if(abs(s.distance.y)<abs(alpha)||(abs(s.distance.y)==abs(alpha)&&s.alignment.y<align)){alpha=s.distance.y;align=s.alignment.y;}
    if(abs(s.distance.z)<abs(alpha)||(abs(s.distance.z)==abs(alpha)&&s.alignment.z<align))alpha=s.distance.z;
    return float4(d,alpha);
}
float4 ShapeDistance(Job job,float2 p,bool monochrome){
    if(job.span.z==0)return -DistanceRange;
    Selector shape=EmptySelector(),inner=EmptySelector(),outer=EmptySelector();
    float4 values[32];
    [loop]for(uint c=0;c<job.span.z;c++){
        Contour contour=Contours[job.span.y+c];Selector s=EmptySelector();
        [loop]for(uint i=0;i<contour.count;i++){
            Edge edge=Edges[job.span.w+contour.first+i];if(monochrome)edge.meta.x=7;AddEdge(s,edge,p);
        }
        float4 distance=Resolve(s);values[c]=distance;shape=Merge(shape,s);
        if(contour.winding>0 && Median(distance.rgb)>=0)inner=Merge(inner,s);
        if(contour.winding<0 && Median(distance.rgb)<=0)outer=Merge(outer,s);
    }
    float4 combined=Resolve(shape),inside=Resolve(inner),outside=Resolve(outer),distance=combined;
    float di=Median(inside.rgb),dout=Median(outside.rgb);int selected=0;
    if(di>=0 && abs(di)<=abs(dout)){
        distance=inside;selected=1;
        [loop]for(uint c=0;c<job.span.z;c++)if(Contours[job.span.y+c].winding>0 && abs(Median(values[c].rgb))<abs(dout) && Median(values[c].rgb)>Median(distance.rgb))distance=values[c];
    }else if(dout<=0 && abs(dout)<abs(di)){
        distance=outside;selected=-1;
        [loop]for(uint c=0;c<job.span.z;c++)if(Contours[job.span.y+c].winding<0 && abs(Median(values[c].rgb))<abs(di) && Median(values[c].rgb)<Median(distance.rgb))distance=values[c];
    }
    if(selected!=0){
        [loop]for(uint c=0;c<job.span.z;c++)if(Contours[job.span.y+c].winding!=selected && Median(values[c].rgb)*Median(distance.rgb)>=0 && abs(Median(values[c].rgb))<abs(Median(distance.rgb)))distance=values[c];
        if(Median(distance.rgb)==Median(combined.rgb))distance=combined;
    }
    // RGB contour selection can intentionally choose a more distant contour at a
    // channel clash; alpha remains the nearest true boundary across the shape.
    distance.a=Resolve(shape).a;
    distance*=job.offset.z;
    return distance;
}
[numthreads(8,8,1)]
void GenerateMtsdf(uint3 pixel:SV_DispatchThreadID){
    if(pixel.z>=JobCount)return;Job job=Jobs[pixel.z];if(any(pixel.xy>=job.rect.zw))return;
    RawOut[pixel]=.5+ShapeDistance(job,float2(pixel.xy)+.5-job.offset.xy,false)/DistanceRange;
}

float Weight(int k){return exp(-.5*k*k/(Sigma*Sigma));}
[numthreads(8,8,1)]
void ShadowHorizontal(uint3 pixel:SV_DispatchThreadID){
    if(pixel.z>=JobCount)return;Job job=Jobs[pixel.z];if(any(pixel.xy>=job.rect.zw))return;
    float sum=0,total=0;
    [loop]for(int k=-(int)Radius;k<=(int)Radius;k++){
        float weight=Weight(k);total+=weight;int x=(int)pixel.x+k;
        if(x>=0 && x<(int)job.rect.z)sum+=saturate((Distances.Load(int4(job.rect.xy+uint2(x,pixel.y),job.span.x,0)).a-.5)*DistanceRange+.5)*weight;
    }
    HorizontalOut[pixel]=sum/total;
}
[numthreads(8,8,1)]
void ShadowVertical(uint3 pixel:SV_DispatchThreadID){
    if(pixel.z>=JobCount)return;Job job=Jobs[pixel.z];if(any(pixel.xy>=job.rect.zw))return;
    float sum=0,total=0;
    [loop]for(int k=-(int)Radius;k<=(int)Radius;k++){
        float weight=Weight(k);total+=weight;int y=(int)pixel.y+k;
        if(y>=0 && y<(int)job.rect.w)sum+=Horizontal.Load(int4(pixel.x,y,pixel.z,0))*weight;
    }
    ShadowOut[uint3(job.rect.xy+pixel.xy,job.span.x)]=sum/total;
}
