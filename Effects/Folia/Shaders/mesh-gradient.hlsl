// Generated from External/Folia/PaperShaders/mesh-gradient.glsl by port-paper.py.
#define u_resolution Screen.xy
#define u_pixelRatio 1.0
#define u_imageAspectRatio (max(TextureSize.z,1)/max(TextureSize.w,1))
#define u_time LatentFrame.x
#define u_originX .5
#define u_originY .5
#define u_worldWidth 0.0
#define u_worldHeight 0.0
#define u_fit 1.0
#define u_scale 1.0
#define u_rotation 0.0
#define u_offsetX 0.0
#define u_offsetY 0.0
#define u_colors BackgroundColors
#define u_colorsCount 6.0
#define u_colorBack Palette[0]
#define u_colorFront Palette[3]
#define u_colorHighlight Palette[1]
#define u_colorShadow Palette[2]
#define u_image CoverTexture
#define u_noiseTexture NoiseTexture
#define u_distortion LatentDistortion.x
#define u_swirl LatentDistortion.y
#define u_grainMixer 0.0
#define u_grainOverlay .01
#define u_pxSize 2.5
#define u_shape 2.0
#define u_type 3.0
#define u_originalColors false
#define u_inverted false
#define u_colorSteps 4.0

















#define TWO_PI 6.28318530718
#define PI 3.14159265358979323846


float2 rotate(float2 uv, float th) {
  return vec2(cos(th)*uv.x-sin(th)*uv.y,sin(th)*uv.x+cos(th)*uv.y);
}


  float hash21(float2 p) {
    p = fract(p * vec2(0.3183099, 0.3678794)) + 0.1;
    p += dot(p, p + 19.19);
    return fract(p.x * p.y);
  }


float valueNoise(float2 st) {
  float2 i = floor(st);
  float2 f = fract(st);
  float a = hash21(i);
  float b = hash21(i + vec2(1.0, 0.0));
  float c = hash21(i + vec2(0.0, 1.0));
  float d = hash21(i + vec2(1.0, 1.0));
  float2 u = f * f * (3.0 - 2.0 * f);
  float x1 = mix(a, b, u.x);
  float x2 = mix(c, d, u.x);
  return mix(x1, x2, u.y);
}

float noise(float2 n, float2 seedOffset) {
  return valueNoise(n + seedOffset);
}

float2 getPosition(int i, float t) {
  float a = float(i) * .37;
  float b = .6 + fract(float(i) / 3.) * .9;
  float c = .8 + fract(float(i + 1) / 4.);

  float x = sin(t * b + a);
  float y = cos(t * c + a * 1.5);

  return .5 + .5 * vec2(x, y);
}

float4 Paper(float2 inputUV, float4 coordinate) {
  float4 fragColor = 0;
  float4 gl_FragCoord = float4(coordinate.x, Screen.y-coordinate.y, coordinate.zw);
  float2 v_objectUV = (float2(inputUV.x,1-inputUV.y)-.5)*Screen.xy/min(Screen.x,Screen.y);
  float2 v_imageUV=ImageUV(inputUV, u_scale);
  float2 uv = v_objectUV;
  uv += .5;
  float2 grainUV = uv * 1000.;

  float grain = noise(grainUV, vec2(0.));
  float mixerGrain = .4 * u_grainMixer * (grain - .5);

  const float firstFrameOffset = 41.5;
  float t = .5 * (u_time + firstFrameOffset);

  float radius = smoothstep(0., 1., length(uv - .5));
  float center = 1. - radius;
  for (float i = 1.; i <= 2.; i++) {
    uv.x += u_distortion * center / i * sin(t + i * .4 * smoothstep(.0, 1., uv.y)) * cos(.2 * t + i * 2.4 * smoothstep(.0, 1., uv.y));
    uv.y += u_distortion * center / i * cos(t + i * 2. * smoothstep(.0, 1., uv.x));
  }

  float2 uvRotated = uv;
  uvRotated -= vec2(.5);
  float angle = 3. * u_swirl * radius;
  uvRotated = rotate(uvRotated, -angle);
  uvRotated += vec2(.5);

  float3 color = vec3(0.);
  float opacity = 0.;
  float totalWeight = 0.;

  for (int i = 0; i < 10; i++) {
    if (i >= int(u_colorsCount)) break;

    float2 pos = getPosition(i, t) + mixerGrain;
    float3 colorFraction = u_colors[i].rgb * u_colors[i].a;
    float opacityFraction = u_colors[i].a;

    float dist = length(uvRotated - pos);

    dist = pow(dist, 3.5);
    float weight = 1. / (dist + 1e-3);
    color += colorFraction * weight;
    opacity += opacityFraction * weight;
    totalWeight += weight;
  }

  color /= max(1e-4, totalWeight);
  opacity /= max(1e-4, totalWeight);

  float grainOverlay = valueNoise(rotate(grainUV, 1.) + vec2(3.));
  grainOverlay = mix(grainOverlay, valueNoise(rotate(grainUV, 2.) + vec2(-1.)), .5);
  grainOverlay = pow(grainOverlay, 1.3);

  float grainOverlayV = grainOverlay * 2. - 1.;
  float3 grainOverlayColor = vec3(step(0., grainOverlayV));
  float grainOverlayStrength = u_grainOverlay * abs(grainOverlayV);
  grainOverlayStrength = pow(grainOverlayStrength, .8);
  color = mix(color, grainOverlayColor, .35 * grainOverlayStrength);

  opacity += .5 * grainOverlayStrength;
  opacity = clamp(opacity, 0., 1.);

  fragColor = vec4(color, opacity);

return fragColor;
}

float4 PS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target { float4 c=Paper(uv,p);return float4(max(c.rgb,0),1);}
