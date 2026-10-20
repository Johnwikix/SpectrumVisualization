using System;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    /// <summary>Filters the transparent Sonnet scene separately from its background and viewport overlay.</summary>
    private void RenderSonnet(ID3D12GraphicsCommandList commands)
    {
        commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(Rtv(1));
        commands.ClearRenderTargetView(Rtv(1), new Color4(0, 0, 0, 0));
        Bind(commands, 0);
        commands.SetPipelineState(_sprites);
        SetInstanceOffset(commands, (uint)_backgroundCount);
        commands.DrawInstanced(6, (uint)_stageGraphicsCount, 0, 0);
        int chromatic = _sonnet.ChromaticCount;
        commands.SetPipelineState(_screenSprites);
        SetInstanceOffset(commands, (uint)(_backgroundCount + _stageGraphicsCount));
        commands.DrawInstanced(6, (uint)chromatic, 0, 0);
        commands.SetPipelineState(_sprites);
        SetInstanceOffset(commands, (uint)(_backgroundCount + _stageGraphicsCount + chromatic));
        int fixedCount = _sonnet.OverlayCount + _sonnet.SubtitleCount;
        commands.DrawInstanced(6, (uint)Math.Max(0, _count - _stageGraphicsCount - chromatic - fixedCount), 0, 0);
        commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(Rtv(0));
        Bind(commands, 1);
        commands.SetPipelineState(_sonnetComposite);
        commands.DrawInstanced(3, 1, 0, 0);
        // Screen-space marks remain sharp and stationary while paragraph filters run underneath.
        Bind(commands, 0);
        commands.SetPipelineState(_sprites);
        SetInstanceOffset(commands, (uint)(_backgroundCount + _count - fixedCount));
        commands.DrawInstanced(6, (uint)fixedCount, 0, 0);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
    }
}
