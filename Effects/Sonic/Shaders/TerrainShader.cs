using ComputeSharp;
using ComputeSharp.D2D1;

namespace WinExSpectrumTest.Effects.Sonic.Shaders
{
    /// <summary>
    /// Full-screen shading pass for the ported "Sonic Topography" terrain.
    /// Performs an exact 2D voxel DDA over the bar grid, intersecting each ray
    /// against the per-cell bar boxes described by the heightfield texture
    /// (HeightFieldShader output), then applies the original fragment shader
    /// look: theme glow by elevation, warm/cool timbre blend, ripple and peak
    /// overrides, top-face edge glow and sparkle, aerial fog and distance fade.
    /// Output is premultiplied so the result composites over the window backdrop.
    /// </summary>
    [D2DInputCount(1)]
    [D2DInputComplex(0)]
    [D2DRequiresScenePosition]
    [D2DShaderProfile(D2D1ShaderProfile.PixelShader50)]
    // The DDA loop samples the heightfield texture; FXC raises X3570 (gradients in
    // varying flow) for that. The samples always hit exact texel centers of a
    // mip-free texture, so the undefined derivatives are irrelevant - keep them as
    // warnings instead of escalating to errors.
    [D2DCompileOptions(D2D1CompileOptions.EnableStrictness | D2D1CompileOptions.OptimizationLevel3)]
    [D2DGeneratedPixelShaderDescriptor]
    public readonly partial struct TerrainShader(
        float3 cameraPosition,
        float3 cameraRight,
        float3 cameraUp,
        float3 cameraForward,
        float tanHalfFovY,
        float2 targetSize,
        float cellSize,
        float halfExtent,
        float barSize,
        float gridTexel,
        float3 baseColor1,
        float3 baseColor2,
        float3 coolCore,
        float3 coolEdge,
        float3 warmCore,
        float3 warmEdge,
        float3 rippleColor,
        float3 peakColor,
        float time,
        float warmth,
        float brightness,
        float sharpness,
        float presence,
        float brilliance,
        float air,
        float glowIntensity,
        float peakEnabled,
        float peakIntensity) : ID2D1PixelShader
    {
        private const float MaxTerrainHeight = 30f;
        // FXC must fully unroll the DDA loop (gradient sample inside), so keep the
        // iteration count small. Rays that would traverse more cells than this are
        // in the >78 distance band that the aerial-fog alpha already fades out.
        private const int MaxCellSteps = 96;

        public float4 Execute()
        {
            float2 pixel = D2D.GetScenePosition().XY;
            float2 uv01 = pixel / targetSize;
            float2 ndc = uv01 * 2f - 1f;
            ndc.Y = -ndc.Y;
            float aspect = targetSize.X / targetSize.Y;

            float3 rd = Hlsl.Normalize(
                cameraForward
                + cameraRight * (ndc.X * tanHalfFovY * aspect)
                + cameraUp * (ndc.Y * tanHalfFovY));

            float gridSize = 1f / gridTexel;

            // Intersect the terrain AABB [-H, H] x [0, maxH] x [-H, H].
            float3 invRd = new(
                Hlsl.Abs(rd.X) < 1e-8f ? 1e8f * (rd.X >= 0f ? 1f : -1f) : 1f / rd.X,
                Hlsl.Abs(rd.Y) < 1e-8f ? 1e8f * (rd.Y >= 0f ? 1f : -1f) : 1f / rd.Y,
                Hlsl.Abs(rd.Z) < 1e-8f ? 1e8f * (rd.Z >= 0f ? 1f : -1f) : 1f / rd.Z);

            float3 boxMin = new(-halfExtent, 0f, -halfExtent);
            float3 boxMax = new(halfExtent, MaxTerrainHeight, halfExtent);
            float3 t0 = (boxMin - cameraPosition) * invRd;
            float3 t1 = (boxMax - cameraPosition) * invRd;
            float3 tMin3 = Hlsl.Min(t0, t1);
            float3 tMax3 = Hlsl.Max(t0, t1);
            float tEnter = Hlsl.Max(Hlsl.Max(tMin3.X, tMin3.Y), tMin3.Z);
            float tExit = Hlsl.Min(Hlsl.Min(tMax3.X, tMax3.Y), tMax3.Z);

            if (tEnter > tExit || tExit < 0f)
            {
                return Background();
            }

            float t = Hlsl.Max(tEnter, 0f);
            float2 planePos = cameraPosition.XZ + rd.XZ * t;

            float2 gridPos = Hlsl.Clamp((planePos + halfExtent) / cellSize, new float2(0f, 0f), new float2(gridSize - 0.001f, gridSize - 0.001f));
            int2 cell = (int2)Hlsl.Floor(gridPos);

            float stepX = rd.X > 0f ? 1f : -1f;
            float stepZ = rd.Z > 0f ? 1f : -1f;
            float tDeltaX = Hlsl.Abs(cellSize * invRd.X);
            float tDeltaZ = Hlsl.Abs(cellSize * invRd.Z);
            float boundaryX = -halfExtent + (cell.X + (stepX > 0f ? 1f : 0f)) * cellSize;
            float boundaryZ = -halfExtent + (cell.Y + (stepZ > 0f ? 1f : 0f)) * cellSize;
            float tMaxX = (boundaryX - cameraPosition.X) * invRd.X;
            float tMaxZ = (boundaryZ - cameraPosition.Z) * invRd.Z;
            if (Hlsl.Abs(rd.X) < 1e-8f) tMaxX = 1e9f;
            if (Hlsl.Abs(rd.Z) < 1e-8f) tMaxZ = 1e9f;

            float pad = (cellSize - barSize) * 0.5f;
            int gridInt = (int)Hlsl.Round(gridSize);

            // Fixed-iteration masked DDA: ps_4_0 forbids gradient instructions
            // (the bilinear input sample) inside varying control flow, so the
            // sample runs unconditionally every iteration and hit/dead state is
            // carried in flags instead of break/return. Shading happens after
            // the loop to keep the unrolled body small.
            float4 hitColor = Background();
            bool hit = false;
            bool dead = false;
            float3 hitNormal = new float3(0f, 1f, 0f);
            float3 hitPos = new float3(0f, 0f, 0f);
            float hitHeight = 0f;
            float4 hitField = new float4(0f, 0f, 0f, 0f);
            float2 hitCellCenter = new float2(0f, 0f);

            for (int i = 0; i < MaxCellSteps; i++)
            {
                // Sample the heightfield at the cell center. D2DSampleInput takes a
                // normalized uv, so this is resolution/DPI independent.
                float2 fieldUv = ((float2)cell + 0.5f) * gridTexel;
                fieldUv = Hlsl.Clamp(fieldUv, new float2(gridTexel * 0.5f, gridTexel * 0.5f), new float2(1f - gridTexel * 0.5f, 1f - gridTexel * 0.5f));
                float4 field = D2D.SampleInput(0, fieldUv);

                float2 cellMin = new float2(-halfExtent + cell.X * cellSize, -halfExtent + cell.Y * cellSize);
                float3 barMin = new float3(cellMin.X + pad, 0f, cellMin.Y + pad);
                float3 barMax = new float3(cellMin.X + pad + barSize, field.X, cellMin.Y + pad + barSize);

                float3 tt0 = (barMin - cameraPosition) * invRd;
                float3 tt1 = (barMax - cameraPosition) * invRd;
                float3 tn = Hlsl.Min(tt0, tt1);
                float3 tf = Hlsl.Max(tt0, tt1);
                float tNear = Hlsl.Max(Hlsl.Max(tn.X, tn.Y), tn.Z);
                float tFar = Hlsl.Min(Hlsl.Min(tf.X, tf.Y), tf.Z);
                float segmentEnd = Hlsl.Min(Hlsl.Min(tMaxX, tMaxZ), tExit);

                bool hitThis = !dead && !hit && field.X > 0.02f && tNear <= tFar && tNear < segmentEnd && tFar >= t;
                if (hitThis)
                {
                    float hitT = Hlsl.Max(tNear, t);
                    hitPos = cameraPosition + rd * hitT;

                    // Entry face normal from the dominant slab.
                    if (tn.Y >= tn.X && tn.Y >= tn.Z) hitNormal = new float3(0f, 1f, 0f);
                    else if (tn.X >= tn.Z) hitNormal = new float3(-stepX, 0f, 0f);
                    else hitNormal = new float3(0f, 0f, -stepZ);

                    hitCellCenter = cellMin + cellSize * 0.5f;
                    hitHeight = field.X;
                    hitField = field;
                    hit = true;
                }

                // Advance to the next cell boundary (masked once dead or hit).
                float nextT = Hlsl.Min(tMaxX, tMaxZ);
                if (!dead && !hit)
                {
                    if (tMaxX < tMaxZ)
                    {
                        cell.X += (int)stepX;
                        tMaxX += tDeltaX;
                    }
                    else
                    {
                        cell.Y += (int)stepZ;
                        tMaxZ += tDeltaZ;
                    }
                    dead = nextT > tExit || cell.X < 0 || cell.X >= gridInt || cell.Y < 0 || cell.Y >= gridInt;
                }
                t = Hlsl.Max(t, nextT);
                cell.X = Hlsl.Clamp(cell.X, 0, gridInt - 1);
                cell.Y = Hlsl.Clamp(cell.Y, 0, gridInt - 1);
            }

            if (hit)
            {
                float rnd = Random(hitCellCenter);
                float3 barMin = new float3(hitCellCenter.X - barSize * 0.5f, 0f, hitCellCenter.Y - barSize * 0.5f);
                hitColor = Shade(hitPos, hitNormal, hitHeight, hitField.Y, hitField.Z, hitField.W, hitCellCenter, rnd, barMin);
            }

            return hit ? hitColor : Background();
        }

        private static float4 Background()
        {
            // Fully transparent sky: the window backdrop shows through.
            return new float4(0f, 0f, 0f, 0f);
        }

        private static float Random(float2 st)
        {
            return Hlsl.Frac(Hlsl.Sin(Hlsl.Dot(st, new float2(12.9898f, 78.233f))) * 43758.5453123f);
        }

        private float4 Shade(
            float3 hit,
            float3 normal,
            float height,
            float rippleNormal,
            float rippleWhite,
            float peak,
            float2 cellCenter,
            float rnd,
            float3 barMin)
        {
            bool isTop = normal.Y > 0.5f;
            float relativeY = Hlsl.Clamp(hit.Y / height, 0f, 1f);
            float distFromTop = 1f - relativeY;
            float centerDist = Hlsl.Length(cellCenter);
            float normElevation = Hlsl.Clamp((height - 1f) / 8f, 0f, 1f);

            // Timbre-driven palette blend.
            float warmBlend = Hlsl.SmoothStep(0f, 1f, warmth * 1.5f + (0.5f - centerDist / 80f));
            float3 zoneCore = Hlsl.Lerp(coolCore, warmCore, warmBlend);
            float3 zoneEdge = Hlsl.Lerp(coolEdge, warmEdge, warmBlend);
            float3 targetGlow = Hlsl.Lerp(zoneCore, zoneEdge, Hlsl.Frac(rnd * 11f));
            float distFade = 1f - Hlsl.SmoothStep(40f, 75f, centerDist);
            targetGlow = Hlsl.Lerp(targetGlow, new float3(0.4f, 0.8f, 1f), brightness * 0.6f);

            float3 currentGlow = Hlsl.Lerp(baseColor2, targetGlow, normElevation) * glowIntensity * distFade;

            float normalBlend = rippleNormal * rippleNormal;
            float whiteBlend = rippleWhite * rippleWhite;
            currentGlow = Hlsl.Lerp(currentGlow, rippleColor, normalBlend * 0.85f);
            currentGlow = Hlsl.Lerp(currentGlow, new float3(1f, 1f, 1f), whiteBlend * 0.9f);

            float peakBlend = Hlsl.Pow(peak, 0.85f) * peakEnabled * peakIntensity;
            currentGlow = Hlsl.Lerp(currentGlow, peakColor, Hlsl.Clamp(peakBlend, 0f, 1f) * 0.7f);

            float3 bodyColor = Hlsl.Lerp(baseColor1, baseColor2, relativeY * distFade);

            float3 finalColor;

            if (isTop)
            {
                float topIntensity = Hlsl.SmoothStep(0f, 0.4f, normElevation);
                topIntensity += Hlsl.Clamp(peakBlend * 0.4f, 0f, 1f);

                float twinkleDistFalloff = Hlsl.SmoothStep(60f, 30f, centerDist);
                float twinkleMultiplier = Hlsl.Lerp(twinkleDistFalloff, 1f, Hlsl.SmoothStep(0.01f, 0.1f, normElevation));

                if (Hlsl.Frac(rnd * 31f) > 0.95f && normElevation < 0.1f)
                {
                    topIntensity += air * 2f * twinkleMultiplier;
                }

                finalColor = Hlsl.Lerp(baseColor2, currentGlow, topIntensity);

                // Top-face edge glow (uv within the bar footprint).
                float2 faceUv = (hit.XZ - barMin.XZ) / barSize;
                float edgeX = Hlsl.SmoothStep(0.05f, 0.01f, faceUv.X) + Hlsl.SmoothStep(0.95f, 0.99f, faceUv.X);
                float edgeY = Hlsl.SmoothStep(0.05f, 0.01f, faceUv.Y) + Hlsl.SmoothStep(0.95f, 0.99f, faceUv.Y);
                float edge = Hlsl.Min(edgeX + edgeY, 1f);
                finalColor += currentGlow * edge * 0.8f * (topIntensity + 0.3f);

                // Presence flickers.
                float flashChance = Hlsl.SmoothStep(0.5f, 1f, presence);
                if (Hlsl.Frac(rnd * 53f) > 0.985f - flashChance * 0.05f)
                {
                    float flashSync = Hlsl.Sin(time * 8f + rnd * 50f) * 0.5f + 0.5f;
                    finalColor += Hlsl.Lerp(new float3(1f, 1f, 1f), new float3(0.5f, 1f, 1f), rnd)
                        * flashSync * presence * (1f + sharpness * 1.5f) * twinkleMultiplier;
                }

                // Brilliance micro-sparks on edges.
                float brilliancePhase = Hlsl.Sin(time * 1.5f + rnd * 30f) * 0.5f + 0.5f;
                if (edge > 0.6f && Hlsl.Frac(rnd * 89f) > 0.992f && brilliancePhase > 0.7f)
                {
                    finalColor += new float3(1f, 1f, 1f) * brilliance * 2f * twinkleMultiplier * brilliancePhase;
                }
            }
            else
            {
                float verticalFalloff = Hlsl.Lerp(1f, 3f, sharpness);
                float sideGlow = Hlsl.SmoothStep(0.5f / verticalFalloff, 0f, distFromTop) * normElevation;
                if (normElevation < 0.02f) sideGlow = 0f;

                float3 sideGlowColor = Hlsl.Lerp(currentGlow, peakColor, Hlsl.Clamp(peakBlend * 0.4f, 0f, 1f));
                finalColor = Hlsl.Lerp(bodyColor, sideGlowColor, sideGlow * 1.5f);

                float rimGlow = Hlsl.SmoothStep(0.03f, 0f, distFromTop) * normElevation;
                finalColor += Hlsl.Lerp(currentGlow, peakColor, Hlsl.Clamp(peakBlend * 0.35f, 0f, 1f)) * rimGlow;
            }

            finalColor = Hlsl.Lerp(finalColor, peakColor, Hlsl.Clamp(peakBlend, 0f, 1f) * 0.15f);
            finalColor += rippleColor * normalBlend * 0.4f;
            finalColor += new float3(1f, 1f, 1f) * whiteBlend * 0.7f;

            // Aerial perspective and distance fade.
            float aerialFog = Hlsl.SmoothStep(30f, 65f, centerDist);
            float3 atmosphericColor = Hlsl.Lerp(baseColor1, baseColor2, 0.4f);
            finalColor = Hlsl.Lerp(finalColor, atmosphericColor, aerialFog * 0.5f);

            float alphaFade = 1f - Hlsl.SmoothStep(55f, 78f, centerDist);
            finalColor *= alphaFade; // premultiply for direct compositing

            return new float4(finalColor, alphaFade);
        }
    }
}
