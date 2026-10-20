using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private void RenderLumiere(ID3D12GraphicsCommandList commands)
    {
        if (!_lumiere.HasScene)
        {
            CopyScratch(commands, 3, 4, ResourceStates.RenderTarget);
            DrawRange(commands, Rtv(1), _backgroundCount, _count);
            commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            CopyScratch(commands, 4, 3, ResourceStates.PixelShaderResource);
            return;
        }
        // Graphics and text have independent bloom. The shared background is composed afterwards.
        if (_lumiere.DarkCount > 0)
        {
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            DrawRange(commands, Rtv(0), _backgroundCount, _lumiere.DarkCount);
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
        CopyScratch(commands, 3, 4, ResourceStates.PixelShaderResource);
        var graphics = _bloom!.Begin(commands);
        SetFoliaState(commands);
        if (!Model.AppSettings.FoliaLumiereTextOnly)
        {
            Bind(commands, 1);
            commands.OMSetRenderTargets(graphics);
            commands.SetPipelineState(_lightField);
            commands.DrawInstanced(3, 1, 0, 0);
        }
        DrawRange(commands, graphics, _backgroundCount + _lumiere.DarkCount, _lumiere.GraphicsCount - _lumiere.DarkCount);
        _bloom.Apply(commands, _textures[3]!, Rtv(0), false);

        CopyScratch(commands, 3, 4, ResourceStates.PixelShaderResource);
        var text = _bloom.Begin(commands);
        DrawRange(commands, text, _backgroundCount + _lumiere.GraphicsCount, _lumiere.TextCount);
        _bloom.Apply(commands, _textures[3]!, Rtv(0), true);

        // Front bokeh and the optical frame sit outside both filters in the original.
        CopyScratch(commands, 3, 4, ResourceStates.RenderTarget);
        int filtered = _lumiere.GraphicsCount + _lumiere.TextCount;
        DrawRange(commands, Rtv(1), _backgroundCount + filtered, _count - filtered);
        commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        CopyScratch(commands, 4, 3, ResourceStates.PixelShaderResource);
    }
    private void SetFoliaState(ID3D12GraphicsCommandList commands)
    {
        commands.SetDescriptorHeaps(_srv);
        commands.SetGraphicsRootSignature(_root);
        commands.SetGraphicsRootConstantBufferView(0, _constants.GPUVirtualAddress);
        commands.SetGraphicsRootConstantBufferView(2, _lightConstants.GPUVirtualAddress);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.RSSetViewport(0, 0, _size.Width, _size.Height, 0, 1);
        commands.RSSetScissorRect(_size.Width, _size.Height);
    }
    private void DrawRange(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target, int first, int count)
    {
        SetFoliaState(commands);
        Bind(commands, 0);
        commands.OMSetRenderTargets(target);
        commands.SetPipelineState(_sprites);
        SetInstanceOffset(commands, (uint)first);
        commands.DrawInstanced(6, (uint)count, 0, 0);
    }
}
