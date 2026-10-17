using System;
using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Audio;
using WinExSpectrumTest.Model;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Single-slot native stage. The host completes the preceding frame before PrepareFrame.
/// GPU resources and mapped memory are owned exclusively by its render worker.</summary>
internal sealed unsafe partial class FoliaGpuEffect(FoliaPlaybackState playback) : IGpuVisualizerEffect, ITemporalGpuEffect
{
    internal const string EffectId = "folia";
    private const int Capacity = 32768;
    private ID3D12Device _device = null!;
    private SpectrumAnalyzer _analyzer = null!;
    private ID3D12RootSignature _root = null!;
    private ID3D12DescriptorHeap _srv = null!, _rtv = null!, _dsv = null!;
    private ID3D12Resource _constants = null!, _instances = null!;
    private ID3D12Resource _lightConstants = null!;
    private Vector4* _lightData;
    private readonly FoliaLumiere _lumiere = new();
    private readonly FoliaCadenza _cadenza = new();
    private readonly FoliaGlowWords _glowWords = new();
    private readonly FoliaPartita _partita = new();
    private readonly FoliaSonnet _sonnet = new();
    private readonly FoliaDiorama _diorama = new();
    private readonly FoliaFume _fume = new();
    private readonly FoliaTilt _tilt = new();
    private readonly FoliaPendolo _pendolo = new();
    private readonly FoliaTempera _tempera = new();
    private int _stageGraphicsCount;
    private ID3D12PipelineState _clockwork = null!;
    private ID3D12Resource _clockConstants = null!;
    private ID3D12Resource? _clockVertices;
    private Vector4* _clockTransforms;
    private int _clockMeshVersion;

    private FoliaBloom? _bloom;
    private ID3D12PipelineState _lightField = null!;
    private ID3D12PipelineState _vignette = null!;
    private ID3D12PipelineState _sora = null!;
    private ID3D12PipelineState _print = null!;
    private string _lastMode = "", _lastBackground = "", _lastImageEffect = "";
    private float _lastFontScale;
    private bool _lastCoverBackground;
    private double _lastPosition;
    public bool ResetHistoryRequested => !ReferenceEquals(_song, playback.Song)
        || _lastMode != AppSettings.FoliaLyricMode || _lastBackground != AppSettings.FoliaBackground
        || _font != AppSettings.FoliaFontFamily || _weight != AppSettings.FoliaFontWeight
        || _lastFontScale != AppSettings.FoliaFontScale || _lastImageEffect != AppSettings.FoliaImageEffect
        || _lastCoverBackground != AppSettings.FoliaCoverBackground || Math.Abs(playback.Clock.Now - _lastPosition) > .5;
    private Vector4* _data;
    private FoliaInstance* _mapped;
    private readonly ID3D12PipelineState?[] _backgrounds = new ID3D12PipelineState?[8];
    private ID3D12PipelineState _copy = null!, _sprites = null!, _screenSprites = null!;
    private ID3D12PipelineState? _temporalCopy;
    private readonly ID3D12Resource?[] _textures = new ID3D12Resource?[6];
    private readonly ID3D12Resource?[] _uploads = new ID3D12Resource?[6];
    private readonly bool[] _pending = new bool[6];
    private ID3D12Resource? _depth, _motion, _reactive;
    private int _srvStride, _rtvStride, _count, _backgroundCount;
    private readonly FoliaBackgroundGeometry _geometry = new();
    private readonly FoliaMonetDrift _monetDrift = new();
    private readonly FoliaLatent _latent = new();
    private ID3D12Resource? _latentTarget;
    private ID3D12PipelineState _latentComposite = null!;
    private double _backgroundTime, _lineAmbient;
    private int _lineIndex = int.MinValue;
    private GpuRenderSize _size;
    private bool _temporal, _depthReadable, _disposed;
    private TemporalFrame _frame;
    private FoliaTextAtlas? _atlas;
    private FoliaSong? _song;
    private object? _modeAtlasSource;
    private string _font = "";
    private int _weight;
    private double _animation;
    public TemporalCamera Camera => new(.1f, 100, MathF.PI / 3);
    public ID3D12Resource Depth => _depth!;
    public ID3D12Resource Motion => _motion!;
    public ID3D12Resource Reactive => _reactive!;

    public void Initialize(in GpuEffectServices services)
    {
        _device = services.Device;
        _analyzer = services.Analyzer;
        _srvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        _rtvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _srv = _device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 28, DescriptorHeapFlags.ShaderVisible));
        _rtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 5));
        _dsv = _device.CreateDescriptorHeap(new(DescriptorHeapType.DepthStencilView, 1));
        _constants = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(512), ResourceStates.GenericRead);
        _data = _constants.Map<Vector4>(0);
        _lightConstants = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(1024), ResourceStates.GenericRead);
        _lightData = _lightConstants.Map<Vector4>(0);
        _clockConstants = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(768), ResourceStates.GenericRead);
        _clockTransforms = _clockConstants.Map<Vector4>(0);
        _instances = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer((ulong)(Capacity * sizeof(FoliaInstance))), ResourceStates.GenericRead);
        _mapped = _instances.Map<FoliaInstance>(0);
        _root = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
            [new RootParameter1(RootParameterType.ConstantBufferView,new RootDescriptor1(0,0),ShaderVisibility.All),
             new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView,5,0,0,0,DescriptorRangeFlags.None),new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,6,0,6,DescriptorRangeFlags.None)]),ShaderVisibility.Pixel),
             new RootParameter1(RootParameterType.ConstantBufferView,new RootDescriptor1(1,0),ShaderVisibility.All),
             new RootParameter1(new RootConstants(2,0,1),ShaderVisibility.Vertex),
             new RootParameter1(RootParameterType.ShaderResourceView,new RootDescriptor1(5,0),ShaderVisibility.All),
             new RootParameter1(RootParameterType.ConstantBufferView,new RootDescriptor1(3,0),ShaderVisibility.Vertex),
             new RootParameter1(RootParameterType.ShaderResourceView,new RootDescriptor1(7,0),ShaderVisibility.Vertex)],
            [Sampler(0, TextureAddressMode.Clamp), Sampler(1, TextureAddressMode.Wrap)]));
        _backgrounds[0] = Pipeline("Stage", "Fullscreen", "PS");
        string[] paper = ["mesh-gradient", "dithering", "image-dithering", "fluted-glass", "paper-texture", "halftone-dots", "lens-distortion"];
        for (int i = 0; i < paper.Length; i++)
            _backgrounds[i + 1] = Pipeline(paper[i], "Fullscreen", "PS");
        _copy = Pipeline("Stage", "Fullscreen", "CopyPS");
        _sprites = Pipeline("Stage", "SpriteVS", "SpritePS", sprites: true);
        _screenSprites = Pipeline("Stage", "SpriteVS", "SpritePS", sprites: true, screen: true);
        _lightField = Pipeline("Lumiere", "Fullscreen", "PS");
        _vignette = Pipeline("Stage", "Fullscreen", "VignettePS", sprites: true);
        _print = Pipeline("Print", "Fullscreen", "PS");
        _sora = Pipeline("Sora", "VS", "PS", sprites: true);
        _latentComposite = Pipeline("Stage", "Fullscreen", "LatentCompositePS");
        _clockwork = Pipeline("Stage", "ClockworkVS", "ClockworkPS", sprites: true);
        SetImage(2, 128, 128, FoliaAssets.Noise(), Format.B8G8R8A8_UNorm);
    }

    private static StaticSamplerDescription Sampler(uint index, TextureAddressMode address) => new(index, Filter.MinMagMipLinear, address, address, address, 0, 0, ComparisonFunction.Never, StaticBorderColor.TransparentBlack, 0, float.MaxValue, ShaderVisibility.All, 0);
    private ID3D12PipelineState Pipeline(string file, string vs, string ps, bool sprites = false, bool temporal = false, bool screen = false)
    {
        string shader = FoliaShaderLibrary.Background(file);
        return _device.CreateGraphicsPipelineState(new GraphicsPipelineStateDescription
        {
            RootSignature = _root,
            VertexShader = ShaderCompiler.Compile(shader, vs, "vs_5_0"),
            PixelShader = ShaderCompiler.Compile(shader, ps, "ps_5_0"),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = temporal ? [Format.R16G16B16A16_Float, Format.R16G16_Float, Format.R8_UNorm] : [Format.R16G16B16A16_Float],
            DepthStencilFormat = temporal ? Format.D32_Float : Format.Unknown,
            SampleDescription = new(1, 0),
            SampleMask = uint.MaxValue,
            BlendState = screen ? new BlendDescription(Blend.One, Blend.InverseSourceColor, Blend.One, Blend.InverseSourceAlpha) : sprites ? BlendDescription.AlphaBlend : BlendDescription.Opaque,
            RasterizerState = new() { CullMode = CullMode.None, FillMode = FillMode.Solid },
            DepthStencilState = new() { DepthEnable = temporal, DepthWriteMask = DepthWriteMask.All, DepthFunc = ComparisonFunction.Always, StencilEnable = false }
        });
    }

    public void ConfigureTemporal(bool enabled)
    {
        if (enabled && _temporalCopy == null)
            _temporalCopy = Pipeline("Stage", "Fullscreen", "TemporalPS", temporal: true);
        _temporal = enabled;
    }
    public void SetTemporalFrame(in TemporalFrame frame) => _frame = frame;

    public void Resize(in GpuRenderSize size)
    {
        _size = size;
        for (int i = 3; i < 5; i++)
        {
            GpuGraphics.DisposeResource(_textures[i]);
            _textures[i] = Texture(Format.R16G16B16A16_Float, ResourceStates.PixelShaderResource, ResourceFlags.AllowRenderTarget);
            _device.CreateRenderTargetView(_textures[i]!, null, Rtv(i - 3));
        }
        _bloom?.Resize(size.Width, size.Height, _textures[4]!);
        if (_latentTarget != null)
            ResizeLatent();
        GpuGraphics.DisposeResource(_depth);
        GpuGraphics.DisposeResource(_motion);
        GpuGraphics.DisposeResource(_reactive);
        _depth = null;
        _motion = null;
        _reactive = null;
        _depthReadable = false;
        if (_temporal)
        {
            _depth = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, ResourceDescription.Texture2D(Format.D32_Float, (uint)size.Width, (uint)size.Height, 1, 1, 1, 0, ResourceFlags.AllowDepthStencil), ResourceStates.DepthWrite, new ClearValue(Format.D32_Float, 1, 0));
            _device.CreateDepthStencilView(_depth, null, _dsv.GetCPUDescriptorHandleForHeapStart());
            _motion = Texture(Format.R16G16_Float, ResourceStates.NonPixelShaderResource, ResourceFlags.AllowRenderTarget);
            _reactive = Texture(Format.R8_UNorm, ResourceStates.NonPixelShaderResource, ResourceFlags.AllowRenderTarget);
            _device.CreateRenderTargetView(_motion, null, Rtv(2));
            _device.CreateRenderTargetView(_reactive, null, Rtv(3));
        }
        if (_atlas != null)
            Descriptors();
    }
    private ID3D12Resource Texture(Format format, ResourceStates state, ResourceFlags flags) => _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, ResourceDescription.Texture2D(format, (uint)_size.Width, (uint)_size.Height, 1, 1, 1, 0, flags), state);
    private CpuDescriptorHandle Rtv(int slot) => _rtv.GetCPUDescriptorHandleForHeapStart() + slot * _rtvStride;

    public void PrepareFrame(double elapsedSeconds, int detail)
    {
        for (int i = 0; i < _uploads.Length; i++)
            if (!_pending[i])
            {
                GpuGraphics.DisposeResource(_uploads[i]);
                _uploads[i] = null;
            }
        var song = playback.Song;
        string family = AppSettings.FoliaFontFamily, mode = AppSettings.FoliaLyricMode, backgroundMode = AppSettings.FoliaBackground;
        int weight = AppSettings.FoliaFontWeight;
        float fontScale = AppSettings.FoliaFontScale;
        bool songChanged = !ReferenceEquals(song, _song);
        if (songChanged || _font != family || _weight != weight)
        {
            // Cold song/font changes only. No Win2D object or text shaping occurs in steady-state frames.
            _atlas = new FoliaTextAtlas(song, family, weight);
            _font = family;
            _weight = weight;
            _song = song;
            SetImage(0, _atlas.Width, _atlas.Height, _atlas.Pixels, Format.R16G16B16A16_Float);
            _atlas.ReleaseDistancePixels();
            SetImage(1, Math.Max(1, song.CoverWidth), Math.Max(1, song.CoverHeight), song.CoverRgba.Length > 0 ? song.CoverRgba : [14, 17, 24, 255], Format.R8G8B8A8_UNorm);
            Descriptors();
        }
        if (mode == "cappella")
        {
            var emojis = FoliaAssets.Emojis;
            if (!ReferenceEquals(_modeAtlasSource, emojis))
            {
                SetImage(5, emojis.Width, emojis.Height, emojis.Pixels, Format.B8G8R8A8_UNorm);
                _modeAtlasSource = emojis;
                Descriptors();
            }
        }
        else if (!ReferenceEquals(_modeAtlasSource, _atlas))
        {
            // Cappella and glyph shadows never coexist. The shared mode atlas has one render-worker owner.
            SetImage(5, _atlas!.Width, _atlas.Height, _atlas.ShadowPixels, Format.R8_UNorm);
            _modeAtlasSource = _atlas;
            Descriptors();
        }
        var clock = playback.Clock;
        double position = clock.Now;
        bool modeChanged = _lastMode != mode;
        bool backgroundChanged = _lastBackground != backgroundMode;
        _lastMode = mode;
        _lastBackground = backgroundMode;
        _lastImageEffect = AppSettings.FoliaImageEffect;
        _lastFontScale = fontScale;
        _lastCoverBackground = AppSettings.FoliaCoverBackground;
        _lastPosition = position;
        float energy = Math.Clamp(_analyzer.LatestFeatures[FeatureIndex.Energy], 0, 1);
        double delta = Math.Clamp(elapsedSeconds, 0, .1);
        if (backgroundChanged)
        {
            _backgroundTime = 0;
            _animation = 0;
        }
        _backgroundTime += delta;
        if (clock.Playing)
            _animation += delta;
        int lineIndex = FoliaLyricLayout.FindLine(_atlas!.Lines, position);
        if (modeChanged || songChanged || _lineIndex != lineIndex)
            _lineAmbient = 0;
        else
            _lineAmbient += delta;
        _lineIndex = lineIndex;
        _data[0] = new(_size.Width, _size.Height, _size.OutputWidth, _size.OutputHeight);
        _data[1] = new(backgroundMode is "monet" or "nomand" ? (float)_backgroundTime : (float)_animation, (float)position, energy, (float)elapsedSeconds);
        _data[2] = new(FoliaSettings.IndexOf(FoliaSettings.BackgroundModes, backgroundMode), FoliaSettings.IndexOf(FoliaSettings.LyricModes, mode), FoliaSettings.IndexOf(FoliaSettings.ImageEffects, AppSettings.FoliaImageEffect), 1);
        _data[3] = new(_temporal ? _frame.JitterX : 0, _temporal ? _frame.JitterY : 0, 0, 0);
        for (int i = 0; i < 10; i++)
            _data[4 + i] = new(song.Palette[i % song.Palette.Length], 1);
        _data[14] = new(_atlas!.Width, _atlas.Height, song.CoverWidth, song.CoverHeight);
        _data[15] = new(AppSettings.FoliaCoverBackground ? 1 : 0, FoliaDistanceField.Range, _size.PixelsPerDip, _temporal ? 1 : 0);
        _data[16] = new(_monetDrift.At(_backgroundTime), 0);
        _data[17] = _data[6];
        _data[18] = _data[5];
        _data[19] = _data[6];
        _data[20] = _data[5];
        _data[21] = _data[4];
        _data[22] = _data[7];
        if (backgroundMode == "latent")
        {
            _latent.Prepare(song);
            if (_latentTarget == null)
                ResizeLatent();
        }
        _latent.Update(backgroundMode == "latent", !clock.Playing, elapsedSeconds, _analyzer.LatestFeatures,
            new Span<Vector4>(_data + 17, 6), out _data[23], out _data[24], out _data[25]);
        var instances = new Span<FoliaInstance>(_mapped, Capacity);
        float dpi = _size.PixelsPerDip;
        float layoutWidth = _size.OutputWidth / dpi, layoutHeight = _size.OutputHeight / dpi;
        _backgroundCount = mode == "fume" && backgroundMode == "common" ? 0 : _geometry.Write(instances, song, (int)_data[2].X, (float)_animation, energy, layoutWidth, layoutHeight, !clock.Playing);
        if ((int)_data[2].Y == 12)
        {
            if (_bloom == null)
            {
                _bloom = new FoliaBloom(_device);
                _bloom.Resize(_size.Width, _size.Height, _textures[4]!);
            }
            _lumiere.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
            _lumiere.Update(new Span<Vector4>(_lightData, 52), song, position, _size.Width, _size.Height, _analyzer.LatestFeatures[FeatureIndex.Bass]);
        }
        if ((int)_data[2].Y == 1)
        {
            _cadenza.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
            _cadenza.FrameDelta = (float)elapsedSeconds;
        }
        if ((int)_data[2].Y == 2)
            _partita.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
        if ((int)_data[2].Y == 11)
            _tempera.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
        if ((int)_data[2].Y == 3)
        {
            _fume.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
            _fume.SetAudio(_analyzer.LatestFeatures, clock.Playing);
        }
        if ((int)_data[2].Y == 8)
        {
            _diorama.Prepare(_atlas);
            _diorama.Energy = energy;
            _diorama.FrameDelta = (float)elapsedSeconds;
            if (modeChanged)
                _diorama.ResetMotion();
        }
        if ((int)_data[2].Y == 5)
            _tilt.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
        if ((int)_data[2].Y == 9)
        {
            _pendolo.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
            _pendolo.Update(elapsedSeconds, position, Math.Max(0, FoliaLyricLayout.FindLine(_atlas.Lines, position)), _analyzer.LatestFeatures[FeatureIndex.Bass], clock.Playing);
            _pendolo.WriteTransforms(new Span<Vector4>(_clockTransforms, 33));
            if (_clockMeshVersion != _pendolo.MeshVersion)
            {
                // PrepareFrame runs after the host's preceding-frame fence, so this immutable buffer can be replaced safely.
                GpuGraphics.DisposeResource(_clockVertices);
                _clockVertices = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
                    ResourceDescription.Buffer((ulong)(_pendolo.Mesh.Length * sizeof(float))), ResourceStates.GenericRead);
                _pendolo.Mesh.AsSpan().CopyTo(new Span<float>(_clockVertices.Map<float>(0), _pendolo.Mesh.Length));
                _clockVertices.Unmap(0);
                _clockMeshVersion = _pendolo.MeshVersion;
            }
        }
        if ((int)_data[2].Y == 10)
            _sonnet.Prepare(_atlas, layoutWidth, layoutHeight, fontScale, family, weight);
        _glowWords.Prepare(_atlas);
        _glowWords.FrameDelta = (float)elapsedSeconds;
        _glowWords.Begin(lineIndex, (int)_data[2].Y, position);
        _count = FoliaLyricLayout.Write(instances[_backgroundCount..], _atlas, song, position, (float)_lineAmbient, (int)_data[2].Y, layoutWidth, layoutHeight, fontScale, _lumiere, _cadenza, _tempera, _sonnet, _diorama, _fume, _tilt, _pendolo, _glowWords, _partita);
        _data[26] = new(_tempera.PostSeed, 0, 0, 0);
        _stageGraphicsCount = (int)_data[2].Y == 11 ? _tempera.GraphicsCount : (int)_data[2].Y == 9 && _atlas.Lines.Length > 0 ? 1 : 0;
        _data[8] = new(FoliaTempera.Ink, 1);
        _data[9] = new(FoliaTempera.Paper, 1);
        // Layout uses DIPs like the original CSS/canvas. The swap chain and SDKs keep physical pixel sizes.
        if (dpi != 1)
        {
            for (int i = 0; i < _backgroundCount + _count; i++)
            {
                ref var instance = ref instances[i];
                instance.Rect *= dpi;
                if (instance.Shape.X == 25)
                {
                    instance.Transform *= dpi;
                    continue;
                }
                instance.Transform.Z *= dpi;
                instance.Shape.Y *= dpi;
                instance.Shape.Z *= dpi;
            }
        }
    }

    public void RecordScene(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target)
    {
        UploadImages(commands);
        commands.SetDescriptorHeaps(_srv);
        commands.SetGraphicsRootSignature(_root);
        commands.SetGraphicsRootConstantBufferView(0, _constants.GPUVirtualAddress);
        commands.SetGraphicsRootConstantBufferView(2, _lightConstants.GPUVirtualAddress);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        commands.RSSetViewport(0, 0, _size.Width, _size.Height, 0, 1);
        commands.RSSetScissorRect(_size.Width, _size.Height);
        int background = (int)_data[2].X;
        if (background == 1)
            RenderLatent(commands);
        else
            Background(commands, background == 3 ? 3 + (int)_data[2].Z : 0, 3, 0);
        if (background == 4)
        {
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            commands.OMSetRenderTargets(Rtv(0));
            Bind(commands, 0);
            commands.SetPipelineState(_sora);
            commands.DrawInstanced(6, 150, 0, 0);
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
        if (_backgroundCount > 0)
        {
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            commands.OMSetRenderTargets(Rtv(0));
            Bind(commands, 0);
            commands.SetPipelineState(_sprites);
            SetInstanceOffset(commands, 0);
            commands.DrawInstanced(6, (uint)_backgroundCount, 0, 0);
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
        if (background == 0)
        {
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            commands.OMSetRenderTargets(Rtv(0));
            Bind(commands, 0);
            commands.SetPipelineState(_vignette);
            commands.DrawInstanced(3, 1, 0, 0);
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
        if ((int)_data[2].Y == 12)
            RenderLumiere(commands);
        else
        {
            if (_stageGraphicsCount > 0)
            {
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                commands.OMSetRenderTargets(Rtv(0));
                Bind(commands, 0);
                commands.SetPipelineState(_sprites);
                SetInstanceOffset(commands, (uint)_backgroundCount);
                commands.DrawInstanced(6, (uint)_stageGraphicsCount, 0, 0);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            }
            if ((int)_data[2].Y == 9 && _clockVertices != null && _atlas!.Lines.Length > 0)
            {
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                commands.OMSetRenderTargets(Rtv(0));
                commands.SetPipelineState(_clockwork);
                commands.SetGraphicsRootConstantBufferView(5, _clockConstants.GPUVirtualAddress);
                commands.SetGraphicsRootShaderResourceView(6, _clockVertices.GPUVirtualAddress);
                commands.DrawInstanced((uint)(_pendolo.Mesh.Length / 7), 1, 0, 0);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            }
            // Preserve the original browser's perceptual alpha compositing in private FP16 scratch.
            // Only the final copy converts to the host's linear scene, before every AA / reconstruction mode.
            CopyScratch(commands, 3, 4, ResourceStates.RenderTarget);
            DrawLyrics(commands, Rtv(1), _size.Width, _size.Height);
            commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
            CopyScratch(commands, 4, 3, ResourceStates.PixelShaderResource);
            if ((int)_data[2].Y == 11)
            {
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                commands.OMSetRenderTargets(Rtv(1));
                Bind(commands, 2);
                commands.SetPipelineState(_print);
                commands.DrawInstanced(3, 1, 0, 0);
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                CopyScratch(commands, 4, 3, ResourceStates.PixelShaderResource);
            }
        }
        Bind(commands, 2);
        if (_temporal)
        {
            if (_depthReadable)
                commands.ResourceBarrierTransition(_depth!, ResourceStates.NonPixelShaderResource, ResourceStates.DepthWrite);
            commands.ResourceBarrierTransition(_motion!, ResourceStates.NonPixelShaderResource, ResourceStates.RenderTarget);
            commands.ResourceBarrierTransition(_reactive!, ResourceStates.NonPixelShaderResource, ResourceStates.RenderTarget);
            CpuDescriptorHandle* targets = stackalloc CpuDescriptorHandle[3] { target, Rtv(2), Rtv(3) };
            commands.OMSetRenderTargets(3, targets, false, _dsv.GetCPUDescriptorHandleForHeapStart());
            commands.SetPipelineState(_temporalCopy!);
            commands.DrawInstanced(3, 1, 0, 0);
            commands.ResourceBarrierTransition(_depth!, ResourceStates.DepthWrite, ResourceStates.NonPixelShaderResource);
            _depthReadable = true;
            commands.ResourceBarrierTransition(_motion!, ResourceStates.RenderTarget, ResourceStates.NonPixelShaderResource);
            commands.ResourceBarrierTransition(_reactive!, ResourceStates.RenderTarget, ResourceStates.NonPixelShaderResource);
        }
        else
        {
            commands.OMSetRenderTargets(target);
            commands.SetPipelineState(_copy);
            commands.DrawInstanced(3, 1, 0, 0);
        }
    }
    private void Bind(ID3D12GraphicsCommandList commands, int table)
    {
        // Texture SRVs belong to pixel shaders; only the immutable upload instance buffer is shared with VS.
        commands.SetGraphicsRootDescriptorTable(1, _srv.GetGPUDescriptorHandleForHeapStart() + table * 7 * _srvStride);
        commands.SetGraphicsRootShaderResourceView(4, _instances.GPUVirtualAddress);
    }
    private void Background(ID3D12GraphicsCommandList commands, int shader, int texture, int table)
    {
        commands.ResourceBarrierTransition(_textures[texture]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(Rtv(texture - 3));
        Bind(commands, table);
        commands.SetPipelineState(_backgrounds[shader]!);
        commands.DrawInstanced(3, 1, 0, 0);
        commands.ResourceBarrierTransition(_textures[texture]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
    }
    public void RecordOverlay(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target)
    {
    }
    private void CopyScratch(ID3D12GraphicsCommandList commands, int source, int destination, ResourceStates after)
    {
        commands.ResourceBarrierTransition(_textures[source]!, ResourceStates.PixelShaderResource, ResourceStates.CopySource);
        commands.ResourceBarrierTransition(_textures[destination]!, ResourceStates.PixelShaderResource, ResourceStates.CopyDest);
        commands.CopyResource(_textures[destination]!, _textures[source]!);
        commands.ResourceBarrierTransition(_textures[source]!, ResourceStates.CopySource, ResourceStates.PixelShaderResource);
        commands.ResourceBarrierTransition(_textures[destination]!, ResourceStates.CopyDest, after);
    }
    private static void SetInstanceOffset(ID3D12GraphicsCommandList commands, uint offset)
        => commands.SetGraphicsRoot32BitConstants(3, 1, &offset, 0);
    private void DrawLyrics(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target, int width, int height)
    {
        commands.SetDescriptorHeaps(_srv);
        commands.SetGraphicsRootSignature(_root);
        commands.SetGraphicsRootConstantBufferView(0, _constants.GPUVirtualAddress);
        commands.SetGraphicsRootConstantBufferView(2, _lightConstants.GPUVirtualAddress);
        Bind(commands, 2);
        commands.RSSetViewport(0, 0, width, height, 0, 1);
        commands.RSSetScissorRect(width, height);
        commands.OMSetRenderTargets(target);
        commands.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        int chromatic = (int)_data[2].Y == 10 ? _sonnet.ChromaticCount : 0;
        if (chromatic > 0)
        {
            commands.SetPipelineState(_screenSprites);
            SetInstanceOffset(commands, (uint)(_backgroundCount + _stageGraphicsCount));
            commands.DrawInstanced(6, (uint)chromatic, 0, 0);
        }
        commands.SetPipelineState(_sprites);
        SetInstanceOffset(commands, (uint)(_backgroundCount + _stageGraphicsCount + chromatic));
        commands.DrawInstanced(6, (uint)(_count - _stageGraphicsCount - chromatic), 0, 0);
    }
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var p in _backgrounds)
            GpuGraphics.DisposeResource(p);
        foreach (var t in _textures)
            GpuGraphics.DisposeResource(t);
        foreach (var u in _uploads)
            GpuGraphics.DisposeResource(u);
        GpuGraphics.DisposeResource(_depth);
        GpuGraphics.DisposeResource(_motion);
        GpuGraphics.DisposeResource(_reactive);
        GpuGraphics.DisposeResource(_copy);
        GpuGraphics.DisposeResource(_sprites);
        GpuGraphics.DisposeResource(_screenSprites);
        GpuGraphics.DisposeResource(_temporalCopy);
        GpuGraphics.DisposeResource(_root);
        GpuGraphics.DisposeResource(_srv);
        GpuGraphics.DisposeResource(_rtv);
        GpuGraphics.DisposeResource(_dsv);
        GpuGraphics.DisposeResource(_instances);
        GpuGraphics.DisposeResource(_constants);
        GpuGraphics.DisposeResource(_lightConstants);
        GpuGraphics.DisposeResource(_lightField);
        _bloom?.Dispose();
        GpuGraphics.DisposeResource(_vignette);
        GpuGraphics.DisposeResource(_sora);
        GpuGraphics.DisposeResource(_print);
        GpuGraphics.DisposeResource(_latentTarget);
        GpuGraphics.DisposeResource(_latentComposite);
        GpuGraphics.DisposeResource(_clockwork);
        GpuGraphics.DisposeResource(_clockConstants);
        GpuGraphics.DisposeResource(_clockVertices);
    }
}
