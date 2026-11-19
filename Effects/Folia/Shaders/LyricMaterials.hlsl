// D3D12 adaptation of the Hanabi, Metaphor and Nebbia film layers in folia-major (AGPL-3.0).
float NoiseCell(float2 cell) {
    float3 value=frac(float3(cell.xyx)*.1031);
    value+=dot(value,value.yzx+31.32);
    return frac((value.x+value.y)*value.z);
}

float3 HanabiSkyStop(float hue,float saturation,float light,float targetHue,float targetSaturation) {
    float lean=frac(targetHue-hue+.5)-.5;
    hue+=lean*.3*step(.2,targetSaturation);
    float3 rgb=saturate(abs(frac(hue+float3(0,2.0/3,1.0/3))*6-3)-1);
    return light+(rgb-.5)*(1-abs(2*light-1))*saturation;
}

float4 HanabiSkyPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    uv-=Jitter.xy/Screen.xy;
    float3 accent=Palette[3].rgb;
    float maximum=max(accent.r,max(accent.g,accent.b)),minimum=min(accent.r,min(accent.g,accent.b));
    float delta=maximum-minimum,light=(maximum+minimum)*.5;
    float saturation=delta/max(.001,1-abs(light*2-1));
    float hue=(maximum==accent.r?(accent.g-accent.b)/max(.001,delta):
        maximum==accent.g?2+(accent.b-accent.r)/max(.001,delta):4+(accent.r-accent.g)/max(.001,delta))/6;
    float3 top=HanabiSkyStop(.66,.62,.06,hue,saturation);
    float3 upper=HanabiSkyStop(.70,.50,.11,hue,saturation);
    float3 lower=HanabiSkyStop(.80,.48,.19,hue,saturation);
    float3 bottom=HanabiSkyStop(.90,.55,.27,hue,saturation);
    float y=saturate(uv.y);
    float3 color=y<.45?lerp(top,upper,y/.45):y<.82?lerp(upper,lower,(y-.45)/.37):lerp(lower,bottom,(y-.82)/.18);
    color=lerp(Palette[0].rgb,color,.85);
    float2 css=(pixel.xy-Jitter.xy)*Screen.zw/(Screen.xy*Options.z);
    css.y-=Time.y*6;
    float2 tile=frac(css/26);
    float dotMask=1-smoothstep(1.1,2.0,min(length(tile-.25),length(tile-.75))*26);
    float2 grid=(uv+float2(0,Time.y*.012))*float2(92,92*Screen.y/Screen.x);
    float starSeed=NoiseCell(floor(grid));
    float2 local=frac(grid)-.5;
    float star=exp(-dot(local,local)*85)*smoothstep(.93,1,starSeed);
    color=lerp(color,1,dotMask*.075);
    color+=lerp(float3(.6,.39,.67),accent,.45)*star*.62;
    return float4(color,1);
}

float InkNoise(float2 p) {
    float2 cell=floor(p),f=frac(p);
    f=f*f*(3-2*f);
    return lerp(lerp(NoiseCell(cell),NoiseCell(cell+float2(1,0)),f.x),
        lerp(NoiseCell(cell+float2(0,1)),NoiseCell(cell+1),f.x),f.y);
}

float4 InkDevelopPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float2 fragment=(pixel.xy-Jitter.xy)*Screen.zw/(Screen.xy*Options.z);
    float2 viewport=Screen.zw/Options.z;
    float aspect=Screen.x/Screen.y;
    float2 p=float2(uv.x*aspect,1-uv.y);
    float seconds=floor(Time.y*8)/8,beat=floor(Time.y*6),seed=NativeComposition.z;
    float2 warp=(float2(InkNoise(p*3.2+float2(0,seconds*.16)),InkNoise(p*3.2+float2(17.3,-seconds*.13)))-.5)*.024+
        (float2(InkNoise(p*9+float2(seconds*.3,3)),InkNoise(p*9+float2(31.7,-seconds*.27)))-.5)*.007;
    float2 at=uv+float2(warp.x/aspect,-warp.y);
    float3 field=LayerTexture.SampleLevel(LinearClamp,at,0).rgb;
    float4 tint=BackgroundHistoryTexture.SampleLevel(LinearClamp,at,0);
    float3 accentColor=tint.a>.002?saturate(tint.rgb/tint.a):Palette[3].rgb;
    float threshold=.5+(InkNoise(fragment*.45+seed*91)-.5)*.16+(NoiseCell(floor(fragment*.9)+seed*13)-.5)*.08;
    float inkField=field.r+NativeComposition.y;
    float edge=max(fwidth(inkField),.0001)*.75;
    float ink=smoothstep(threshold-edge,threshold+edge,inkField);
    float accentEdge=max(fwidth(field.b),.0001)*.75;
    float accent=smoothstep(threshold-accentEdge,threshold+accentEdge,field.b);
    float vignette=smoothstep(.42,1,length(uv-.5)*1.3);
    float tone=saturate(field.g*.9+(field.r+field.b)*.5+vignette*.3);
    float2 grid=Rot(float2(fragment.x,viewport.y-fragment.y),NativeComposition.x)/7;
    float radius=sqrt(tone)*.58;
    float softness=.9/7;
    float dots=(1-smoothstep(radius-softness,radius+softness,length(frac(grid)-.5)))*step(.015,tone);

    float3 paper=Palette[0].rgb,inkColor=Palette[1].rgb;
    float3 color=lerp(paper,lerp(paper,Palette[2].rgb,.82),dots);
    color=lerp(color,accentColor,accent);
    color=lerp(color,lerp(accentColor,inkColor,.4),dots*accent*smoothstep(.85,1,field.g));
    color=lerp(color,inkColor,ink);
    float2 border=min(fragment,viewport-fragment);
    float mark=max(step(abs(border.x-18),.75)*step(18,border.y)*step(border.y,34),
        step(abs(border.y-18),.75)*step(18,border.x)*step(border.x,34));
    color=lerp(color,inkColor,mark*.85);
    float fine=NoiseCell(floor(fragment/1.4)+beat*7.13+seed*3);
    float mottle=InkNoise(fragment*.12+seed*50);
    float speck=step(.988,NoiseCell(floor(fragment/2.2)+beat*3.7+.5));
    color*=1+((fine-.5)*.22+(mottle-.5)*.1);
    color=lerp(color,lerp(inkColor,paper,ink),speck*.55);
    return float4(saturate(color),1);
}

// Nebbia night scene adapted from nebbiaSceneCommonGlsl.ts, nebbiaSceneGlsl.ts and
// nebbiaSpotGlsl.ts at 6ee9404. These functions produce display-referred color.
float3 NebbiaHsl(float3 rgb) {
    float hi=max(rgb.r,max(rgb.g,rgb.b)), lo=min(rgb.r,min(rgb.g,rgb.b));
    float light=(hi+lo)*.5, delta=hi-lo;
    if(delta<=.00001) return float3(0,0,light);
    float h=hi==rgb.r?(rgb.g-rgb.b)/delta : (hi==rgb.g?(rgb.b-rgb.r)/delta+2:(rgb.r-rgb.g)/delta+4);
    return float3(frac(h/6),delta/max(1-abs(2*light-1),.00001),light);
}
float3 NebbiaRgb(float3 hsl) {
    float3 k=frac(hsl.x+float3(0,8.0/12.0,4.0/12.0))*12;
    float a=hsl.y*min(hsl.z,1-hsl.z);
    return hsl.z-a*clamp(min(k-3,9-k),-1,1);
}
float3 NebbiaLight(float3 rgb) {
    float3 hsl=NebbiaHsl(rgb);
    return NebbiaRgb(float3(hsl.x,max(hsl.y,.55),clamp(hsl.z,.6,.78)));
}
float3 NebbiaSky(float3 rgb,float lightness) {
    float3 hsl=NebbiaHsl(rgb);
    return NebbiaRgb(float3(hsl.x,min(hsl.y,.55),lightness));
}
#define uTime NativeComposition.z
#define uBus 1.0
#define uNeon 1.0
#define uLa NebbiaLight(Palette[3].rgb)
#define uLb NebbiaLight(Palette[2].rgb)
#define uLc NebbiaLight(Palette[1].rgb)
#define uSky0 NebbiaSky(Palette[0].rgb,.05)
#define uSky1 NebbiaSky(lerp(Palette[0].rgb,Palette[3].rgb,.3),.14)
#define uSpotTgt (NativeComposition.xy*vec2(Screen.x/Screen.y,1))
#define uSpotR NativeLyric.y
#define uSpotI NativeLyric.z
#define uSpotSourceY (((uint)NativeLyric.w%3)==1?-.04:.595)
#define uSpotSrc vec2(.8*Screen.x/Screen.y,uSpotSourceY)
#define uSpotSrc2 vec2(.2*Screen.x/Screen.y,uSpotSourceY)

float hash(float2 p) { p = fract(p * vec2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
float h1(float n) { n = fract(n * 0.1031); n *= n + 33.33; n *= n + n; return fract(n); }
float vnoise(float2 p) {
  float2 i = floor(p), f = fract(p); f = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1, 0)), f.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), f.x), f.y);
}
float3 pick(float h) { return h < 0.34 ? uLa : (h < 0.67 ? uLb : uLc); }
float glowPt(float2 d, float r, float b, float streak) {
  d.x = sign(d.x) * max(abs(d.x) - streak, 0.0);
  float s2 = r * r + b * b;
  return (r * r / s2) * exp(-dot(d, d) / s2 * 1.6);
}
float reach(float r, float b) { return 3.0 * sqrt(r * r + b * b); }
static const float HZ = 0.6, ROAD = 0.7;
static const float WIND = 0.6;
static const float3 SODIUM = float3(1.0, 0.68, 0.32);
static const int SCN_BUILDINGS = 1, SCN_LIGHTS = 2, SCN_CARS = 4, SCN_ROAD = 8, SCN_RAIN = 16, SCN_RAIN_SLANT = 32;
bool sceneryOn(int bit) { return ((((uint)NativeLyric.w / 3 == 2 && (uint)NativeLyric.w % 3 != 1) ? 15 : 63) & bit) != 0; }

float3 lampsSigns(float2 q, float b, float t) {
  float3 c = vec3(0.0);
  float xs = q.x + t * 0.42, cw = 0.34, streak = 0.012 * uBus;
  float cs = floor(xs / cw);
  bool sg = abs(q.y - 0.51) < 0.05 + reach(0.016, b + 0.002), sh = abs(q.y - 0.645) < reach(0.026, b + 0.003);
  if (sg || sh) [loop] for (float k = -1.0; k <= 1.0; k++) {
    float n = cs + k;
    if (sg) {
      float2 cen = vec2((n + 0.2 + 0.6 * h1(n)) * cw, 0.46 + 0.1 * h1(n + 17.0));
      if (h1(n + 33.0) < 0.75) c += pick(h1(n + 2.0)) * glowPt(vec2(xs, q.y) - cen, 0.006 + 0.01 * h1(n + 5.0), b + 0.002, streak) * 1.6;
    }
    float2 shop = vec2((n + 0.5) * cw, 0.645);
    if (sh && h1(n + 41.0) < 0.6) c += mix(SODIUM, pick(h1(n + 8.0)), 0.3) * glowPt(vec2(xs, q.y) - shop, 0.016 + 0.01 * h1(n + 3.0), b + 0.003, streak) * 0.9;
  }
  float xl = q.x + t * 0.22, lw = 0.55;
  float cl = floor(xl / lw);
  if (abs(q.y - 0.36) < reach(0.07, b)) [loop] for (float lamp = -1.0; lamp <= 1.0; lamp++) {
    float2 d = vec2(xl - (cl + lamp + 0.5) * lw, q.y - 0.36);
    c += SODIUM * (glowPt(d, 0.007, b, 0.004 * uBus) * 2.6 + glowPt(d, 0.07, b, 0.0) * 0.3);
  }
  return c;
}
float4 building(float2 q, float b, float t, float layer) {
  float near = layer;
  float x = q.x + t * mix(0.02, 0.035, near) + layer * 7.7;
  float cw = mix(0.065, 0.12, near), aa = max(b, 0.0012);
  float c0 = floor(x / cw);
  float4 res = vec4(0.0);
  float fx = x - c0 * cw, nbReach = aa + 0.0013;
  float neighbors=ceil(nbReach/cw);
  [loop] for (float k = -neighbors; k <= neighbors; k++) {
    if ((k < 0.0 && fx > nbReach) || (k > 0.0 && cw - fx > nbReach)) continue;
    float n = c0 + k + layer * 100.0;
    float w = cw * (0.72 + 0.26 * h1(n));
    float left = (c0 + k) * cw + (cw - w) * h1(n + 1.0);
    float lx = x - left;
    float hgt = mix(0.07, 0.11, near) + mix(0.16, 0.27, near) * pow(h1(n + 2.0), 1.5);
    float base = HZ + near * 0.015;
    float top = base - hgt;
    bool tier = h1(n + 3.0) < 0.5;
    float tw = w * (0.5 + 0.25 * h1(n + 4.0)), tl = (w - tw) * 0.5, th = hgt * (0.12 + 0.15 * h1(n + 5.0));
    float body = smoothstep(-aa, aa, lx) * smoothstep(-aa, aa, w - lx) * smoothstep(-aa, aa, q.y - top);
    float up = tier ? smoothstep(-aa, aa, lx - tl) * smoothstep(-aa, aa, tl + tw - lx) * smoothstep(-aa, aa, q.y - (top - th)) : 0.0;
    float roof = tier ? top - th : top;
    float ant = h1(n + 6.0) < 0.3 ? smoothstep(0.0012 + aa, 0.0, abs(lx - w * 0.5)) * smoothstep(-aa, aa, q.y - (roof - 0.04 - 0.03 * h1(n + 7.0))) : 0.0;
    float cov = max(max(body, up), ant);
    if (cov <= 0.0) continue;
    float3 fac = mix(uSky0, vec3(0.015, 0.018, 0.025), 0.55) * (0.7 + 0.6 * h1(n + 8.0));
    float lowLit = pow(clamp((q.y - roof) / (base - roof), 0.0, 1.0), 2.5);
    fac += mix(SODIUM, uLa, 0.5) * 0.1 * lowLit;
    fac += mix(uSky1, uLb, 0.3) * 0.12 * smoothstep(0.003 + aa, 0.0, lx) * body;
    float fh = mix(0.0075, 0.012, near), wc = mix(0.0055, 0.0085, near);
    float2 g = vec2((lx - wc * 0.6) / wc, (q.y - roof - fh * 0.8) / fh);
    float2 id = floor(g), f = fract(g);
    float dens = 0.15 + 0.5 * h1(n + 9.0);
    float floorOn = step(0.18, hash(vec2(id.y, n)));
    float on = step(hash(id + n * 3.7), dens) * floorOn;
    float inside = smoothstep(-aa, aa, lx - wc * 0.6) * smoothstep(-aa, aa, w - wc * 0.6 - lx) * smoothstep(-aa, aa, q.y - roof - fh * 0.8) * smoothstep(-aa, aa, base - fh - q.y);
    float pane = smoothstep(0.32, 0.22, abs(f.x - 0.5)) * smoothstep(0.3, 0.2, abs(f.y - 0.5));
    float blurMix = clamp(b / (wc * 0.8), 0.0, 1.0);
    float win = mix(pane * on, 0.3 * dens * 0.82, blurMix) * inside * max(body, up);
    float hc = hash(id + n * 1.3);
    float3 wcol = hc < 0.5 ? vec3(1.0, 0.78, 0.5) : (hc < 0.8 ? vec3(0.78, 0.88, 1.0) : pick(hash(id + 5.0)));
    wcol = mix(wcol * (0.55 + 0.6 * hash(id + 2.9)), vec3(0.95, 0.82, 0.66) * 0.85, blurMix);
    float3 bc = fac + wcol * win * mix(0.7, 1.0, near);
    bc = mix(bc, mix(uSky0, uSky1, 0.7), mix(0.45, 0.12, near));
    res = vec4(mix(res.rgb, bc, cov), max(res.a, cov));
  }
  return res;
}
float3 neonLights(float2 q, float b, float t) {
  float3 c = vec3(0.0);
  float bb = b + 0.003;   // 散景本身就是虚的
  if (abs(q.y - (HZ - 0.12)) < 0.1 + reach(0.012, bb)) [loop] for (float row = 0.0; row < 2.0; row++) {
    float x = q.x + t * (0.06 + row * 0.03) + row * 7.3, cw = 0.045 - row * 0.012;
    float c0 = floor(x / cw);
    float cells=ceil(reach(.012,bb)/cw);
    [loop] for (float k = -cells; k <= cells; k++) {
      float n = c0 + k + row * 300.0;
      if (h1(n + 11.0) > 0.85) continue;
      float2 cen = vec2((c0 + k + 0.15 + 0.7 * h1(n)) * cw, HZ - 0.02 - 0.2 * pow(h1(n + 3.0), 1.6));
      float r = 0.004 + 0.008 * pow(h1(n + 6.0), 3.0);
      c += pick(h1(n + 9.0)) * glowPt(vec2(x, q.y) - cen, r, bb, 0.0) * (1.0 + 1.8 * h1(n + 13.0));
    }
  }
  float xb = q.x + t * 0.1, bw = 0.75, cb = floor(xb / bw);
  if (abs(q.y - (HZ - 0.14)) < 0.06 + reach(0.13, b)) [loop] for (float k = -1.0; k <= 1.0; k++) {
    float n = cb + k + 500.0;
    float2 d = vec2(xb - (cb + k + 0.2 + 0.6 * h1(n)) * bw, q.y - (HZ - 0.08 - 0.12 * h1(n + 2.0)));
    float3 lc = pick(h1(n + 4.0));
    c += lc * (glowPt(d, 0.014, bb, 0.0) * 2.0 + glowPt(d, 0.13, b, 0.0) * 0.4);
  }
  return c;
}
float beamFrom(float2 src, float2 q, float b) {
  float2 d = uSpotTgt - src;
  float L = length(d);
  if (uSpotI <= 0.0 || L < 1e-3) return 0.0;
  d /= L;
  float2 rel = q - src;
  float along = dot(rel, d);
  float perp = length(rel - d * clamp(along, 0.0, L));
  float k = clamp(along / L, 0.0, 1.0);
  float w = mix(0.004, uSpotR * 0.9, k) + b;
  float body = exp(-perp * perp / (w * w)) * smoothstep(-0.01, 0.04, along) * (0.25 + 0.75 * k * k);
  float lamp = exp(-dot(rel, rel) / (0.00004 + b * b * 0.25)) * 2.5 / (1.0 + b * 50.0);
  return (body * 0.7 + lamp) * uSpotI;
}
float spotBeam(float2 q, float b) { return beamFrom(uSpotSrc, q, b) + beamFrom(uSpotSrc2, q, b); }
float3 NebbiaNightScene(float2 q, float b) {
  float t = uTime * uBus;
  float3 col = mix(uSky0, uSky1, smoothstep(0.0, HZ, q.y));
  col += mix(uLa, SODIUM, 0.4) * 0.14 * exp(-abs(q.y - HZ + 0.12) * 6.0);
  float aa = max(b, 0.0015);
  if (sceneryOn(SCN_BUILDINGS)) {
    float4 farBuildings = building(q, b, t, 0.0);
    col = mix(col, farBuildings.rgb, farBuildings.a);
    float4 nearBuildings = building(q, b, t, 1.0);
    col = mix(col, nearBuildings.rgb, nearBuildings.a);
  }
  bool lightsOn = sceneryOn(SCN_LIGHTS);
  if (lightsOn) {
    col += (neonLights(q, b, t) + uLa * 0.12 * exp(-abs(q.y - HZ + 0.1) * 5.0)) * uNeon;
    col += lampsSigns(q, b, t);
  }
  float road = sceneryOn(SCN_ROAD) ? smoothstep(ROAD - aa, ROAD + aa, q.y) : 0.0;
  col = mix(col, uSky0 * 0.5, road * 0.85);
  if (road > 0.0 && lightsOn) {
    float2 qr = vec2(q.x, ROAD - (q.y - ROAD) * 0.55);
    col += lampsSigns(qr, b + 0.012, t) * road * 0.35;
    float2 qn = vec2(q.x, HZ - (q.y - ROAD) * 0.22);
    col += neonLights(qn, b + 0.008, t) * road * 0.45 * uNeon;
  }
  if (sceneryOn(SCN_CARS)) [loop] for (float lane = 0.0; lane < 2.0; lane++) {
    float sp = lane < 0.5 ? 0.55 : -1.1, ly = lane < 0.5 ? 0.76 : 0.83, ww = 0.9;
    float xc = q.x + t * sp, cc = floor(xc / ww);
    float3 lc = lane < 0.5 ? vec3(1.0, 0.18, 0.12) : vec3(1.0, 0.95, 0.85);
    bool hd = abs(q.y - ly) < reach(0.004, b), rf = road > 0.0 && abs((q.y - ly) * 3.0 - 0.03) < reach(0.02, b * 2.0);
    if (!hd && !rf) continue;
    [loop] for (float k = -1.0; k <= 1.0; k++) {
      float n = cc + k + lane * 50.0;
      if (h1(n) > 0.6) continue;
      float cx = (cc + k + 0.2 + 0.6 * h1(n + 4.0)) * ww;
      float st = abs(sp) * 0.03 * uBus;
      if (hd) {
        float headlights = glowPt(vec2(xc - cx + 0.035, q.y - ly), 0.004, b, st) + glowPt(vec2(xc - cx - 0.035, q.y - ly), 0.004, b, st);
        col += lc * headlights * 2.2;
      }
      if (rf) col += lc * glowPt(vec2(xc - cx, (q.y - ly) * 3.0 - 0.03), 0.02, b * 2.0, st) * road * 0.3;
    }
  }
  float beam = spotBeam(q, b);
  if (!sceneryOn(SCN_RAIN)) return col + uLa * beam;
  float2 rd = normalize(vec2(sceneryOn(SCN_RAIN_SLANT) ? -WIND * uBus : 0.0, 1.0));
  float2 r = vec2(dot(q, rd), dot(q, vec2(-rd.y, rd.x)));
  float colId = floor(r.y / 0.007), hr = h1(colId);
  float seg = fract(r.x * (1.5 + hr) - uTime * (2.5 + hr * 2.0) * (1.0 + 0.4 * uBus) + hr * 10.0);
  float rainLine = smoothstep(0.0012 + b, 0.0, abs(fract(r.y / 0.007) - 0.5) * 0.007) * smoothstep(0.0, 0.1, seg) * smoothstep(0.3, 0.1, seg);
  col += uLa * beam + mix(uLa, vec3(1.0), 0.5) * rainLine * beam * 1.6 * step(0.3, hr) * (1.0 - clamp(b * 40.0, 0.0, 1.0));
  return col;
}


// Initial aquarium shoal follows the source's spawn seed. Motion integrates its
// positive swimming stroke analytically from the ambient clock, including seeks.
static const float4 NebbiaJellySeed[16]={
    float4(0.621637778, 1.04282994, 0.3216039, 0.657739078),
    float4(0.308714858, 0.172316336, 0.767786834, 2.87507602),
    float4(0.373198323, -0.170072337, 0.718882772, 4.60462542),
    float4(0.89405238, 1.21683895, 0.469675521, 0.43127507),
    float4(0.976656185, 0.923626623, 0.284161591, 1.21728436),
    float4(0.872819247, 0.0474744223, 0.13085022, 4.04578418),
    float4(0.391072742, 1.17479054, 0.225131694, 4.89257057),
    float4(0.352822178, -0.0509326427, 0.720015327, 1.86624222),
    float4(0.668853426, 0.529042308, 0.305842919, 0.198532527),
    float4(0.12956364, -0.330583931, 0.776231672, 2.94232157),
    float4(0.141474172, 0.127801718, 0.468285022, 0.143513948),
    float4(0.762480957, 0.573117499, 0.761627384, 6.12376768),
    float4(0.0805185734, 0.289133274, 0.0649800933, 0.286780215),
    float4(0.669778564, 0.247922834, 0.123819935, 2.91888207),
    float4(0.277741137, -0.30310786, 0.33697761, 2.45955644),
    float4(0.511901499, 0.75574221, 0.329772044, 1.65352756)
};
static const float4 NebbiaJellyMotion[16]={
    float4(1.76896479, 0.0181555593, 0.633077179, 0),
    float4(2.12655881, 0.0173642208, 0.897349645, 0),
    float4(2.26091853, 0.0197737969, 0.990100267, 0),
    float4(1.69756532, 0.0217417533, 0.826489403, 0),
    float4(2.195622, 0.0155021168, 0.906359272, 0),
    float4(1.61057907, 0.0228905206, 0.936631099, 0),
    float4(2.44528513, 0.0165532004, 0.555579646, 0),
    float4(2.42174095, 0.0164636284, 0.075818046, 0),
    float4(2.02119528, 0.0229107772, 0.119731327, 0),
    float4(1.63588963, 0.0228825019, 0.529654916, 0),
    float4(2.30061644, 0.0172912071, 0.839399611, 0),
    float4(1.96656794, 0.0198415169, 0.02984307, 0),
    float4(1.60296599, 0.0128431465, 0.0117014261, 0),
    float4(1.85733304, 0.0173015279, 0.782387692, 0),
    float4(1.73605214, 0.0235280399, 0.805977245, 0),
    float4(2.05204582, 0.0192850987, 0.0332691919, 0)
};
float NebbiaSwimIntegral(float angle) {
    return 2*floor(angle/(2*3.14159265))+1-cos(min(3.14159265,frac(angle/(2*3.14159265))*2*3.14159265));
}
float4 NebbiaJellyState(int i,out float4 color) {
    float4 v=NebbiaJellySeed[i], movement=NebbiaJellyMotion[i];
    float t=uTime, phase=v.w+t*movement.x;
    float lift=(NebbiaSwimIntegral(phase)-NebbiaSwimIntegral(v.w))/movement.x;
    float y=v.y-movement.y*(t+.9*lift);
    y=1.35-frac((1.35-y)/1.7)*1.7;
    float hue=movement.z;
    float x=(v.x+.02*sin(t*.15+hue*10))*(Screen.x/Screen.y+.2)-.1;
    uint colorIndex=(uint)(hue*4)%4;
    float3 vivid=colorIndex==0?float3(1,.35,.72):colorIndex==1?float3(.35,.8,1):
        colorIndex==2?float3(.72,.5,1):float3(1,.72,.35);
    float3 colorValue=lerp(vivid,pick(frac(hue*3.7)),.35)*(.45+.55*v.z);
    color=float4(colorValue,phase);
    return float4(x,y,.03+.055*v.z,pow(max(sin(phase),0),3));
}
float3 jellies(float2 q, float b) {
  float3 c = vec3(0.0);
  float sharp = 1.0 - clamp(b * 25.0, 0.0, 1.0);
  bool fine = b < 0.004;
  [loop] for (int i = 0; i < 16; i++) {

    float4 shade; float4 J = NebbiaJellyState(i,shade);
    float size = J.z, pulse = J.w;
    float2 d = q - J.xy;
    if (abs(d.x) > size * 2.2 + b * 3.0 || d.y < -size * 1.6 - b * 3.0 || d.y > size * 5.0 + b * 3.0) continue;
    float3 jc = shade.rgb * uNeon;
    float ph = shade.a;
    float wx = size * (1.0 - 0.18 * pulse), hy = size * 0.72 * (1.0 + 0.14 * pulse);
    float2 p = vec2(d.x / wx, d.y / hy);
    float soft = b / size + 0.04;
    float edge = 0.05 + 0.3 * p.x * p.x + 0.04 * cos(p.x * 14.0 + ph * 0.5) * sharp;
    float r = max(length(vec2(p.x, min(p.y, 0.0))), (p.y - edge) / (0.3 + soft) + 1.0);
    float fill = smoothstep(1.0 + soft, 1.0 - soft, r);
    float rr = (r - 0.9) / (0.09 + soft);
    float rim = exp(-rr * rr);
    float2 pc = p + vec2(0.0, 0.4);
    float lobe = length(pc) - 0.26 - 0.07 * cos(4.0 * atan2(pc.y, pc.x));
    float gon = exp(-lobe * lobe / (0.004 + soft * soft)) * (0.4 + 0.6 * sharp);
    c += jc * (fill * (0.16 + 0.55 * rim + 0.45 * gon) + glowPt(d + vec2(0.0, size * 0.25), size * 0.9, b, 0.0) * 0.18);
    if (d.y <= 0.0) continue;
    float we = sqrt(pow(wx * 0.55, 2.0) + b * b);
    c += jc * exp(-d.x * d.x / (we * we)) * pow(max(1.0 - d.y / (size * 3.5), 0.0), 2.0) * 0.12 * smoothstep(0.0, size * 0.3, d.y);
    if (!fine) continue;
    float w = 0.0012 + size * 0.012;
    we = sqrt(w * w + b * b);
    [loop] for (float k = 0.0; k < 6.0; k++) {
      float len = size * (2.6 + 1.6 * h1((float)i * 13.0 + k));
      float s = d.y / len;
      if (s > 1.05) continue;
      float tx = (k / 5.0 - 0.5) * 1.5 * wx * (1.0 - 0.35 * min(s, 1.0));
      float dx = d.x - tx - sin(d.y / size * 2.6 - ph * 0.9 + k * 1.3) * size * 0.28 * min(s, 1.0);
      c += jc * (w / we) * exp(-dx * dx / (we * we)) * pow(max(1.0 - s, 0.0), 1.5) * smoothstep(hy * 0.1, hy * 0.35, d.y) * 0.8;
    }
    float s = d.y / (size * 1.7);
    if (s < 1.0) {
      float wa = size * 0.14 * (1.0 - s * 0.6), wae = sqrt(wa * wa + b * b);
      float dx = d.x - sin(d.y / size * 3.5 - ph + 1.0) * size * 0.12 * s;
      c += jc * (wa / wae) * exp(-dx * dx / (wae * wae)) * (1.0 - s) * 0.35 * smoothstep(0.0, 0.01, d.y);
    }
  }
  return c;
}

float4 aqBubble(float2 d, float rad, float b) {
  float l = length(d), aa = max(b, 0.0005);
  float keep = rad * rad / (rad * rad + b * b * 4.0);
  float disc = smoothstep(rad + aa, rad - aa, l);
  float up = 0.5 + 0.5 * clamp(-d.y / max(l, 1e-5), -1.0, 1.0);
  float fres = pow(clamp(l / rad, 0.0, 1.0), 5.0) * disc * (0.35 + 0.65 * up);
  float spec = glowPt(d + vec2(rad * 0.36, rad * 0.4), rad * 0.15, b, 0.0);
  float spec2 = glowPt(d - vec2(rad * 0.32, rad * 0.42), rad * 0.1, b, 0.0) * 0.3;
  float caus = glowPt(d - vec2(0.0, rad * 0.5), rad * 0.32, b, 0.0) * 0.14;
  float4 big = vec4(vec3(fres * 0.9 * keep + spec * 1.3 + spec2 + caus), disc * 0.14 * keep);
  float4 tiny = vec4(vec3(glowPt(d, rad * 0.6, b, 0.0) * 1.3 + spec * 1.2), 0.0);
  return mix(tiny, big, smoothstep(0.0018, 0.0034, rad));
}
float3 aqBubbles(float3 col, float2 q, float b, float3 bc) {
  float t = uTime;
  float drift = sceneryOn(SCN_RAIN_SLANT) ? 0.1 : 0.0;
  float fade = smoothstep(0.93, 0.8, q.y);
  float qx = q.x + (0.92 - q.y) * drift;
  if (sceneryOn(SCN_RAIN)) {
    float cw = 0.055, cx = floor(qx / cw);
    [loop] for (float k = -1.0; k <= 1.0; k++) {
      float n = cx + k;
      if (h1(n * 2.3 + 1.0) > 0.6 * 0.9) continue;
      float src = (n + 0.5) * cw + (h1(n + 3.0) - 0.5) * cw * 0.5;
      float sp = 0.14 + 0.1 * h1(n + 7.0), ch = 0.014 + 0.01 * h1(n + 5.0);
      float rise = clamp((0.92 - q.y) / 0.9, 0.0, 1.0);
      float sw = 0.003 + 0.016 * rise, swe = sqrt(sw * sw + b * b);
      float sx = qx - src;
      col += bc * exp(-sx * sx / (swe * swe)) * 0.03 * fade * (sw / swe + 0.3);
      if (b > 0.02) continue;
      float v = q.y + t * sp, iy = floor(v / ch);
      [loop] for (float j = -1.0; j <= 1.0; j++) {
        float m = iy + j, hb = h1(m * 1.31 + n * 17.0);
        if (hb > 0.85) continue;
        float y = (m + 0.2 + 0.6 * h1(m * 2.7 + n)) * ch - t * sp;
        float up = clamp((0.92 - y) / 0.9, 0.0, 1.0);
        float rad = 0.0012 + 0.003 * hb * hb * hb + 0.0012 * up;
        float2 c = vec2(src + sin(m * 2.1 + t * 6.0) * (0.0015 + 0.006 * up) + (h1(m + n) - 0.5) * (0.006 + 0.03 * up), y);
        float4 bub = aqBubble(vec2(qx, q.y) - c, rad, b);
        col = col * (1.0 - bub.a * fade) + bc * bub.rgb * fade * (0.6 + 0.4 * h1(m * 5.0 + n));
      }
    }
  }
  if (sceneryOn(SCN_CARS)) {
    float bw = 0.13, bx = floor(qx / bw);
    [loop] for (float k = -1.0; k <= 1.0; k++) {
      float n = bx + k;
      float sp = 0.07 + 0.04 * h1(n + 41.0), ch = 0.42;
      float iy = floor((q.y + t * sp) / ch);
      [loop] for (float j = -1.0; j <= 0.0; j++) {
        float m = iy + j;
        if (h1(m * 3.7 + n * 11.0) > min(1.1 * 0.6, 0.85)) continue;
        float rad = 0.008 + 0.016 * pow(h1(m + n * 5.0), 1.5);
        float2 c = vec2((n + 0.2 + 0.6 * h1(m * 7.0 + n)) * bw + sin(t * 1.3 + m) * 0.02, (m + 0.5) * ch - t * sp);
        float2 d = vec2(qx, q.y) - c;
        float2 u = d / max(length(d), 1e-5);
        float c2 = u.x * u.x - u.y * u.y, s2 = 2.0 * u.x * u.y;
        float c3 = u.x * (4.0 * u.x * u.x - 3.0), s3 = u.y * (3.0 - 4.0 * u.y * u.y);
        float f1 = t * 4.0 + m, f2 = t * 3.0;
        float shape = 1.0 + 0.03 * (s3 * cos(f1) + c3 * sin(f1)) + 0.02 * (s2 * cos(f2) - c2 * sin(f2));
        float4 bub = aqBubble(vec2(d.x, d.y * 1.18) / shape, rad, b);
        col = col * (1.0 - bub.a * fade) + bc * (bub.rgb + glowPt(d, rad * 1.4, b, 0.0) * 0.04) * fade;
      }
    }
  }
  return col;
}

float3 NebbiaAquarium(float2 uv,float b) {
  float2 q=uv*vec2(Screen.x/Screen.y,1.0);
  float t = uTime, sharp = 1.0 - clamp(b * 25.0, 0.0, 1.0);
  float3 deep = uSky0 * 0.55;
  float3 col = mix(mix(uSky1, uLa, 0.3) * 1.25 + 0.02, deep, smoothstep(0.0, 0.95, q.y));
  float3 lightC = mix(uLa, vec3(1.0), 0.45);
  float rx = q.x + q.y * 0.35;
  float rays = pow(0.5 + 0.5 * sin(rx * 9.0 + t * 0.3 + sin(rx * 3.1 - t * 0.2) * 1.5), 6.0)
             + 0.6 * pow(0.5 + 0.5 * sin(rx * 17.0 - t * 0.45), 8.0);
  col += lightC * mix(0.25, rays, sharp * 0.6 + 0.4) * (1.0 - smoothstep(0.0, 0.85, q.y)) * 0.13;
  col += lightC * 0.8 * exp(-q.y * 14.0) * (0.25 + 0.15 * sin(q.x * 30.0 + t * 1.3 + sin(q.x * 7.0)) * sharp);
  if (sceneryOn(SCN_ROAD)) {
    float floorY = 0.89 + 0.015 * sin(q.x * 3.0);
    float caus = pow(abs(sin(q.x * 23.0 + sin(q.y * 40.0 + t) * 1.2 + t * 0.6) * sin(q.x * 17.0 - t * 0.5 + q.y * 30.0)), 3.0);
    col = mix(col, deep * 1.3 + 0.03 + lightC * caus * 0.12 * sharp, smoothstep(floorY - 0.01 - b, floorY + 0.01 + b, q.y));
  }
  if (sceneryOn(SCN_BUILDINGS)) {
    float rockY = 0.8 + 0.07 * vnoise(vec2(q.x * 3.5, 1.0)) + 0.02 * vnoise(vec2(q.x * 14.0, 3.0));
    float under = max(q.y - rockY, 0.0);
    float tex = 0.75 + 0.5 * mix(0.5, vnoise(q * vec2(26.0, 34.0)) * 0.65 + vnoise(q * vec2(70.0, 90.0)) * 0.35, sharp);
    float3 rock = mix(deep * 1.6 + lightC * 0.07, deep * 0.75, smoothstep(0.0, 0.09, under)) * tex;
    float rc = pow(abs(sin(q.x * 31.0 + t * 0.5 + sin(q.y * 50.0 - t * 0.7)) * sin(q.x * 19.0 - t * 0.4 + q.y * 37.0)), 4.0);
    rock += lightC * (0.16 * exp(-under * 70.0) + rc * 0.07 * sharp * (1.0 - smoothstep(0.0, 0.05, under)));
    col = mix(col, rock, smoothstep(rockY - b - 0.003, rockY + b + 0.003, q.y) * 0.95);
    float rw = 0.17, rcx = floor(q.x / rw);
    [loop] for (float k = -1.0; k <= 1.0; k++) {
      float n = rcx + k, h = h1(n * 4.1 + 2.0);
      if (h > 0.7) continue;
      float rx = 0.06 + 0.05 * h1(n + 9.0), ry = rx * (0.55 + 0.3 * h1(n * 2.7));
      float2 c = vec2((n + 0.3 + 0.4 * h1(n * 5.3)) * rw, 0.95);
      float2 d = (q - c) / vec2(rx, ry);
      float edge = 0.12 * (vnoise(vec2(atan2(d.y, d.x) * 2.5, n * 3.0)) - 0.5);
      float e = length(d) + edge, soft = (b + 0.003) / ry;
      float cover = smoothstep(1.0 + soft, 1.0 - soft, e);
      if (cover <= 0.0) continue;
      float3 nrm = normalize(vec3(d * 0.9, sqrt(max(1.0 - dot(d, d), 0.04))));
      float lit = max(dot(nrm, normalize(vec3(-0.35, -1.0, 0.7))), 0.0);
      float3 stone = (deep * 1.8 + lightC * 0.08) * (0.4 + 1.0 * lit) * tex + lightC * pow(lit, 6.0) * 0.16 * sharp;
      col = mix(col, stone, cover * 0.97);
    }
  }
  bool weeds = sceneryOn(SCN_BUILDINGS), glow = sceneryOn(SCN_LIGHTS);
  if (weeds || glow) {
    float cw = 0.06, cx = floor(q.x / cw);
    [loop] for (float k = -1.0; k <= 1.0; k++) {
      float n = cx + k, h = h1(n * 1.7 + 5.0);
      if (h > 0.45) continue;
      float top = 0.55 + 0.25 * h1(n + 2.0), base = 0.92, len = base - top;
      float sway = t * (3.6 + 1.8 * h) + h * 20.0, bend = 0.07 * (0.7 + 0.6 * h);
      if (weeds) {
        float s = clamp((base - q.y) / len, 0.0, 1.0);
        float x0 = (n + 0.5) * cw + sin(q.y * 11.0 + sway) * 0.025 * 1.0 * s;
        float w = 0.003 * (1.0 - s * 0.6), we = sqrt(w * w + b * b), dx = q.x - x0;
        float a = (w / we) * exp(-dx * dx / (we * we)) * smoothstep(top - b, top + 0.02 + b, q.y) * step(q.y, base) * 0.5;
        col = mix(col, mix(deep, vec3(0.02, 0.08, 0.05), 0.6), a);
      }
      if (!glow || q.y < top - 0.05 - b * 3.0) continue;
      float s = clamp((base - q.y) / len, 0.0, 1.0);
      float N = 11.0, gap = len / (N - 1.0);
      if (b > 0.012) {
        float xs = (n + 0.5) * cw + sin(q.y * 11.0 + sway) * 0.025 * 1.0 * s, dx = q.x - xs;
        float fj = s * (N - 1.0), ja = floor(fj);
        float3 pc = mix(mix(pick(h1(n * 3.0 + ja)), pick(h1(n * 3.0 + ja + 1.0)), fj - ja), vec3(1.0), 0.25);
        float streakGlow = 0.0;
        [loop] for (float r3 = 0.0; r3 < 3.0; r3++) {
          float r = r3 < 0.5 ? 0.0018 + 0.0018 * s : r3 < 1.5 ? (0.0018 + 0.0018 * s) * 4.0 : 0.009 + 0.008 * s;
          float wgt = r3 < 0.5 ? 1.6 : r3 < 1.5 ? 0.25 : 0.4;
          float s2 = r * r + b * b;
          streakGlow += wgt * (r * r / s2) * exp(-dx * dx / s2 * 1.6) * sqrt(3.1416 * s2 / 1.6) / gap;
        }
        float e = 0.009 + b;
        streakGlow *= smoothstep(top - e, top + e, q.y) * smoothstep(base + e, base - e, q.y);
        col += pc * streakGlow * 0.7 * (0.35 + 0.65 * s) * uNeon;
        continue;
      }
      float j0 = floor(s * (N - 1.0) + 0.5);
      [loop] for (float dj = -2.0; dj <= 2.0; dj++) {
        float j = j0 + dj, sj = j / (N - 1.0);
        if (j < 0.0 || j > N - 1.0) continue;
        float py = base - sj * len;
        float2 pp = vec2((n + 0.5) * cw + sin(py * 11.0 + sway) * 0.025 * 1.0 * sj, py);
        float tw = 0.45 + 0.55 * pow(0.5 + 0.5 * sin(t * (2.5 + h1(n * 9.0 + j) * 3.0) + j * 1.7 + h * 30.0), 2.0);
        float pr = 0.0018 + 0.0018 * sj;
        float3 pc = mix(pick(h1(n * 3.0 + j)), vec3(1.0), 0.25);
        float2 d = q - pp;
        col += pc * (glowPt(d, pr, b, 0.0) * 1.6 + glowPt(d, pr * 4.0, b, 0.0) * 0.25 + glowPt(d, 0.009 + 0.008 * sj, b, 0.0) * 0.4) * tw * (0.35 + 0.65 * sj) * uNeon;
      }
    }
  }
  col += jellies(q, b);
  col = aqBubbles(col, q, b, mix(lightC, vec3(1.0), 0.4));
  col += lightC * spotBeam(q, b) * 0.8;
  return col;
}

float3 NebbiaOutside(float2 uv,uint setting,float blur) {
    if (setting==2)
        return max(0,LayerTexture.SampleLevel(LinearClamp,uv,0).rgb);
    [branch] if (setting==1)
        return NebbiaAquarium(uv,blur);
    return NebbiaNightScene(uv*float2(Screen.x/Screen.y,1),blur);
}

float4 NebbiaFogPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float2 sceneUv=float2(frac(uv.x*2),uv.y)-Jitter.xy/Screen.xy;
    uint behind=(uint)NativeLyric.w%3;
    float blur=uv.x<.5?.03:.09;
    return float4(NebbiaOutside(sceneUv,behind,blur),1);
}

// Lenses are sparse on the rain window and much smaller on the condensation field.
void NebbiaWater(float2 q,float t,uint glass,float clear,out float2 lensNormal,out float lensMask,out float snow) {
    float2 grid=q*(glass==1?100:14);
    float2 tile=floor(grid);
    float rnd=hash(tile+float2(731,173));
    float rnd2=hash(tile+float2(13,491));
    float2 center=.19+.62*float2(rnd,rnd2);
    float2 local=frac(grid)-center;
    float growth=saturate((t-rnd2*10)/6);
    float chance=glass==1?lerp(.006,.49,growth):.11;
    float radius=glass==1?.07+.13*rnd2:.034+.093*rnd2*rnd2;
    bool running=glass==0 && rnd>.98;
    if(running) local.y=frac(grid.y+.3+t*(.14+rnd2*.25))-.5;
    if(glass==1) radius*=lerp(.78,1,sqrt(growth));
    float2 drop=local/max(radius,.001);
    drop*=glass==1?float2(1.05,.96):float2(1.16,.85);
    float d=length(drop);
    float aa=clamp(fwidth(d),.03,.45);
    lensMask=(1-smoothstep(1-aa*.5,1+aa*.5,d))*step(1-chance,rnd);
    if(glass==1) lensMask*=1-clear;
    lensNormal=drop*.5;
    snow=0;
    if(glass==2) {
        float2 frost=q*110;
        float h=hash(floor(frost)+29);
        float2 grain=frac(frost)-(.18+.64*float2(h,hash(floor(frost)+57)));
        float region=vnoise(q*15+9);
        float ice=(1-smoothstep(.06,.22,length(grain)))*step(.64-region*.43,h);
        float2 flakes=q*17+float2(.33*sin(t*.4),-t*.75);
        float2 id=floor(flakes);
        float seed=hash(id+51);
        float2 fleck=(frac(flakes)-float2(.2+seed*.6,.25+.5*hash(id+735)))*float2(1.05,.8);
        float flurry=(1-smoothstep(.025,.12,length(fleck)))*step(.72,seed);
        float settle=smoothstep(0,12,t);
        snow=saturate(ice*.45*settle+flurry*.32*smoothstep(0,3,t))*(1-clear);
        lensMask*=.13+settle*.25;
    }
}

float4 NebbiaPS(float4 pixel:SV_Position,float2 uv:TEXCOORD0):SV_Target {
    float t=NativeComposition.z;
    uint setting=(uint)NativeLyric.w;
    uint glass=setting/3;
    uint behind=setting-glass*3;
    float2 stable=uv-Jitter.xy/Screen.xy;
    float2 px=stable*Screen.xy;
    float2 q=stable*float2(Screen.x/Screen.y,1);
    float fine=vnoise(px/6)*.6+vnoise(px/2.2)*.4;
    float blotch=vnoise(px/55+3.1);
    float4 ink=BackgroundHistoryTexture.SampleLevel(LinearClamp,uv,0);
    float2 r1=mul(float2x2(.8,.6,-.6,.8),px);
    float2 r2=mul(float2x2(.6,-.8,.8,.6),px);
    float bth=.3+(.55*vnoise(r1/34+7)+.3*vnoise(r2/15+2.3)+.15*vnoise(px/6.5)-.5)*.5;
    float breath=smoothstep(bth-.2,bth+.25,ink.b)*ink.a*.7;
    float mask=ink.r;
    float th=.3+(fine-.5)*.132+(blotch-.5)*.12;
    float clear=smoothstep(th-.05,th+.06,mask);
    float dens=.75*(.82+.3*vnoise(px/380+t*.015))*(.9+.2*stable.y)+breath*.9;
    float3 haze=float3(.70,.74,.78);
    float3 tint=dot(Palette[0].rgb,float3(.2126,.7152,.0722))>.5?float3(.86,.89,.92):float3(.07,.08,.10);
    float dark=1-dot(tint,1.0/3);
    float alpha=1;
    float3 fog;
    if(behind==2) {
        alpha=saturate(min(.92,(.62*dens+.3*breath)*(1+.3*dark)));
        fog=tint*alpha;
    } else {
        const float halfTexel=1/(4*max(1,ceil(Screen.x/4)));
        float texX=clamp(uv.x*.5,halfTexel,.5-halfTexel);
        float3 blur=BackdropTexture.SampleLevel(LinearClamp,float2(texX,uv.y),0).rgb;
        float3 glow=BackdropTexture.SampleLevel(LinearClamp,float2(texX+.5,uv.y),0).rgb;
        fog=blur*max(.08,1-.45*dens-.25*breath)+glow*.9*dens+haze*(.20*dens+.25*breath);
    }
    fog+=(hash(px)-.5)*.025*alpha+smoothstep(.78,.98,vnoise(px/1.6))*.05*dens;
    float2 spot=q-uSpotTgt;
    fog+=uLa*uSpotI*exp(-dot(spot,spot)/(max(.0004,uSpotR*uSpotR)*2.2))*(.18+.4*dens);

    float2 normalXY;
    float water, snow;
    NebbiaWater(q,t,glass,clear,normalXY,water,snow);
    bool dewField=glass==1 && water>ink.g;
    if(ink.g>water) {
        float2 dx=float2(2.0/Screen.x,0), dy=float2(0,2.0/Screen.y);
        normalXY=float2(
            BackgroundHistoryTexture.SampleLevel(LinearClamp,uv-dx,0).g-BackgroundHistoryTexture.SampleLevel(LinearClamp,uv+dx,0).g,
            BackgroundHistoryTexture.SampleLevel(LinearClamp,uv-dy,0).g-BackgroundHistoryTexture.SampleLevel(LinearClamp,uv+dy,0).g)*.5;
        water=ink.g;
    }
    float3 color=fog;
    bool useLens=water>.001;
    float2 throughUv=useLens?stable+float2(-normalXY.x,normalXY.y)*.12:stable;
    float3 through=0;
    [branch] if((clear>.001 && behind!=2) || useLens)
        through=NebbiaOutside(throughUv,behind==2 && useLens?0:behind,useLens?.006:.0012);
    if(clear>.001) {
        float3 wiped=(behind==2?0:through*1.05)+haze*.02*dens;
        float rim=4*clear*(1-clear)*.16*dens;
        color=lerp(fog,wiped,clear)+haze*rim;
        if(behind==2) alpha=lerp(alpha,.02*dens,clear)+rim;
    }
    if(useLens) {
        float3 lens=through;
        float3 normal=normalize(float3(normalXY*2,1));
        float3 lightDir=normalize(float3(-1,1,2));
        float lambert=saturate(dot(lightDir,normal));
        float glint=pow(saturate(dot(normal,normalize(lightDir+float3(0,0,1)))),48);
        lens+=(lambert-.8)*float3(.30,.30,.32)+glint*float3(.30,.30,.33);
        if(behind==2) {
            lens=max(lens,0)*(1-.53*dark);
            alpha=lerp(alpha,lerp(1,.47+.21*(1-water),dark),water);
        } else {
            lens=max(lens,fog*.55)+haze*.08*dens;
            if(dewField) lens=max(lens*1.15,fog*1.051)+(glint*.544+lambert*.068)*dens;
        }
        color=lerp(color,lens,water);
    }
    if(behind==2) color=max(color,0)+NebbiaOutside(stable,behind,0)*(1-saturate(alpha));
    color=lerp(color,float3(.82,.89,.96),snow);
    if(behind==1) {
        float peak=max(color.r,max(color.g,color.b));
        if(peak>.75) color*=(.75+.25*(1-exp((.75-peak)/.25)))/peak;
    }
    return float4(saturate(color),1);
}

#undef uTime
#undef uBus
#undef uNeon
#undef uLa
#undef uLb
#undef uLc
#undef uSky0
#undef uSky1
#undef uSpotTgt
#undef uSpotR
#undef uSpotI
#undef uSpotSourceY
#undef uSpotSrc
#undef uSpotSrc2
