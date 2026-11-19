using System;
using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

// The values are shared with the stage shaders, independently of the settings menu order.
internal enum FoliaNativeStage { None, Hanabi, Metaphor, Nebbia, Machi }

internal sealed unsafe partial class FoliaGpuEffect
{
    private FoliaHanabi? _hanabi;
    private FoliaMetaphor? _metaphor;
    private FoliaNebbia? _nebbia;
    private FoliaNativeStage _nativeStage, _targetStage;
    private ID3D12PipelineState? _hanabiSky, _inkField, _inkDevelop, _windowMask, _windowScene, _machiSky, _machiPanel, _machiScene;
    private ID3D12Resource? _windowTexture, _panelTexture, _machiGeometry, _machiDepth, _machiVertices;
    private ID3D12DescriptorHeap? _stageRtv, _stageDsv;
    private int _windowWidth, _windowHeight, _nativeForegroundCount, _metaphorShot = -1;
    private float _hanabiBloom;
    private bool _negativeInk;
    private Matrix4x4 _machiPrevious;

    internal static FoliaNativeStage NativeStage(ReadOnlySpan<char> mode) => mode switch
    {
        "hanabi" => FoliaNativeStage.Hanabi,
        "metaphor" => FoliaNativeStage.Metaphor,
        "nebbia" => FoliaNativeStage.Nebbia,
        "machi" => FoliaNativeStage.Machi,
        _ => FoliaNativeStage.None
    };

    private FoliaNativeStage ActiveNativeStage()
    {
        if (!_lyricsVisible || (_atlas!.Lines.Length == 0 && _song!.LyricsPending))
            return FoliaNativeStage.None;
        return NativeStage(_activeRequest.Mode);
    }

    private void PrepareStageTargets(FoliaNativeStage stage)
    {
        if (stage == _targetStage)
            return;
        ReleaseStageTargets();
        switch (stage)
        {
            case FoliaNativeStage.Hanabi:
                _hanabiSky ??= Pipeline("LyricMaterials", "Fullscreen", "HanabiSkyPS");
                break;
            case FoliaNativeStage.Metaphor:
                _inkField ??= StagePipeline("Stage", "SpriteVS", "InkFieldPS", new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One), Format.R16G16B16A16_Float);
                _inkDevelop ??= Pipeline("LyricMaterials", "Fullscreen", "InkDevelopPS");
                break;
            case FoliaNativeStage.Nebbia:
                var maximum = new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One);
                maximum.RenderTarget[0].BlendOperation = BlendOperation.Max;
                maximum.RenderTarget[0].BlendOperationAlpha = BlendOperation.Max;
                _windowMask ??= StagePipeline("Stage", "SpriteVS", "WindowMaskPS", maximum, Format.R8_UNorm);
                _windowScene ??= Pipeline("LyricMaterials", "Fullscreen", "NebbiaPS");
                _stageRtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 1));
                _windowWidth = Math.Max(1, (_size.Width + 1) / 2);
                _windowHeight = Math.Max(1, (_size.Height + 1) / 2);
                _windowTexture = StageTarget(Format.R8_UNorm, _windowWidth, _windowHeight, 0);
                break;
            case FoliaNativeStage.Machi:
                _machiSky ??= Pipeline("Machi", "Fullscreen", "MachiSkyPS");
                _machiPanel ??= Pipeline("Stage", "MachiPanelVS", "SpritePS", sprites: true);
                _machiScene ??= StagePipeline("Machi", "MachiVS", "MachiPS", BlendDescription.Opaque,
                    Format.R16G16B16A16_Float, Format.R16G16B16A16_Float);
                _stageRtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 2));
                _stageDsv = _device.CreateDescriptorHeap(new(DescriptorHeapType.DepthStencilView, 1));
                _panelTexture = StageTarget(Format.R16G16B16A16_Float, FoliaMachi.PanelWidth, FoliaMachi.PanelHeight, 0);
                _machiGeometry = StageTarget(Format.R16G16B16A16_Float, _size.Width, _size.Height, 1);
                _machiDepth = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                    ResourceDescription.Texture2D(Format.D32_Float, (uint)_size.Width, (uint)_size.Height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil),
                    ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1, 0));
                _device.CreateDepthStencilView(_machiDepth, null, _stageDsv.GetCPUDescriptorHandleForHeapStart());
                var vertices = FoliaMachi.ConvenienceStore.Mesh.AsSpan();
                _machiVertices = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
                    ResourceDescription.Buffer((ulong)vertices.Length * (uint)sizeof(FoliaMachi.Vertex)), ResourceStates.GenericRead);
                vertices.CopyTo(new Span<FoliaMachi.Vertex>(_machiVertices.Map<FoliaMachi.Vertex>(0), vertices.Length));
                _machiVertices.Unmap(0);
                break;
        }
        _targetStage = stage;
        Descriptors();
    }

    private ID3D12PipelineState StagePipeline(string file, string vs, string ps, in BlendDescription blend, Format color, Format geometry = Format.Unknown)
    {
        string source = FoliaShaderLibrary.Background(file);
        bool mesh = geometry != Format.Unknown;
        return GpuPipelineCache.Create(_device, new GraphicsPipelineStateDescription
        {
            RootSignature = _root,
            VertexShader = ShaderCompiler.Compile(source, vs, "vs_5_0"),
            PixelShader = ShaderCompiler.Compile(source, ps, "ps_5_0"),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = mesh ? [color, geometry] : [color],
            DepthStencilFormat = mesh ? Format.D32_Float : Format.Unknown,
            BlendState = blend,
            SampleDescription = new(1, 0),
            SampleMask = uint.MaxValue,
            RasterizerState = new() { CullMode = CullMode.None, FillMode = FillMode.Solid },
            DepthStencilState = new() { DepthEnable = mesh, DepthWriteMask = DepthWriteMask.All, DepthFunc = ComparisonFunction.LessEqual, StencilEnable = false }
        }, $"Folia/root-v9-native-stages/{file}/{vs}/{ps}/{color}/{geometry}");
    }

    private CpuDescriptorHandle NativeRtv(int slot) => _stageRtv!.GetCPUDescriptorHandleForHeapStart() + slot * _rtvStride;

    private ID3D12Resource StageTarget(Format format, int width, int height, int index)
    {
        var resource = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
            ResourceDescription.Texture2D(format, (uint)width, (uint)height, 1, 1, 1, 0, ResourceFlags.AllowRenderTarget), ResourceStates.PixelShaderResource);
        _device.CreateRenderTargetView(resource, null, NativeRtv(index));
        return resource;
    }

    private void ReleaseStageTargets()
    {
        GpuGraphics.DisposeResource(_windowTexture);
        GpuGraphics.DisposeResource(_panelTexture);
        GpuGraphics.DisposeResource(_machiGeometry);
        GpuGraphics.DisposeResource(_machiDepth);
        GpuGraphics.DisposeResource(_machiVertices);
        GpuGraphics.DisposeResource(_stageRtv);
        GpuGraphics.DisposeResource(_stageDsv);
        _windowTexture = _panelTexture = _machiGeometry = _machiDepth = _machiVertices = null;
        _stageRtv = _stageDsv = null;
        _targetStage = FoliaNativeStage.None;
    }

    private void DisposeNativeStages()
    {
        ReleaseStageTargets();
        GpuGraphics.DisposeResource(_hanabiSky);
        GpuGraphics.DisposeResource(_inkField);
        GpuGraphics.DisposeResource(_inkDevelop);
        GpuGraphics.DisposeResource(_windowMask);
        GpuGraphics.DisposeResource(_windowScene);
        GpuGraphics.DisposeResource(_machiPanel);
        GpuGraphics.DisposeResource(_machiScene);
        GpuGraphics.DisposeResource(_machiSky);
        _hanabi = null;
        _metaphor = null;
        _nebbia = null;
    }

    private int WriteNativeStage(Span<FoliaInstance> instances, double position, float width, float height, float scale,
        int anchor, in FoliaMotionFrame motion, bool reset)
    {
        var atlas = _atlas!;
        var song = _song!;
        var painter = new FoliaPainter(instances, width, height);
        _negativeInk = AppSettings.FoliaMetaphorNegative;
        _data[38] = new((float)_nativeStage, motion.Onset, motion.Low, _negativeInk ? 1 : 0);
        _data[39] = default;
        _metaphorShot = -1;
        switch (_nativeStage)
        {
            case FoliaNativeStage.Hanabi:
                _hanabi!.Write(ref painter, song, position, scale, motion);
                _hanabiBloom = _hanabi.Bloom(position);
                break;
            case FoliaNativeStage.Metaphor:
                _metaphor!.Write(ref painter, atlas, position, scale, motion);
                _metaphorShot = _metaphor.ShotAt(position);
                break;
            case FoliaNativeStage.Nebbia:
                Vector2 focus = _nebbia!.Write(ref painter, atlas, position, scale, anchor);
                _data[39] = new(focus, 0, 0);
                break;
            case FoliaNativeStage.Machi:
                Matrix4x4 current = FoliaMachi.Camera(position, (float)_size.Width / _size.Height);
                // Both matrices exclude jitter. The shader stores previous minus current in scene pixels.
                Matrix4x4* matrices = (Matrix4x4*)_lightData;
                matrices[0] = current;
                matrices[1] = reset || _frame.Reset ? current : _machiPrevious;
                _machiPrevious = current;
                _nativeForegroundCount = FoliaMachi.WritePanel(instances, atlas, song, position, anchor, scale);
                return _nativeForegroundCount;
        }
        _nativeForegroundCount = painter.Count;
        if (anchor >= 0)
        {
            Vector3 textColor = _nativeStage == FoliaNativeStage.Metaphor && !_negativeInk
                ? new(.13f, .11f, .14f) : new(.91f, .92f, .97f);
            FoliaLyricLayout.Subtitle(ref painter, atlas, FoliaLyricLayout.FindActiveLine(atlas.Lines, position),
                anchor, position, textColor, textColor);
        }
        return painter.Count;
    }

    private void RecordNativeStage(ID3D12GraphicsCommandList commands)
    {
        switch (_nativeStage)
        {
            case FoliaNativeStage.Hanabi:
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                RecordFullStage(commands, Rtv(1), _hanabiSky!);
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                var bloomTarget = _bloom!.Begin(commands);
                DrawRange(commands, bloomTarget, 0, _nativeForegroundCount);
                _bloom.Apply(commands, _textures[3]!, Rtv(0), false, _hanabiBloom);
                break;
            case FoliaNativeStage.Metaphor:
                var field = Rtv(1);
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                commands.ClearRenderTargetView(field, new Color4(0, 0, 0, 0));
                RecordNativeInstances(commands, field, _inkField!);
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                RecordFullStage(commands, Rtv(0), _inkDevelop!, 1);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                break;
            case FoliaNativeStage.Nebbia:
                var mask = NativeRtv(0);
                commands.ResourceBarrierTransition(_windowTexture!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                commands.ClearRenderTargetView(mask, new Color4(0, 0, 0, 0));
                commands.RSSetViewport(0, 0, _windowWidth, _windowHeight, 0, 1);
                commands.RSSetScissorRect(_windowWidth, _windowHeight);
                RecordNativeInstances(commands, mask, _windowMask!);
                commands.ResourceBarrierTransition(_windowTexture!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                SetFoliaState(commands);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                RecordFullStage(commands, Rtv(0), _windowScene!, 1);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                break;
            case FoliaNativeStage.Machi:
                RecordMachi(commands);
                break;
        }
        // The common output expects the stage root bindings even if bloom used a private heap.
        SetFoliaState(commands);
        if (_count > _nativeForegroundCount)
        {
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            DrawRange(commands, Rtv(0), _nativeForegroundCount, _count - _nativeForegroundCount);
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
    }

    private void RecordMachi(ID3D12GraphicsCommandList commands)
    {
        var panel = NativeRtv(0);
        commands.ResourceBarrierTransition(_panelTexture!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.ClearRenderTargetView(panel, new Color4(0, 0, 0, 0));
        commands.RSSetViewport(0, 0, FoliaMachi.PanelWidth, FoliaMachi.PanelHeight, 0, 1);
        commands.RSSetScissorRect(FoliaMachi.PanelWidth, FoliaMachi.PanelHeight);
        RecordNativeInstances(commands, panel, _machiPanel!);
        commands.ResourceBarrierTransition(_panelTexture!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        SetFoliaState(commands);
        var scene = Rtv(0);
        var geometry = NativeRtv(1);
        var depth = _stageDsv!.GetCPUDescriptorHandleForHeapStart();
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        RecordFullStage(commands, scene, _machiSky!);
        commands.ResourceBarrierTransition(_machiGeometry!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.ClearRenderTargetView(geometry, new Color4(0, 0, 0, 0));
        commands.ClearDepthStencilView(depth, ClearFlags.Depth, 1, 0);
        CpuDescriptorHandle* targets = stackalloc CpuDescriptorHandle[2] { scene, geometry };
        commands.OMSetRenderTargets(2, targets, false, depth);
        Bind(commands, 1);
        commands.SetGraphicsRootShaderResourceView(6, _machiVertices!.GPUVirtualAddress);
        commands.SetPipelineState(_machiScene!);
        commands.DrawInstanced((uint)FoliaMachi.ConvenienceStore.Mesh.Length, 1, 0, 0);
        commands.ResourceBarrierTransition(_machiGeometry!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
    }

    private void RecordNativeInstances(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target, ID3D12PipelineState pipeline)
    {
        Bind(commands, 0);
        commands.OMSetRenderTargets(target);
        commands.SetPipelineState(pipeline);
        SetInstanceOffset(commands, 0);
        commands.DrawInstanced(6, (uint)_nativeForegroundCount, 0, 0);
    }

    private void RecordFullStage(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target, ID3D12PipelineState pipeline, int table = 0)
    {
        Bind(commands, table);
        commands.OMSetRenderTargets(target);
        commands.SetPipelineState(pipeline);
        commands.DrawInstanced(3, 1, 0, 0);
    }
}
