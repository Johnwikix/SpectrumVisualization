using Vortice.Direct3D12;
using Vortice.DXGI;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private void ResizeLatent()
    {
        Rendering.GpuGraphics.DisposeResource(_latentTarget);
        _latentTarget = Texture(Format.R16G16B16A16_Float, ResourceStates.PixelShaderResource, ResourceFlags.AllowRenderTarget);
        _device.CreateRenderTargetView(_latentTarget, null, Rtv(4));
    }
    private void RenderLatent(ID3D12GraphicsCommandList commands)
    {
        // CSS scales / filters the two rendered canvases before soft-light blending.
        // Keeping those as texture samples preserves the original dither edge filtering.
        Background(commands, 1, 4, 0);
        Background(commands, 2, 3, 0);
        commands.ResourceBarrierTransition(_latentTarget!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(Rtv(4));
        Bind(commands, 3);
        commands.SetPipelineState(_latentComposite);
        commands.DrawInstanced(3, 1, 0, 0);
        commands.ResourceBarrierTransition(_latentTarget!, ResourceStates.RenderTarget, ResourceStates.CopySource);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
        commands.CopyResource(_textures[3]!, _latentTarget!);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
        commands.ResourceBarrierTransition(_latentTarget!, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
    }
}
