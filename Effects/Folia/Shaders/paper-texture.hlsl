// Generated from External/Folia/PaperShaders/paper-texture.glsl by port-paper.py.
#define u_resolution Screen.xy
#define u_pixelRatio 1.0
#define u_imageAspectRatio (max(TextureSize.z,1)/max(TextureSize.w,1))
#define u_time (Time.x)
#define u_originX .5
#define u_originY .5
#define u_worldWidth 0.0
#define u_worldHeight 0.0
#define u_fit 2.0
#define u_scale 1.106972356
#define u_rotation 0.0
#define u_offsetX 0.0
#define u_offsetY 0.0
#define u_colors Palette
#define u_colorsCount 4.0
#define u_colorBack Palette[0]
#define u_colorFront Palette[3]
#define u_colorHighlight Palette[1]
#define u_colorShadow Palette[2]
#define u_image CoverTexture
#define u_noiseTexture NoiseTexture
#define u_distortion .8
#define u_swirl .1
#define u_grainMixer 0.0
#define u_grainOverlay .01
#define u_pxSize 2.5
#define u_shape 2.0
#define u_type 3.0
#define u_originalColors false
#define u_inverted false
#define u_colorSteps 4.0
#define u_contrast .32
#define u_roughness .42
#define u_fiber .3
#define u_fiberSize .2
#define u_crumples .35
#define u_crumpleSize .35
#define u_folds .65
#define u_foldCount 5.0
#define u_drops .2
#define u_seed 5.8
#define u_fade 0.0






























float getUvFrame(float2 uv) {
  float aax = 2. * fwidth(uv.x);
  float aay = 2. * fwidth(uv.y);

  float left   = smoothstep(0., aax, uv.x);
  float right = 1. - smoothstep(1. - aax, 1., uv.x);
  float bottom = smoothstep(0., aay, uv.y);
  float top = 1. - smoothstep(1. - aay, 1., uv.y);

  return left * right * bottom * top;
}


#define TWO_PI 6.28318530718
#define PI 3.14159265358979323846


float2 rotate(float2 uv, float th) {
  return vec2(cos(th)*uv.x-sin(th)*uv.y,sin(th)*uv.x+cos(th)*uv.y);
}


  float randomR(float2 p) {
    float2 uv = floor(p) / 100. + .5;
    return u_noiseTexture.SampleLevel(LinearClamp, fract(uv),0).r;
  }

float valueNoise(float2 st) {
  float2 i = floor(st);
  float2 f = fract(st);
  float a = randomR(i);
  float b = randomR(i + vec2(1.0, 0.0));
  float c = randomR(i + vec2(0.0, 1.0));
  float d = randomR(i + vec2(1.0, 1.0));
  float2 u = f * f * (3.0 - 2.0 * f);
  float x1 = mix(a, b, u.x);
  float x2 = mix(c, d, u.x);
  return mix(x1, x2, u.y);
}
float fbm(float2 n) {
  float total = 0.0, amplitude = .4;
  for (int i = 0; i < 3; i++) {
    total += valueNoise(n) * amplitude;
    n *= 1.99;
    amplitude *= 0.65;
  }
  return total;
}


float randomG(float2 p) {
  float2 uv = floor(p) / 50. + .5;
  return u_noiseTexture.SampleLevel(LinearClamp, fract(uv),0).g;
}
float roughness_fn(float2 p) {
  p *= .1;
  float o = 0.;
  for (float i = 0.; ++i < 4.; p *= 2.1) {
    float4 w = vec4(floor(p), ceil(p));
    float2 f = fract(p);
    o += mix(
    mix(randomG(w.xy), randomG(w.xw), f.y),
    mix(randomG(w.zy), randomG(w.zw), f.y),
    f.x);
    o += .2 / exp(2. * abs(sin(.2 * p.x + .5 * p.y)));
  }
  return o / 3.;
}


float fiberRandom(float2 p) {
  float2 uv = floor(p) / 100.;
  return u_noiseTexture.SampleLevel(LinearClamp, fract(uv),0).b;
}

float fiberValueNoise(float2 st) {
  float2 i = floor(st);
  float2 f = fract(st);
  float a = fiberRandom(i);
  float b = fiberRandom(i + vec2(1.0, 0.0));
  float c = fiberRandom(i + vec2(0.0, 1.0));
  float d = fiberRandom(i + vec2(1.0, 1.0));
  float2 u = f * f * (3.0 - 2.0 * f);
  float x1 = mix(a, b, u.x);
  float x2 = mix(c, d, u.x);
  return mix(x1, x2, u.y);
}

float fiberNoiseFbm(in float2 n, float2 seedOffset) {
  float total = 0.0, amplitude = 1.;
  for (int i = 0; i < 4; i++) {
    n = rotate(n, .7);
    total += fiberValueNoise(n + seedOffset) * amplitude;
    n *= 2.;
    amplitude *= 0.6;
  }
  return total;
}

float fiberNoise(float2 uv, float2 seedOffset) {
  float epsilon = 0.001;
  float n1 = fiberNoiseFbm(uv + vec2(epsilon, 0.0), seedOffset);
  float n2 = fiberNoiseFbm(uv - vec2(epsilon, 0.0), seedOffset);
  float n3 = fiberNoiseFbm(uv + vec2(0.0, epsilon), seedOffset);
  float n4 = fiberNoiseFbm(uv - vec2(0.0, epsilon), seedOffset);
  return length(vec2(n1 - n2, n3 - n4)) / (2.0 * epsilon);
}


float2 randomGB(float2 p) {
  float2 uv = floor(p) / 50. + .5;
  return u_noiseTexture.SampleLevel(LinearClamp, fract(uv),0).gb;
}
float crumpledNoise(float2 t, float pw) {
  float2 p = floor(t);
  float wsum = 0.;
  float cl = 0.;
  for (int y = -1; y < 2; y += 1) {
    for (int x = -1; x < 2; x += 1) {
      float2 b = vec2(float(x), float(y));
      float2 q = b + p;
      float2 q2 = q - floor(q / 8.) * 8.;
      float2 c = q + randomGB(q2);
      float2 r = c - t;
      float w = pow(smoothstep(0., 1., 1. - abs(r.x)), pw) * pow(smoothstep(0., 1., 1. - abs(r.y)), pw);
      cl += (.5 + .5 * sin((q2.x + q2.y * 5.) * 8.)) * w;
      wsum += w;
    }
  }
  return pow(wsum != 0.0 ? cl / wsum : 0.0, .5) * 2.;
}
float crumplesShape(float2 uv) {
  return crumpledNoise(uv * .25, 16.) * crumpledNoise(uv * .5, 2.);
}


float2 folds(float2 uv) {
  float3 pp = vec3(0.);
  float l = 9.;
  for (float i = 0.; i < 15.; i++) {
    if (i >= u_foldCount) break;
    float2 rand = randomGB(vec2(i, i * u_seed));
    float an = rand.x * TWO_PI;
    float2 p = vec2(cos(an), sin(an)) * rand.y;
    float dist = distance(uv, p);
    l = min(l, dist);

    if (l == dist) {
      pp.xy = (uv - p.xy);
      pp.z = dist;
    }
  }
  return mix(pp.xy, vec2(0.), pow(pp.z, .25));
}

float drops_fn(float2 uv) {
  float2 iDropsUV = floor(uv);
  float2 fDropsUV = fract(uv);
  float dropsMinDist = 1.;
  for (int j = -1; j <= 1; j++) {
    for (int i = -1; i <= 1; i++) {
      float2 neighbor = vec2(float(i), float(j));
      float2 offset = randomGB(iDropsUV + neighbor);
      offset = .5 + .5 * sin(10. * u_seed + TWO_PI * offset);
      float2 pos = neighbor + offset - fDropsUV;
      float dist = length(pos);
      dropsMinDist = min(dropsMinDist, dropsMinDist*dist);
    }
  }
  return 1. - smoothstep(.05, .09, pow(dropsMinDist, .5));
}

float lst(float edge0, float edge1, float x) {
  return clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
}

float4 Paper(float2 inputUV, float4 coordinate) {
  float4 fragColor = 0;
  float4 gl_FragCoord = float4(coordinate.x, Screen.y-coordinate.y, coordinate.zw);
  float2 v_objectUV = (float2(inputUV.x,1-inputUV.y)-.5)*Screen.xy/max(Screen.x,Screen.y);
  float2 v_imageUV=ImageUV(inputUV, u_scale);

  float2 imageUV = v_imageUV;
  float2 patternUV = v_imageUV - .5;
  patternUV = 5. * (patternUV * vec2(u_imageAspectRatio, 1.));

  float2 roughnessUv = 1.5 * (gl_FragCoord.xy - .5 * u_resolution) / u_pixelRatio;
  float roughness = roughness_fn(roughnessUv + vec2(1., 0.)) - roughness_fn(roughnessUv - vec2(1., 0.));

  float2 crumplesUV = fract(patternUV * .02 / u_crumpleSize - u_seed) * 32.;
  float crumples = u_crumples * (crumplesShape(crumplesUV + vec2(.05, 0.)) - crumplesShape(crumplesUV));

  float2 fiberUV = 2. / u_fiberSize * patternUV;
  float fiber = fiberNoise(fiberUV, vec2(0.));
  fiber = .5 * u_fiber * (fiber - 1.);

  float2 normal = vec2(0.);
  float2 normalImage = vec2(0.);

  float2 foldsUV = patternUV * .12;
  foldsUV = rotate(foldsUV, 4. * u_seed);
  float2 w = folds(foldsUV);
  foldsUV = rotate(foldsUV + .007 * cos(u_seed), .01 * sin(u_seed));
  float2 w2 = folds(foldsUV);

  float drops = u_drops * drops_fn(patternUV * 2.);

  float fade = u_fade * fbm(.17 * patternUV + 10. * u_seed);
  fade = clamp(8. * fade * fade * fade, 0., 1.);

  w = mix(w, vec2(0.), fade);
  w2 = mix(w2, vec2(0.), fade);
  crumples = mix(crumples, 0., fade);
  drops = mix(drops, 0., fade);
  fiber *= mix(1., .5, fade);
  roughness *= mix(1., .5, fade);

  normal.xy += u_folds * min(5. * u_contrast, 1.) * 4. * max(vec2(0.), w + w2);
  normalImage.xy += u_folds * 2. * w;

  normal.xy += crumples;
  normalImage.xy += 1.5 * crumples;

  normal.xy += 3. * drops;
  normalImage.xy += .2 * drops;

  normal.xy += u_roughness * 1.5 * roughness;
  normal.xy += fiber;

  normalImage += u_roughness * .75 * roughness;
  normalImage += .2 * fiber;

  float3 lightPos = vec3(1., 2., 1.);
  float res = dot(normalize(vec3(normal, 9.5 - 9. * pow(u_contrast, .1))), normalize(lightPos));

  float3 fgColor = u_colorFront.rgb * u_colorFront.a;
  float fgOpacity = u_colorFront.a;
  float3 bgColor = u_colorBack.rgb * u_colorBack.a;
  float bgOpacity = u_colorBack.a;

  imageUV += .02 * normalImage;
  float frame = getUvFrame(imageUV);
  float4 image = u_image.SampleLevel(LinearClamp, imageUV,0);
  image.rgb += .6 * pow(u_contrast, .4) * (res - .7);

  frame *= image.a;

  float3 color = fgColor * res;
  float opacity = fgOpacity * res;

  color += bgColor * (1. - opacity);
  opacity += bgOpacity * (1. - opacity);
  opacity = mix(opacity, 1., frame);

  color -= .007 * drops;

  color.rgb = mix(color, image.rgb, frame);

  fragColor = vec4(color, opacity);

return fragColor;
}

float4 PS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target { float4 c=Paper(uv,p);c.rgb=lerp(c.rgb,Palette[0].rgb,.35);return float4(max(c.rgb,0),1);}
