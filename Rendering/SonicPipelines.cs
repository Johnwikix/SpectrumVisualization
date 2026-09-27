using System;
using System.Runtime.InteropServices;
using System.Text;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

/// <summary>Linear scene composition, premultiplied particles, and a single HDR/SDR output transform.</summary>
internal sealed unsafe class SonicPipelines : IDisposable
{
    private const string Source = """
        Texture2D<float4> image : register(t0);
        SamplerState sampleImage : register(s0);
        cbuffer Output : register(b0) { float hdr; float whiteNits; float peakNits; };

        void VS(uint id : SV_VertexID, out float4 position : SV_Position, out float2 uv : TEXCOORD0)
        {
            float2 xy = float2((id << 1) & 2, id & 2);
            position = float4(xy * 2 - 1, 0, 1);
            uv = float2(xy.x, 1 - xy.y);
        }
        float4 Copy(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
        {
            return image.SampleLevel(sampleImage, uv, 0);
        }
        float3 EncodeSrgb(float3 c)
        {
            return lerp(c * 12.92, 1.055 * pow(max(c, 0), 1.0 / 2.4) - .055, step(.0031308, c));
        }
        float3 PQ(float3 nits)
        {
            float3 y = pow(max(nits, 0) / 10000, 2610.0 / 16384);
            return pow((3424.0 / 4096 + (2413.0 / 128) * y) / (1 + (2392.0 / 128) * y), 2523.0 / 32);
        }
        float4 Encode(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target
        {
            float3 c = max(image.SampleLevel(sampleImage, uv, 0).rgb, 0);
            if (hdr > .5)
            {
                c *= whiteNits;
                float high = max(c.r, max(c.g, c.b));
                // Preserve diffuse white and hue; smoothly compress highlights to the requested peak.
                if (high > whiteNits)
                {
                    float headroom = max(peakNits - whiteNits, .001);
                    float mapped = whiteNits + headroom * (1 - exp(-(high - whiteNits) / headroom));
                    c *= min(mapped, peakNits) / max(high, .001);
                }
                c = float3(dot(c, float3(.627404, .329283, .0433136)),
                           dot(c, float3(.069097, .919540, .0113612)),
                           dot(c, float3(.0163916, .0880132, .895595)));
                c = PQ(c);
            }
            else c = EncodeSrgb(saturate(c));
            float noise = frac(52.9829189 * frac(dot(position.xy, float2(.06711056, .00583715)))) - .5;
            return float4(saturate(c + noise / 1023), 1);
        }
        void ParticleVS(float2 p : POSITION, float4 c : COLOR, out float4 position : SV_Position, out float4 color : COLOR)
        {
            position = float4(p, 0, 1);
            color = c;
        }
        float4 ParticlePS(float4 position : SV_Position, float4 color : COLOR) : SV_Target { return color; }
        """;

    public ID3D12RootSignature Root { get; private set; } = null!;
    public ID3D12PipelineState Copy { get; private set; } = null!;
    public ID3D12PipelineState Particles { get; private set; } = null!;
    public ID3D12PipelineState Output { get; private set; } = null!;

    public SonicPipelines(ID3D12Device device)
    {
        try
        {
            Root = device.CreateRootSignature(new RootSignatureDescription1(
                RootSignatureFlags.AllowInputAssemblerInputLayout,
                [new RootParameter1(new RootDescriptorTable1([
                    new DescriptorRange1(DescriptorRangeType.ShaderResourceView, 1, 0, 0, 0, DescriptorRangeFlags.None)]), ShaderVisibility.Pixel),
                 new RootParameter1(new RootConstants(0, 0, 3), ShaderVisibility.Pixel)],
                [new StaticSamplerDescription(0, Filter.MinMagMipLinear, TextureAddressMode.Clamp, TextureAddressMode.Clamp,
                    TextureAddressMode.Clamp, 0, 0, ComparisonFunction.Never, StaticBorderColor.TransparentBlack, 0, float.MaxValue, ShaderVisibility.Pixel, 0)]));
            byte[] vs = Compile("VS", "vs_5_0");
            Copy = Create(device, vs, Compile("Copy", "ps_5_0"), Format.R16G16B16A16_Float, false);
            Output = Create(device, vs, Compile("Encode", "ps_5_0"), Format.R10G10B10A2_UNorm, false);
            Particles = Create(device, Compile("ParticleVS", "vs_5_0"), Compile("ParticlePS", "ps_5_0"), Format.R16G16B16A16_Float, true);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private ID3D12PipelineState Create(ID3D12Device device, byte[] vs, byte[] ps, Format format, bool particles)
    {
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = Root,
            VertexShader = vs,
            PixelShader = ps,
            InputLayout = particles ? new InputLayoutDescription([
                new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0),
                new InputElementDescription("COLOR", 0, Format.R32G32B32A32_Float, 8, 0)]) : default,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [format],
            SampleDescription = new SampleDescription(1, 0),
            SampleMask = uint.MaxValue,
            BlendState = particles ? BlendDescription.AlphaBlend : BlendDescription.Opaque,
            RasterizerState = new RasterizerDescription { CullMode = CullMode.None, FillMode = FillMode.Solid },
            DepthStencilState = new DepthStencilDescription { DepthEnable = false, StencilEnable = false }
        };
        return device.CreateGraphicsPipelineState(description);
    }

    private static byte[] Compile(string entry, string profile)
    {
        byte[] source = Encoding.UTF8.GetBytes(Source);
        fixed (byte* pointer = source)
        {
            var result = Compiler.Compile(pointer, new PointerUSize((nuint)source.Length), "SonicOutput.hlsl",
                null, null, entry, profile, ShaderFlags.OptimizationLevel3, EffectFlags.None, out Blob shader, out Blob errors);
            using (shader)
            using (errors)
            {
                if (shader == null)
                    throw new InvalidOperationException($"Shader {entry}: {errors?.AsString()} ({result})");
                byte[] bytes = new byte[(int)(ulong)shader.BufferSize];
                Marshal.Copy(shader.BufferPointer, bytes, 0, bytes.Length);
                return bytes;
            }
        }
    }

    public void Dispose()
    {
        Particles?.Dispose();
        Output?.Dispose();
        Copy?.Dispose();
        Root?.Dispose();
    }
}
