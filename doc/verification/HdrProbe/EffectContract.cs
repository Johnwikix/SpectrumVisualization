using Vortice.Direct3D12;
using Vortice.Mathematics;
using WinExSpectrumTest.Effects.Sonic;

namespace WinExSpectrumTest.Rendering;

internal sealed unsafe partial class GpuGraphics
{
    public void VerifyEffectContract()
    {
        int disposed = 0;
        ChangeEffect(() => new ProbeEffect(() => disposed++, false));
        Render(1d / 120, 200, 1000);
        float red = ReadLinearScene()[0].X;
        if (Math.Abs(red - .25f) > .001f) throw new InvalidOperationException("Second effect did not own scene rendering");
        try
        {
            ChangeEffect(() => new ProbeEffect(() => disposed++, true));
            throw new InvalidOperationException("Initialization failure must propagate");
        }
        catch (NotSupportedException) { }
        if (disposed != 1) throw new InvalidOperationException("Failed candidate not disposed");
        Render(1d / 120, 200, 1000);
        if (Math.Abs(ReadLinearScene()[0].X - red) > .001f) throw new InvalidOperationException("Failed candidate replaced live effect");
        Configure(new(75, false, 120));
        Resize(321, 181);
        Render(1d / 120, 200, 1000);
        if (ReadLinearScene().Length != 240 * 135) throw new InvalidOperationException("Internal size not independent of output size");
        ChangeEffect(static () => new SonicGpuEffect());
        if (disposed != 2) throw new InvalidOperationException("Previous effect not disposed exactly once");
        Configure(new(100, true, 160));
        Render(1d / 120, 200, 1000);
        Console.WriteLine("PASS second GPU effect, failed initialization, independent internal size and Sonic restoration");
    }

    private sealed class ProbeEffect(Action disposed, bool fail) : IGpuVisualizerEffect
    {
        public void Initialize(in GpuEffectServices services)
        {
            if (fail) throw new NotSupportedException("Injected initialization failure");
        }
        public void Resize(in GpuRenderSize size) { }
        public void PrepareFrame(double elapsedSeconds, int detail) { }
        public void RecordScene(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target)
            => commands.ClearRenderTargetView(target, new Color4(.25f, .125f, .5f, 1));
        public void Dispose() => disposed();
    }
}
