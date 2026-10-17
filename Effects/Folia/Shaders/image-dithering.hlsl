// Generated from External/Folia/PaperShaders/image-dithering.glsl by port-paper.py.
#define u_resolution Screen.xy
#define u_pixelRatio 1.0
#define u_imageAspectRatio (max(TextureSize.z,1)/max(TextureSize.w,1))
#define u_time (Time.x)
#define u_originX .5
#define u_originY .5
#define u_worldWidth 0.0
#define u_worldHeight 0.0
#define u_fit 2.0
#define u_scale 1.0
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
#define u_pxSize 3.0
#define u_shape 2.0
#define u_type 4.0
#define u_originalColors false
#define u_inverted false
#define u_colorSteps 4.0

































  float hash21(float2 p) {
    p = fract(p * vec2(0.3183099, 0.3678794)) + 0.1;
    p += dot(p, p + 19.19);
    return fract(p.x * p.y);
  }


#define TWO_PI 6.28318530718
#define PI 3.14159265358979323846


float getUvFrame(float2 uv, float2 pad) {
  float aa = 0.0001;

  float left   = smoothstep(-pad.x, -pad.x + aa, uv.x);
  float right  = smoothstep(1.0 + pad.x, 1.0 + pad.x - aa, uv.x);
  float bottom = smoothstep(-pad.y, -pad.y + aa, uv.y);
  float top    = smoothstep(1.0 + pad.y, 1.0 + pad.y - aa, uv.y);

  return left * right * bottom * top;
}

float2 getImageUV(float2 uv) {
  float2 boxOrigin = vec2(.5 - u_originX, u_originY - .5);
  float r = u_rotation * PI / 180.;
  float2x2 graphicRotation = makeMat2(cos(r), sin(r), -sin(r), cos(r));
  float2 graphicOffset = vec2(-u_offsetX, u_offsetY);

  float2 imageBoxSize;
  if (u_fit == 1.) { // contain
    imageBoxSize.x = min(u_resolution.x / u_imageAspectRatio, u_resolution.y) * u_imageAspectRatio;
  } else if (u_fit == 2.) { // cover
    imageBoxSize.x = max(u_resolution.x / u_imageAspectRatio, u_resolution.y) * u_imageAspectRatio;
  } else {
    imageBoxSize.x = min(10.0, 10.0 / u_imageAspectRatio * u_imageAspectRatio);
  }
  imageBoxSize.y = imageBoxSize.x / u_imageAspectRatio;
  float2 imageBoxScale = u_resolution.xy / imageBoxSize;

  float2 imageUV = uv;
  imageUV *= imageBoxScale;
  imageUV += boxOrigin * (imageBoxScale - 1.);
  imageUV += graphicOffset;
  imageUV /= u_scale;
  imageUV.x *= u_imageAspectRatio;
  imageUV = mul(graphicRotation,imageUV);
  imageUV.x /= u_imageAspectRatio;

  imageUV += .5;
  imageUV.y = 1. - imageUV.y;

  return imageUV;
}

static const int bayer2x2[4] = {0, 2, 3, 1};
static const int bayer4x4[16] = {
0, 8, 2, 10,
12, 4, 14, 6,
3, 11, 1, 9,
15, 7, 13, 5
};

static const int bayer8x8[64] = {
0, 32, 8, 40, 2, 34, 10, 42,
48, 16, 56, 24, 50, 18, 58, 26,
12, 44, 4, 36, 14, 46, 6, 38,
60, 28, 52, 20, 62, 30, 54, 22,
3, 35, 11, 43, 1, 33, 9, 41,
51, 19, 59, 27, 49, 17, 57, 25,
15, 47, 7, 39, 13, 45, 5, 37,
63, 31, 55, 23, 61, 29, 53, 21
};

float getBayerValue(float2 uv, int size) {
  int2 pos = int2(fract(uv / float(size)) * float(size));
  int index = pos.y * size + pos.x;

  if (size == 2) {
    return float(bayer2x2[index]) / 4.0;
  } else if (size == 4) {
    return float(bayer4x4[index]) / 16.0;
  } else if (size == 8) {
    return float(bayer8x8[index]) / 64.0;
  }
  return 0.0;
}


float4 Paper(float2 inputUV, float4 coordinate) {
  float4 fragColor = 0;
  float4 gl_FragCoord = float4(coordinate.x, Screen.y-coordinate.y, coordinate.zw);
  float2 v_objectUV = (float2(inputUV.x,1-inputUV.y)-.5)*Screen.xy/max(Screen.x,Screen.y);
  float2 v_imageUV=ImageUV(inputUV, u_scale);

  float pxSize = u_pxSize * u_pixelRatio;
  float2 pxSizeUV = gl_FragCoord.xy - .5 * u_resolution;
  pxSizeUV /= pxSize;
  float2 canvasPixelizedUV = (floor(pxSizeUV) + .5) * pxSize;
  float2 normalizedUV = canvasPixelizedUV / u_resolution;

  float2 imageUV = getImageUV(normalizedUV);
  float2 ditheringNoiseUV = canvasPixelizedUV;
  float4 image = u_image.SampleLevel(LinearClamp, imageUV,0);
  float frame = getUvFrame(imageUV, pxSize / u_resolution);

  int type = int(floor(u_type));
  float dithering = 0.0;

  float lum = dot(vec3(.2126, .7152, .0722), image.rgb);
  lum = u_inverted ? (1. - lum) : lum;

  switch (type) {
    case 1: {
      dithering = step(hash21(ditheringNoiseUV), lum);
    } break;
    case 2:
    dithering = getBayerValue(pxSizeUV, 2);
    break;
    case 3:
    dithering = getBayerValue(pxSizeUV, 4);
    break;
    default :
    dithering = getBayerValue(pxSizeUV, 8);
    break;
  }

  float colorSteps = max(floor(u_colorSteps), 1.);
  float3 color = vec3(0.0);
  float opacity = 1.;

  dithering -= .5;
  float brightness = clamp(lum + dithering / colorSteps, 0.0, 1.0);
  brightness = mix(0.0, brightness, frame);
  brightness = mix(0.0, brightness, image.a);
  float quantLum = floor(brightness * colorSteps + 0.5) / colorSteps;
  quantLum = mix(0.0, quantLum, frame);

  if (u_originalColors == true) {
    float3 normColor = image.rgb / max(lum, 0.001);
    color = normColor * quantLum;

    float quantAlpha = floor(image.a * colorSteps + 0.5) / colorSteps;
    opacity = mix(quantLum, 1., quantAlpha);
  } else {
    float3 fgColor = u_colorFront.rgb * u_colorFront.a;
    float fgOpacity = u_colorFront.a;
    float3 bgColor = u_colorBack.rgb * u_colorBack.a;
    float bgOpacity = u_colorBack.a;
    float3 hlColor = u_colorHighlight.rgb * u_colorHighlight.a;
    float hlOpacity = u_colorHighlight.a;

    fgColor = mix(fgColor, hlColor, step(1.02 - .02 * u_colorSteps, brightness));
    fgOpacity = mix(fgOpacity, hlOpacity, step(1.02 - .02 * u_colorSteps, brightness));

    color = fgColor * quantLum;
    opacity = fgOpacity * quantLum;
    color += bgColor * (1.0 - opacity);
    opacity += bgOpacity * (1.0 - opacity);
  }

  fragColor = vec4(color, opacity);

return fragColor;
}

float4 PS(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target { float4 c=Paper(uv,p);c.rgb=lerp(c.rgb,Palette[0].rgb,.35);return float4(max(c.rgb,0),1);}
