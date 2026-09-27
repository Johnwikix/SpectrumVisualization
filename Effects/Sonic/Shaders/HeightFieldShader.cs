using ComputeSharp;

namespace WinExSpectrumTest.Effects.Sonic.Shaders
{
    /// <summary>
    /// One texel per bar cell of the ported "Sonic Topography" terrain.
    /// Reproduces the original vertex-shader elevation model: frequency-region
    /// lifts (sub-bass center, chunky bass, flowing low-mid, mid "river",
    /// scattered high-mid spikes), ambient idle waves and expanding beat ripples.
    ///
    /// Output channels:
    ///   R = bar height (world units, base block included)
    ///   G = squared ripple intensity (normal ripples)
    ///   B = squared ripple intensity (meteor/white ripples)
    ///   A = center peak intensity (sub-bass based, drives the peak color)
    /// Ripple encoding: one float4 per ripple = (x, z, startTime, strength),
    /// where a negative strength marks a meteor-type (white, sharper) ripple and
    /// zero marks an inactive slot.
    /// </summary>
    [ThreadGroupSize(DefaultThreadGroupSizes.XY)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct HeightFieldShader(
        ReadOnlyBuffer<float4> data,
        ReadWriteTexture2D<float4> target,
        float time,
        float subBass,
        float bass,
        float lowMid,
        float mid,
        float highMid,
        float energy,
        float idleWave,
        float audioIntensity,
        float responseRange,
        float halfExtent,
        float cellSize,
        float smoothness,
        float density) : IComputeShader
    {
        public void Execute()
        {
            float2 texel = ThreadIds.XY;
            float2 pos2D = (texel + 0.5f) * cellSize - halfExtent;
            float centerDist = Hlsl.Length(pos2D);
            float rnd = Random(pos2D);
            float range = responseRange;

            // 1. Idle background state (smooth, ocean-like).
            float2 movingPos = pos2D * 0.05f + new float2(time * 0.1f, time * 0.05f);
            float baseNoise = (Snoise(movingPos) + 1f) * 0.5f;
            float wave = Hlsl.Sin(pos2D.X * 0.15f + pos2D.Y * 0.1f - time * 0.6f) * 0.5f + 0.5f;
            float globalFalloff = Hlsl.SmoothStep(halfExtent * 0.71f * range, halfExtent * 0.36f * range, centerDist);
            float idleElevation = Hlsl.Lerp(baseNoise, wave, smoothness * 0.5f + 0.2f) * 0.8f * globalFalloff;

            // 2. Frequency regions with eased animation curves.
            float subRegion = Hlsl.SmoothStep(halfExtent * 0.30f * range, 0f, centerDist);
            float subLift = EaseLift(subBass, 6f) * subRegion;
            float peakIntensity = Hlsl.Clamp(subLift / 6f, 0f, 1f);

            float bassNoise = Snoise(pos2D * 0.1f - new float2(0f, time * 0.2f));
            float bassRegion = Hlsl.SmoothStep(halfExtent * 0.42f * range, halfExtent * 0.06f * range, centerDist + bassNoise * 5f);
            float bassRnd = Hlsl.SmoothStep(0f, 1f, rnd + density * 0.5f);
            float bassLift = EaseLift(bass, 5f) * bassRegion * bassRnd;

            float lowMidNoise = Snoise(pos2D * 0.05f + new float2(time * 0.1f, 0f));
            float lowMidLift = FlowLift(lowMid, 3f) * (lowMidNoise * 0.5f + 0.5f);

            float riverFlow = Hlsl.Sin(pos2D.X * 0.2f + pos2D.Y * 0.2f + Snoise(pos2D * 0.1f) * 2f - time * 2f);
            float midLift = FlowLift(mid, 4f) * Hlsl.Max(0f, riverFlow);

            float highMidRegion = Hlsl.SmoothStep(halfExtent * 0.12f * range, halfExtent * 0.54f * range, centerDist);
            float highMidLift = 0f;
            if (Hlsl.Frac(rnd * 13.3f) > 0.8f)
            {
                highMidLift = EaseLift(highMid, 3f) * highMidRegion * Hlsl.Frac(rnd * 7.7f);
            }

            float audioElevation = (subLift + bassLift + lowMidLift + midLift + highMidLift) * audioIntensity;

            // Energy spike with elastic bounce on rare cells.
            if (rnd > 0.99f)
            {
                float energyRaw = Hlsl.Clamp(energy, 0f, 1f);
                float energyBounce = 1f - Hlsl.Pow(1f - energyRaw, 1.5f);
                energyBounce += Hlsl.Sin(energyRaw * 6.283f * 2f) * Hlsl.Exp(-energyRaw * 5f) * 0.2f;
                audioElevation += energyBounce * 6f * audioIntensity;
            }

            audioElevation *= globalFalloff;

            // Ambient background waves - always present as base layer.
            float hillNoise = Snoise(pos2D * 0.08f + new float2(time * 0.12f, 0f));
            float hillNoise2 = Snoise(pos2D * 0.06f + new float2(0f, time * 0.08f));
            float rippleNoise = Snoise(pos2D * 0.15f + new float2(time * 0.2f, time * 0.15f));
            float textureNoise = Snoise(pos2D * 0.4f + new float2(time * 0.3f, time * 0.3f)) * 0.3f;

            float baseUndulation = (hillNoise * 0.6f + hillNoise2 * 0.4f) * 0.5f + 0.5f;
            float rippleUndulation = rippleNoise * 0.3f + 0.5f;
            float blockVariation = (rnd - 0.5f) * 0.15f;
            float combinedWave = baseUndulation * 0.5f + rippleUndulation * 0.35f + textureNoise + blockVariation;
            combinedWave = Hlsl.SmoothStep(0.1f, 0.9f, combinedWave);
            float idleBlockWave = combinedWave * idleWave * 2.5f * globalFalloff;

            float elevation = idleElevation + audioElevation + idleBlockWave;

            // 3. Expanding ripples.
            float rippleElevation = 0f;
            float rippleIntensityNormal = 0f;
            float rippleIntensityWhite = 0f;

            for (int i = 0; i < 12; i++)
            {
                float4 ripple = data[i];
                float signedStrength = ripple.W;
                if (signedStrength == 0f) continue;

                bool isMeteor = signedStrength < 0f;
                float strength = Hlsl.Abs(signedStrength);
                float dist = Hlsl.Length(pos2D - ripple.XY);
                float timeSince = time - ripple.Z;
                if (timeSince < 0f) continue;

                float curSpeed = isMeteor ? 18f : 14f;
                float curWidth = isMeteor ? 2.5f : 5f;
                float curFadeDist = isMeteor ? 18f : 22f;
                float elevationScale = isMeteor ? 1.8f : 3f;

                float waveRadius = timeSince * curSpeed;
                float d = dist - waveRadius;
                float rippleWave = Hlsl.Exp(-d * d / curWidth);
                float fade = Hlsl.Exp(-waveRadius / curFadeDist);
                float strengthCurve = Hlsl.Clamp(strength * 0.4f, 0f, 1f);
                float rPulse = rippleWave * fade * strengthCurve;

                rippleElevation += rPulse * elevationScale;
                if (isMeteor) rippleIntensityWhite += rPulse;
                else rippleIntensityNormal += rPulse;
            }

            elevation += rippleElevation;

            float height = Hlsl.Max(1f + elevation, 0.05f);
            target[ThreadIds.XY] = new float4(
                height,
                Hlsl.Clamp(Hlsl.Sqrt(rippleIntensityNormal), 0f, 1f),
                Hlsl.Clamp(Hlsl.Sqrt(rippleIntensityWhite), 0f, 1f),
                peakIntensity);
        }

        private static float Random(float2 st)
        {
            return Hlsl.Frac(Hlsl.Sin(Hlsl.Dot(st.XY, new float2(12.9898f, 78.233f))) * 43758.5453123f);
        }

        private static float EaseLift(float raw, float maxHeight)
        {
            float x = Hlsl.Clamp(raw, 0f, 1f);
            float eased = 1f - Hlsl.Pow(1f - x, 2.5f);
            float overshoot = Hlsl.Sin(x * 6.283f * 1.5f) * Hlsl.Exp(-x * 4f) * 0.15f;
            return (eased + overshoot) * maxHeight;
        }

        private static float FlowLift(float raw, float maxHeight)
        {
            float x = Hlsl.Clamp(raw, 0f, 1f);
            float eased = Hlsl.Pow(x, 0.75f);
            float breathe = Hlsl.Sin(x * 3.14159f) * 0.12f;
            return (eased + breathe) * maxHeight;
        }

        // 2D simplex noise (Ashima / Ian McEwan), ported from the original GLSL.
        private static float Snoise(float2 v)
        {
            float4 C = new(0.211324865405187f, 0.366025403784439f, -0.577350269189626f, 0.024390243902439f);
            float2 i = Hlsl.Floor(v + Hlsl.Dot(v, C.YY));
            float2 x0 = v - i + Hlsl.Dot(i, C.XX);
            float2 i1 = x0.X > x0.Y ? new float2(1f, 0f) : new float2(0f, 1f);
            float4 x12 = x0.XYXY + C.XXZZ;
            float2 x12xy = x12.XY - i1;
            float2 x12zw = x12.ZW;
            i = Mod289(i);
            float3 p = Permute(Permute(i.Y + new float3(0f, i1.Y, 1f)) + i.X + new float3(0f, i1.X, 1f));
            float3 m = Hlsl.Max(0.5f - new float3(Hlsl.Dot(x0, x0), Hlsl.Dot(x12xy, x12xy), Hlsl.Dot(x12zw, x12zw)), 0f);
            m *= m;
            m *= m;
            float3 x = 2f * Hlsl.Frac(p * C.WWW) - 1f;
            float3 h = Hlsl.Abs(x) - 0.5f;
            float3 ox = Hlsl.Floor(x + 0.5f);
            float3 a0 = x - ox;
            m *= 1.79284291400159f - 0.85373472095314f * (a0 * a0 + h * h);
            float3 g = new float3(
                a0.X * x0.X + h.X * x0.Y,
                a0.Y * x12xy.X + h.Y * x12xy.Y,
                a0.Z * x12zw.X + h.Z * x12zw.Y);
            return 130f * Hlsl.Dot(m, g);
        }

        private static float3 Permute(float3 x)
        {
            return Mod289(((x * 34f) + 1f) * x);
        }

        private static float2 Mod289(float2 x)
        {
            return x - Hlsl.Floor(x * (1f / 289f)) * 289f;
        }

        private static float3 Mod289(float3 x)
        {
            return x - Hlsl.Floor(x * (1f / 289f)) * 289f;
        }
    }
}
