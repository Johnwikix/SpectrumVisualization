using Vortice.Direct3D12;
using Vortice.Direct3D12.Debug;
using Vortice.DXGI;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    internal void VerifyDebugMessages()
    {
        using var info = _device.QueryInterfaceOrNull<ID3D12InfoQueue>();
        if (info == null) return;
        bool failed = false;
        int reported = 0;
        for (ulong i = 0; i < info.NumStoredMessagesAllowedByRetrievalFilter; i++)
        {
            var message = info.GetMessage(i);
            if (message.Severity is MessageSeverity.Error or MessageSeverity.Corruption)
            {
                if (reported++ < 10) Console.Error.WriteLine($"D3D12 {message.Id}: {message.Description}");
                failed = true;
            }
        }
        info.ClearStoredMessages();
        if (failed) throw new InvalidOperationException("D3D12 debug layer reported errors");
    }

    internal void VerifyTemporalMotion()
    {
        if ((_capabilities & (1u << (int)ReconstructionMode.XeSS)) == 0) return;
        Configure(new(100, ReconstructionMode.XeSS, 80, "native"));
        AppSettings.SonicAutoRotate = false;
        Render(1d / 120, 200, 1000);
        WaitForGpu();
        var effect = (ITemporalGpuEffect)_effect;
        for (int i = 0; i < 2; i++)
        {
            effect.SetTemporalFrame(new TemporalFrame(i == 0 ? -.37f : .41f, i == 0 ? .29f : -.33f, false));
            _effect.PrepareFrame(0, _options.Detail);
            ((WinExSpectrumTest.Effects.Sonic.SonicGpuEffect)_effect).VerifyStaticConstants();
            VerifyStaticHeights();
            BeginCommands();
            Transition(_scene!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            _effect.RecordScene(_commands, Rtv(2));
            Transition(_scene!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            Submit();
            WaitForGpu();
            float maximum = ReadMotionMaximum(effect.Motion);
            if (maximum > .002f) throw new InvalidOperationException($"Static motion contains camera jitter: {maximum}");
        }
        AppSettings.SonicAutoRotate = true;
        // Keep history; rotating the camera must now generate nonzero motion.
        Render(1d / 120, 200, 1000);
        float moving = ReadMotionMaximum(effect.Motion);
        if (moving < .01f) throw new InvalidOperationException("Moving camera has no motion vectors");
        ResetHistory();
        Render(1d / 120, 200, 1000);
        if (ReadMotionMaximum(effect.Motion) > .002f) throw new InvalidOperationException("Reset frame reused old motion history");
        Console.WriteLine($"PASS static motion excludes jitter, moving motion={moving:F3} pixels, reset clears history");
    }

    private float ReadMotionMaximum(ID3D12Resource source)
    {
        uint pitch = (uint)((_size.Width * 4 + 255) & ~255);
        using var readback = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
            ResourceDescription.Buffer((ulong)pitch * (uint)_size.Height), ResourceStates.CopyDest);
        BeginCommands();
        Transition(source, ResourceStates.NonPixelShaderResource, ResourceStates.CopySource);
        _commands.CopyTextureRegion(new TextureCopyLocation(readback, new PlacedSubresourceFootPrint
        {
            Footprint = new SubresourceFootPrint(Format.R16G16_Float, (uint)_size.Width, (uint)_size.Height, 1, pitch)
        }), 0, 0, 0, new TextureCopyLocation(source, 0), null);
        Transition(source, ResourceStates.CopySource, ResourceStates.NonPixelShaderResource);
        Submit();
        WaitForGpu();
        byte* address = readback.Map<byte>(0);
        float maximum = 0;
        for (int y = 0; y < _size.Height; y++)
        {
            Half* row = (Half*)(address + y * pitch);
            for (int x = 0; x < _size.Width * 2; x++)
            {
                float value = Math.Abs((float)row[x]);
                if (!float.IsFinite(value)) throw new InvalidOperationException("Nonfinite motion vector");
                maximum = Math.Max(maximum, value);
            }
        }
        readback.Unmap(0);
        return maximum;
    }

    private void VerifyStaticHeights()
    {
        var effect = (WinExSpectrumTest.Effects.Sonic.SonicGpuEffect)_effect;
        int grid = _options.Detail;
        uint pitch = (uint)((grid * 16 + 255) & ~255);
        using var current = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
            ResourceDescription.Buffer((ulong)pitch * (uint)grid), ResourceStates.CopyDest);
        using var previous = _device.CreateCommittedResource(new HeapProperties(HeapType.Readback), HeapFlags.None,
            ResourceDescription.Buffer((ulong)pitch * (uint)grid), ResourceStates.CopyDest);
        BeginCommands();
        var footprint = new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(Format.R32G32B32A32_Float, (uint)grid, (uint)grid, 1, pitch) };
        Transition(effect.FieldForProbe, ResourceStates.UnorderedAccess, ResourceStates.CopySource);
        Transition(effect.PreviousForProbe, ResourceStates.NonPixelShaderResource, ResourceStates.CopySource);
        _commands.CopyTextureRegion(new TextureCopyLocation(current, footprint), 0, 0, 0, new TextureCopyLocation(effect.FieldForProbe, 0), null);
        _commands.CopyTextureRegion(new TextureCopyLocation(previous, footprint), 0, 0, 0, new TextureCopyLocation(effect.PreviousForProbe, 0), null);
        Transition(effect.FieldForProbe, ResourceStates.CopySource, ResourceStates.UnorderedAccess);
        Transition(effect.PreviousForProbe, ResourceStates.CopySource, ResourceStates.NonPixelShaderResource);
        Submit(); WaitForGpu();
        byte* a = current.Map<byte>(0);
        byte* b = previous.Map<byte>(0);
        float delta = 0;
        for (int y = 0; y < grid; y++)
        for (int x = 0; x < grid * 4; x++) delta = Math.Max(delta, Math.Abs(((float*)(a + y * pitch))[x] - ((float*)(b + y * pitch))[x]));
        current.Unmap(0); previous.Unmap(0);
        if (delta > .0001f) throw new InvalidOperationException("Static probe terrain changed");
    }
}
