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
internal sealed unsafe partial class FoliaGpuEffect : IGpuVisualizerEffect, ITemporalGpuEffect
{
    private readonly FoliaPlaybackState playback;
    private readonly bool _forceCpuGlyphs;
    private readonly FoliaGlyphStore _glyphStore;
    private FoliaGpuGlyphAtlas _glyphAtlas = null!;
    private int _glyphVersion;
    internal FoliaGpuEffect(FoliaPlaybackState playback, bool forceCpuGlyphs = false)
    {
        this.playback = playback;
        _displayClock = playback.Clock;
        _forceCpuGlyphs = forceCpuGlyphs;
        _glyphStore = new();
        _preparation = new(_glyphStore);
    }
    internal const string EffectId = "folia";
    private const int Capacity = 32768;
    private ID3D12Device _device = null!;
    private SpectrumAnalyzer _analyzer = null!;
    private ID3D12RootSignature _root = null!;
    private ID3D12DescriptorHeap _srv = null!, _rtv = null!, _dsv = null!;
    private ID3D12Resource _constants = null!, _instances = null!;
    private ID3D12Resource _lightConstants = null!;
    private Vector4* _lightData;
    private FoliaLumiere _lumiere = new();
    private FoliaCadenza _cadenza = new();
    private FoliaGlowWords _glowWords = new();
    private FoliaPartita _partita = new();
    private FoliaSonnet _sonnet = new();
    private FoliaDiorama _diorama = new();
    private FoliaFume _fume = new();
    private FoliaTilt _tilt = new();
    private FoliaPendolo _pendolo = new();
    private FoliaTempera _tempera = new();
    private FoliaMonet _monet = new();
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
    private ID3D12PipelineState _sonnetComposite = null!;
    private readonly FoliaRealtimeMotion _realtimeMotion = new();
    private string _lastMode = "", _lastBackground = "", _lastImageEffect = "";
    private bool _lastCoverBackground;
    private double _lastPosition;
    private FoliaClock _displayClock;
    public bool ResetHistoryRequested => _upload is { Complete: true }
        || _lyricsVisible != (_hasScene && playback.Song.Title.Length != 0)
        || _lastImageEffect != AppSettings.FoliaImageEffect || _lastCoverBackground != AppSettings.FoliaCoverBackground
        || _hasScene && Math.Abs((FollowsPlaybackClock(playback.Song) ? playback.Clock : _displayClock).Now - _lastPosition) > .5;

    private bool FollowsPlaybackClock(FoliaSong song) => _hasScene && FoliaSceneRequest.SameText(_song!, song);
    private Vector4* _data;
    private FoliaInstance* _mapped;
    private FoliaInstance[] _prepared = null!;
    private readonly ID3D12PipelineState?[] _backgrounds = new ID3D12PipelineState?[8];
    private ID3D12PipelineState _copy = null!, _sprites = null!, _screenSprites = null!;
    private ID3D12PipelineState? _temporalCopy;
    private readonly ID3D12Resource?[] _textures = new ID3D12Resource?[7];
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
    private double _animation;
    public TemporalCamera Camera => new(.1f, 100, MathF.PI / 3);
    public ID3D12Resource Depth => _depth!;
    public ID3D12Resource Motion => _motion!;
    public ID3D12Resource Reactive => _reactive!;

    public void Initialize(in GpuEffectServices services)
    {
        _device = services.Device;
        _analyzer = services.Analyzer;
        _glyphAtlas = new(_device, _glyphStore, _forceCpuGlyphs);
        _srvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView);
        _rtvStride = (int)_device.GetDescriptorHandleIncrementSize(DescriptorHeapType.RenderTargetView);
        _srv = _device.CreateDescriptorHeap(new(DescriptorHeapType.ConstantBufferViewShaderResourceViewUnorderedAccessView, 36, DescriptorHeapFlags.ShaderVisible));
        _rtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 6));
        _dsv = _device.CreateDescriptorHeap(new(DescriptorHeapType.DepthStencilView, 1));
        _constants = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(768), ResourceStates.GenericRead);
        _data = _constants.Map<Vector4>(0);
        _lightConstants = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(1024), ResourceStates.GenericRead);
        _lightData = _lightConstants.Map<Vector4>(0);
        _clockConstants = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
            ResourceDescription.Buffer(768), ResourceStates.GenericRead);
        _clockTransforms = _clockConstants.Map<Vector4>(0);
        _instances = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer((ulong)(Capacity * sizeof(FoliaInstance))), ResourceStates.GenericRead);
        _mapped = _instances.Map<FoliaInstance>(0);
        _prepared = new FoliaInstance[Capacity];
        _root = _device.CreateRootSignature(new RootSignatureDescription1(RootSignatureFlags.None,
            [new RootParameter1(RootParameterType.ConstantBufferView,new RootDescriptor1(0,0),ShaderVisibility.All),
             new RootParameter1(new RootDescriptorTable1([new DescriptorRange1(DescriptorRangeType.ShaderResourceView,5,0,0,0,DescriptorRangeFlags.None),new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,6,0,6,DescriptorRangeFlags.None),new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,8,0,7,DescriptorRangeFlags.None),new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,9,0,5,DescriptorRangeFlags.None),new DescriptorRange1(DescriptorRangeType.ShaderResourceView,1,10,0,8,DescriptorRangeFlags.None)]),ShaderVisibility.Pixel),
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
        _backgroundSnapshot = Pipeline("Stage", "Fullscreen", "BackgroundSnapshotPS");
        _backgroundBlend = Pipeline("Stage", "Fullscreen", "BackgroundBlendPS", sprites: true);
        _print = Pipeline("Print", "Fullscreen", "PS");
        _sonnetComposite = Pipeline("Sonnet", "Fullscreen", "PS", sprites: true);
        _sora = Pipeline("Sora", "VS", "PS", sprites: true);
        _latentComposite = Pipeline("Stage", "Fullscreen", "LatentCompositePS");
        _clockwork = Pipeline("Stage", "ClockworkVS", "ClockworkPS", sprites: true);
        SetImage(2, 128, 128, FoliaAssets.Noise(), Format.B8G8R8A8_UNorm);
        _atlas = FoliaTextAtlas.Placeholder;
        _song = FoliaSong.Empty;
        SetImage(0, 1, 1, _atlas.Pixels, Format.R16G16B16A16_Float);
        SetImage(1, 1, 1, EmptyCover, Format.R8G8B8A8_UNorm);
        SetImage(5, 1, 1, _atlas.ShadowPixels, Format.R8_UNorm);
        _modeAtlasSource = _atlas;
    }

    private static StaticSamplerDescription Sampler(uint index, TextureAddressMode address) => new(index, Filter.MinMagMipLinear, address, address, address, 0, 0, ComparisonFunction.Never, StaticBorderColor.TransparentBlack, 0, float.MaxValue, ShaderVisibility.All, 0);
    private ID3D12PipelineState Pipeline(string file, string vs, string ps, bool sprites = false, bool temporal = false, bool screen = false)
    {
        string shader = FoliaShaderLibrary.Background(file);
        return GpuPipelineCache.Create(_device, new GraphicsPipelineStateDescription
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
    }, $"Folia/root-v8-theme-crossfade-keyword-glow/{file}/{vs}/{ps}/{sprites}/{temporal}/{screen}");
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
        ResetBackgroundTransition();
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
        GpuGraphics.DisposeResource(_textures[6]);
        _textures[6] = _temporal ? Texture(Format.R16G16B16A16_Float, ResourceStates.PixelShaderResource, ResourceFlags.None) : null;
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
        AdvanceBackgroundTransition(elapsedSeconds);
        for (int i = 0; i < _uploads.Length; i++)
            if (!_pending[i])
            {
                GpuGraphics.DisposeResource(_uploads[i]);
                _uploads[i] = null;
            }
        bool songChanged = UpdatePreparedScene();
        var song = _song!;
        string mode = _lyricsVisible && _atlas!.Lines.Length > 0 ? _activeRequest.Mode : "classic";
        string backgroundMode = _hasScene ? _activeRequest.Background : "common";
        float fontScale = _hasScene ? _activeRequest.Scale : 1;
        // The visible scene keeps its own immutable clock while the next track
        // prepares. Its Now still advances; commit switches scene and clock together.
        if (FollowsPlaybackClock(playback.Song) || !_lyricsVisible)
            _displayClock = playback.Clock;
        var clock = _displayClock;
        double position = clock.Now;
        bool modeChanged = _lastMode != mode;
        bool backgroundChanged = _lastBackground != backgroundMode;
        bool motionReset = songChanged || modeChanged || Math.Abs(position - _lastPosition) > .5 || elapsedSeconds > .25;
        var motion = _realtimeMotion.Update(_analyzer.LatestFeatures, elapsedSeconds,
            AppSettings.FoliaRealtimeMotion && mode is "sonnet" or "diorama" or "pendolo", clock.Playing, motionReset);
        _sonnet.Motion = mode == "sonnet" ? motion.Scale(AppSettings.FoliaSonnetMotionStrength) : default;
        _diorama.SetMotion(mode == "diorama" ? motion.Scale(AppSettings.FoliaDioramaMotionStrength) : default, position, motionReset);
        _pendolo.Motion = mode == "pendolo" ? motion.Scale(AppSettings.FoliaPendoloMotionStrength) : default;
        _lastMode = mode;
        _lastBackground = backgroundMode;
        _lastImageEffect = AppSettings.FoliaImageEffect;
        _lastCoverBackground = AppSettings.FoliaCoverBackground;
        _lastPosition = position;
        float energy = Math.Clamp(_analyzer.LatestFeatures[FeatureIndex.Energy], 0, 1);
        _sonnet.SetAudio(_analyzer.LatestFeatures, elapsedSeconds, clock.Playing && mode == "sonnet");
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
        // PrepareFrame follows the preceding-frame fence. Exclusive mode resources can
        // be released here instead of retaining the largest visited mode until shutdown.
        if (mode != "lumiere" && _bloom != null)
        {
            _bloom.Dispose();
            _bloom = null;
        }
        if (backgroundMode != "latent" && _latentTarget != null)
        {
            GpuGraphics.DisposeResource(_latentTarget);
            _latentTarget = null;
        }
        if (mode != "pendolo" && _clockVertices != null)
        {
            GpuGraphics.DisposeResource(_clockVertices);
            _clockVertices = null;
            _clockMeshVersion = -1;
        }
        _data[0] = new(_size.Width, _size.Height, _size.OutputWidth, _size.OutputHeight);
        _data[1] = new(backgroundMode is "monet" or "nomand" ? (float)_backgroundTime : (float)_animation, (float)position, energy, (float)elapsedSeconds);
        _data[2] = new(FoliaSettings.IndexOf(FoliaSettings.BackgroundModes, backgroundMode), FoliaSettings.IndexOf(FoliaSettings.LyricModes, mode), FoliaSettings.IndexOf(FoliaSettings.ImageEffects, AppSettings.FoliaImageEffect), 1);
        _data[27] = _data[3];
        _data[3] = new(_temporal ? _frame.JitterX : 0, _temporal ? _frame.JitterY : 0, _frame.Reset ? 1 : 0, 0);
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
            if (_latentTarget == null)
                ResizeLatent();
        }
        _latent.Update(backgroundMode == "latent", !clock.Playing, elapsedSeconds, _analyzer.LatestFeatures,
            new Span<Vector4>(_data + 17, 6), out _data[23], out _data[24], out _data[25]);
        // Upload heaps are write-combined. Layout and camera passes read earlier instances;
        // perform those passes in cached CPU memory, then upload once with the SIMD span copy.
        var instances = _prepared.AsSpan();
        float dpi = _size.PixelsPerDip;
        float layoutWidth = _hasScene ? _activeRequest.Width : _size.OutputWidth / dpi;
        float layoutHeight = _hasScene ? _activeRequest.Height : _size.OutputHeight / dpi;
        _backgroundCount = mode == "fume" && backgroundMode == "common" ? 0 : _geometry.Write(instances, song, (int)_data[2].X, (float)_animation, energy, layoutWidth, layoutHeight, !clock.Playing, _analyzer.LatestFeatures);
        if ((int)_data[2].Y == 12)
        {
            if (_bloom == null)
            {
                _bloom = new FoliaBloom(_device);
                _bloom.Resize(_size.Width, _size.Height, _textures[4]!);
            }
            _lumiere.Update(new Span<Vector4>(_lightData, 52), song, position, _size.Width, _size.Height, _analyzer.LatestFeatures[FeatureIndex.Bass]);
        }
        if ((int)_data[2].Y == 1)
        {
            _cadenza.FrameDelta = (float)elapsedSeconds;
            _cadenza.Energy = clock.Playing ? energy : 0;
        }
        if ((int)_data[2].Y == 3)
        {
            _fume.SetAudio(_analyzer.LatestFeatures, clock.Playing);
        }
        if ((int)_data[2].Y == 8)
        {
            _diorama.Energy = energy;
            _diorama.FrameDelta = (float)elapsedSeconds;
            _diorama.SetSurfaceAudio(_analyzer.LatestFeatures, elapsedSeconds, position, clock.Playing, motionReset);
            if (modeChanged)
                _diorama.ResetMotion();
        }
        if ((int)_data[2].Y == 9)
        {
            _pendolo.Update(elapsedSeconds, position, FoliaLyricLayout.FindActiveLine(_atlas.Lines, position), _analyzer.LatestFeatures[FeatureIndex.Bass], clock.Playing, lineIndex, _atlas.Lines.Length);
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
        _glowWords.FrameDelta = (float)elapsedSeconds;
        if (_glowWords.Chat is { } chat) chat.Delta = (float)elapsedSeconds;
        _glowWords.Energy = clock.Playing ? energy : 0;
        _monet.FrameDelta = (float)elapsedSeconds;
        _glowWords.Begin(lineIndex, (int)_data[2].Y, position);
        _count = _lyricsVisible ? FoliaLyricLayout.Write(instances[_backgroundCount..], _atlas, song, position, (float)_lineAmbient, (int)_data[2].Y, layoutWidth, layoutHeight, fontScale, _lumiere, _cadenza, _tempera, _sonnet, _diorama, _fume, _tilt, _pendolo, _glowWords, _partita, _monet) : 0;
        if (mode == "sonnet")
        {
            var overlay = new FoliaPainter(instances[(_backgroundCount + _count)..], layoutWidth, layoutHeight);
            _sonnet.WriteOverlay(ref overlay, song.Palette[1]);
            _count += overlay.Count;
        }
        _data[26] = mode == "sonnet" ? _sonnet.PostProcess : new(_tempera.PostSeed, 0, 0, 0);
        _data[28] = mode == "pendolo" ? new(_pendolo.Motion.Onset, _pendolo.Motion.Low, 0, 0) : Vector4.Zero;
        _data[29] = new(AppSettings.FoliaSonnetPostProcess ? 1 : 0, 0, 0, 0);
        WriteTuning();
        _stageGraphicsCount = mode switch { "tempera" => _tempera.GraphicsCount, "sonnet" => _sonnet.GraphicsCount,
            "pendolo" when _atlas.Lines.Length > 0 => _pendolo.GraphicsCount, _ => 0 };
        _data[8] = new(_tempera.InkColor, 1);
        _data[9] = new(_tempera.PaperColor, 1);
        instances[..(_backgroundCount + _count)].CopyTo(new Span<FoliaInstance>(_mapped, Capacity));
    }

    public void RecordScene(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target)
    {
        _glyphAtlas.RecordGrowth(commands);
        _upload?.Record(commands);
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
        RecordBackgroundTransition(commands);
        if ((int)_data[2].Y == 12)
            RenderLumiere(commands);
        else if ((int)_data[2].Y == 10)
            RenderSonnet(commands);
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
            if ((int)_data[2].Y == 11 && AppSettings.FoliaTemperaPostProcess)
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
        if (_temporal)
            CopyScratch(commands, 3, 6, ResourceStates.PixelShaderResource);
    }
    private void Bind(ID3D12GraphicsCommandList commands, int table)
    {
        // Texture SRVs belong to pixel shaders; only the immutable upload instance buffer is shared with VS.
        commands.SetGraphicsRootDescriptorTable(1, _srv.GetGPUDescriptorHandleForHeapStart() + table * 9 * _srvStride);
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
        try { _preparation.Dispose(); }
        catch (Exception error) { App.WriteCrashLog("Folia preparation shutdown", error.Message, error); }
        _upload?.Dispose();
        _activeScene?.Dispose();
        _upload = _activeScene = null;
        _glyphAtlas?.Dispose();
        _glyphStore.Dispose();
        _hasScene = _hasRequest = _lyricsVisible = false;
        _atlas = null;
        _song = null;
        _modeAtlasSource = null;
        _requested = _activeRequest = default;
        _prepared = [];
        _lumiere = null!;
        _cadenza = null!;
        _glowWords = null!;
        _partita = null!;
        _sonnet = null!;
        _diorama = null!;
        _fume = null!;
        _tilt = null!;
        _pendolo = null!;
        _tempera = null!;
        _monet = null!;
        _latent.SetPalette(FoliaSong.Empty, []);
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
        ResetBackgroundTransition();
        GpuGraphics.DisposeResource(_backgroundSnapshot);
        GpuGraphics.DisposeResource(_backgroundBlend);
        GpuGraphics.DisposeResource(_sora);
        GpuGraphics.DisposeResource(_print);
        GpuGraphics.DisposeResource(_sonnetComposite);
        GpuGraphics.DisposeResource(_latentTarget);
        GpuGraphics.DisposeResource(_latentComposite);
        GpuGraphics.DisposeResource(_clockwork);
        GpuGraphics.DisposeResource(_clockConstants);
        GpuGraphics.DisposeResource(_clockVertices);
    }
}
