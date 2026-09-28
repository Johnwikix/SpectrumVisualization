using System;
using Microsoft.Win32.SafeHandles;
using Vortice.DXGI;

namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    private const int BackBufferCount = 2;
    private SwapChainFlags _swapChainFlags;
    private SafeWaitHandle? _frameLatencyHandle;
    internal bool AllowsTearing => (_swapChainFlags & SwapChainFlags.AllowTearing) != 0;

    private void CreatePresentationSwapChain(int width, int height, bool requestTearing)
    {
        _swapChainFlags = SwapChainFlags.FrameLatencyWaitableObject;
        if (requestTearing && _factory.PresentAllowTearing)
        {
            try
            {
                _swapChain = CreateCompositionChain(width, height, _swapChainFlags | SwapChainFlags.AllowTearing);
                _swapChainFlags |= SwapChainFlags.AllowTearing;
            }
            catch (Exception ex) when (ex.HResult is unchecked((int)0x887A0001) or unchecked((int)0x887A0004) or unchecked((int)0x80070057))
            {
                // Some composition configurations reject tearing despite factory support.
                // Keep rendering with an explicitly reported composed presentation path.
            }
        }
        _swapChain ??= CreateCompositionChain(width, height, _swapChainFlags);
        _swapChain.MaximumFrameLatency = BackBufferCount;
        // The API transfers handle ownership. Never wait on compositor latency in the render loop.
        _frameLatencyHandle = new SafeWaitHandle(_swapChain.FrameLatencyWaitableObject, ownsHandle: true);
    }

    private IDXGISwapChain3 CreateCompositionChain(int width, int height, SwapChainFlags flags)
    {
        using var chain = _factory.CreateSwapChainForComposition(_queue,
            new SwapChainDescription1((uint)width, (uint)height, OutputFormat, false, Usage.RenderTargetOutput,
                BackBufferCount, Scaling.Stretch, SwapEffect.FlipSequential, AlphaMode.Ignore, flags), null);
        return chain.QueryInterface<IDXGISwapChain3>();
    }

    private PresentFlags PresentationFlags(bool nonblocking) =>
        (AllowsTearing ? PresentFlags.AllowTearing : PresentFlags.None) |
        (nonblocking ? PresentFlags.DoNotWait : PresentFlags.None);
}
