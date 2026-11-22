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
    private ID3D12PipelineState? _hanabiSky, _inkField, _inkDevelop, _windowMask, _windowScene, _machiPanel;
    private ID3D12Resource? _inkTint, _windowTexture, _panelTexture;
    private ID3D12PipelineState? _windowFogPipeline;
    private ID3D12Resource? _windowFog;
    private ID3D12DescriptorHeap? _stageRtv;
    private int _windowWidth, _windowHeight, _nativeForegroundCount, _metaphorShot = -1;
    private int _windowFogWidth, _windowFogHeight;
    private float _hanabiBloom;
    private bool _negativeInk;
    private int _nebbiaWindowSetting;
    private double _nebbiaTime;
    private FoliaMachi? _machi;
    private string? _installedMachiScene;
    private FoliaMachi.Plan? _machiPlan;
    private MachiDxrBackend? _machiBackend;
    private MachiDxrFrame _machiFrame;
    private Matrix4x4[] _actorTransforms = [];
    private ID3D12PipelineState? _machiAtmosphere, _machiLens, _machiWeather, _machiSteam;
    private float _machiMotionWeight;
    private bool _hardwareRaytracing, _vendorInitializationReady;
    private GpuRenderSize _machiTraceSize;
    private ReconstructionMode _machiSuperResolutionMode;
    private string _machiDlssPreset = "k";
    private MachiDenoisingMode _machiDenoising;
    private (FoliaSong Song, string Scene, string Denoiser)? _failedMachiSelection;
    private void RecordMachiFailure(in FoliaSceneRequest request)
    {
        string requested = AppSettings.FoliaMachiDenoiser;
        // A failure in the old mode must not prevent a newly requested mode from starting.
        if (ResolveMachiDenoising(requested) == MachiDenoisingRequested)
            _failedMachiSelection = (request.Song, request.MachiScene, requested);
    }
    public bool MachiHdrOutput { private get; set; }
    public bool UsesMachiReconstruction => _hardwareRaytracing && _failedMachiSelection == null
        && (AppSettings.FoliaLyricMode == "machi" || (_hasScene && _activeRequest.Mode == "machi"));
    public MachiDenoisingMode RequestedMachiDenoising => ResolveMachiDenoising(AppSettings.FoliaMachiDenoiser);
    private MachiDenoisingMode ResolveMachiDenoising(string requested) => requested switch
    {
        "none" => MachiDenoisingMode.None,
        "dlss-rr" when _vendorInitializationReady => MachiDenoisingMode.RayReconstruction,
        _ => MachiDenoisingMode.Nrd
    };
    public ReconstructionMode MachiSuperResolution => _machiBackend?.SuperResolutionActive ?? ReconstructionMode.Off;
    public bool MachiUnsupportedScale => _machiBackend?.SuperResolutionResult == -30;
    public void ConfigureMachi(in GpuRenderSize size, ReconstructionMode mode, string preset, MachiDenoisingMode denoising)
    {
        _machiTraceSize = size;
        _machiSuperResolutionMode = mode;
        _machiDlssPreset = preset;
        _machiDenoising = denoising;
    }
    public void ResetMachiHistory() => _machiBackend?.ResetHistory();
    public MachiRenderStatus MachiStatus
    {
        get
        {
            if (_failedMachiSelection != null) return MachiRenderStatus.Unavailable;
            if (_machiBackend == null) return MachiRenderStatus.Idle;
            bool superResolution = MachiSuperResolution >= ReconstructionMode.XeSS;
            if (_machiBackend.DenoisingDisabled)
                return superResolution ? MachiRenderStatus.RawSuperResolution : MachiRenderStatus.Raw;
            if (_machiBackend.RayReconstructionActive) return MachiRenderStatus.RayReconstruction;
            if (!_machiBackend.DenoiserAvailable) return MachiRenderStatus.Unavailable;
            if (MachiRayReconstructionRequested) return MachiRenderStatus.NrdFallback;
            return superResolution ? MachiRenderStatus.NrdSuperResolution : MachiRenderStatus.Nrd;
        }
    }
    public void EnableVendorInitialization() => _vendorInitializationReady = true;

    internal static FoliaNativeStage NativeStage(ReadOnlySpan<char> mode) => mode switch
    {
        "hanabi" => FoliaNativeStage.Hanabi,
        "metaphor" => FoliaNativeStage.Metaphor,
        "nebbia" => FoliaNativeStage.Nebbia,
        "machi" => FoliaNativeStage.Machi,
        _ => FoliaNativeStage.None
    };

    private static int NebbiaWindowSetting()
    {
        int behind = AppSettings.FoliaNebbiaBehind switch { "aquarium" => 1, "backdrop" => 2, _ => 0 };
        int glass = AppSettings.FoliaNebbiaGlass switch { "dew" => 1, "snow" => 2, _ => 0 };
        return behind + glass * 3;
    }

    private FoliaNativeStage ActiveNativeStage()
    {
        if (!_hasScene)
            return FoliaNativeStage.None;
        if (_activeRequest.Mode == "machi" && (_temporal || _failedMachiSelection != null || _machi == null))
            return FoliaNativeStage.None;
        return NativeStage(_activeRequest.Mode);
    }

    private void PrepareStageTargets(FoliaNativeStage stage)
    {
        if (stage != FoliaNativeStage.Machi && _machiEnvironment.TimeOfDay != null
            && _activeRequest.Mode != "machi" && AppSettings.FoliaLyricMode != "machi")
            _machiEnvironment = default;
        if (stage == _targetStage && (stage != FoliaNativeStage.Machi || _machiPlan!.SceneId == _installedMachiScene))
            return;
        if (stage != FoliaNativeStage.Machi || _targetStage != stage) ReleaseStageTargets();
        switch (stage)
        {
            case FoliaNativeStage.Hanabi:
                _hanabiSky ??= Pipeline("LyricMaterials", "Fullscreen", "HanabiSkyPS");
                break;
            case FoliaNativeStage.Metaphor:
                _inkField ??= StagePipeline("Stage", "SpriteVS", "InkFieldPS", new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One),
                    Format.R16G16B16A16_Float, Format.R16G16B16A16_Float, depth: false);
                _stageRtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 1));
                _inkTint = StageTarget(Format.R16G16B16A16_Float, _size.Width, _size.Height, 0);
                _inkDevelop ??= Pipeline("LyricMaterials", "Fullscreen", "InkDevelopPS");
                break;
            case FoliaNativeStage.Nebbia:
                var maximum = new BlendDescription(Blend.One, Blend.One, Blend.One, Blend.One);
                maximum.RenderTarget[0].BlendOperation = BlendOperation.Max;
                maximum.RenderTarget[0].BlendOperationAlpha = BlendOperation.Max;
                _windowMask ??= StagePipeline("Stage", "SpriteVS", "WindowMaskPS", maximum, Format.R8G8B8A8_UNorm);
                _windowScene ??= Pipeline("LyricMaterials", "Fullscreen", "NebbiaPS");
                _windowFogPipeline ??= Pipeline("LyricMaterials", "Fullscreen", "NebbiaFogPS");
                _stageRtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 2));
                _windowWidth = Math.Max(1, (_size.Width + 1) / 2);
                _windowHeight = Math.Max(1, (_size.Height + 1) / 2);
                _windowTexture = StageTarget(Format.R8G8B8A8_UNorm, _windowWidth, _windowHeight, 0);
                _windowFogWidth = 2 * Math.Max(1, (_size.Width + 3) / 4);
                _windowFogHeight = Math.Max(1, (_size.Height + 3) / 4);
                _windowFog = StageTarget(Format.R16G16B16A16_Float, _windowFogWidth, _windowFogHeight, 1);
                break;
            case FoliaNativeStage.Machi:
                try
                {
                    _machiPanel ??= Pipeline("Stage", "MachiPanelVS", "MachiPanelPS", sprites: true);
                    _machiAtmosphere ??= Pipeline("Machi", "Fullscreen", "AtmospherePS");
                    _machiLens ??= Pipeline("Machi", "Fullscreen", "LensPS");
                    _machiSteam ??= StagePipeline("Machi", "SteamVS", "SteamPS",
                        new BlendDescription(Blend.One, Blend.InverseSourceAlpha, Blend.One, Blend.InverseSourceAlpha),
                        Format.R16G16B16A16_Float);
                    _machiWeather ??= StagePipeline("Machi", "WeatherVS", "WeatherPS", BlendDescription.AlphaBlend, Format.R16G16B16A16_Float);
                    var asset = _machi!;
                    if (_machiBackend == null)
                    {
                        _stageRtv = _device.CreateDescriptorHeap(new(DescriptorHeapType.RenderTargetView, 1));
                        _panelTexture = StageTarget(Format.R16G16B16A16_Float, FoliaMachi.PanelWidth, FoliaMachi.PanelHeight, 0);
                        var traceSize = MachiTraceSize(_size);
                        _machiBackend = new(_device, traceSize.Width, traceSize.Height, asset.Mesh, asset.Indices,
                            asset.SignPixels, asset.SignWidth, asset.SignHeight, asset.StaticIndexCount, asset.Actors,
                            preferRayReconstruction: MachiRayReconstructionRequested, lights: asset.Lights,
                            outputWidth: traceSize.OutputWidth, outputHeight: traceSize.OutputHeight,
                            superResolutionMode: _machiSuperResolutionMode, preset: _machiDlssPreset,
                            disableDenoising: MachiDenoisingRequested == MachiDenoisingMode.None, tracePipeline: _machiTracePipeline!);
                    }
                    else
                    {
                        _machiBackend.ReplaceScene(asset.Mesh, asset.Indices, asset.SignPixels, asset.SignWidth,
                            asset.SignHeight, asset.StaticIndexCount, asset.Actors, asset.Lights);
                    }
                    if (!_machiBackend.DenoisingDisabled && !_machiBackend.DenoiserAvailable && !_machiBackend.RayReconstructionAvailable)
                        throw new InvalidOperationException($"Machi NRD initialization failed: {_machiBackend.DenoiserResult}.");
                    _actorTransforms = new Matrix4x4[asset.Actors.Length];
                    _machiDoor = null;
                    SetMachiWeatherLayout(asset);
                    _installedMachiScene = _machiPlan!.SceneId;
                    // Only a successfully installed district selects a new water pattern.
                    _machiEnvironment.SetPuddleScene(_installedMachiScene);
                }
                catch (Exception ex)
                {
                    App.WriteCrashLog("Machi DXR initialization", ex.Message, ex);
                    RecordMachiFailure(_activeRequest);
                    ReleaseStageTargets();
                    _nativeStage = FoliaNativeStage.None;
                    Descriptors();
                    return;
                }
                break;
        }
        _targetStage = stage;
        Descriptors();
    }

    private GpuRenderSize MachiTraceSize(in GpuRenderSize stage) =>
        _machiTraceSize.OutputWidth == stage.Width && _machiTraceSize.OutputHeight == stage.Height
            ? _machiTraceSize : stage with { OutputWidth = stage.Width, OutputHeight = stage.Height };
    private MachiDenoisingMode MachiDenoisingRequested => _machiTraceSize.OutputWidth != 0 ? _machiDenoising : RequestedMachiDenoising;
    private bool MachiRayReconstructionRequested => MachiDenoisingRequested == MachiDenoisingMode.RayReconstruction;

    private ID3D12PipelineState StagePipeline(string file, string vs, string ps, in BlendDescription blend, Format color, Format geometry = Format.Unknown, bool depth = true)
    {
        string source = FoliaShaderLibrary.Background(file);
        bool multipleTargets = geometry != Format.Unknown;
        bool mesh = multipleTargets && depth;
        int pipelineVersion = file == "Machi" ? 13 : 10;
        return GpuPipelineCache.Create(_device, new GraphicsPipelineStateDescription
        {
            RootSignature = _root,
            VertexShader = ShaderCompiler.Compile(source, vs, "vs_5_0"),
            PixelShader = ShaderCompiler.Compile(source, ps, "ps_5_0"),
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = multipleTargets ? [color, geometry] : [color],
            DepthStencilFormat = mesh ? Format.D32_Float : Format.Unknown,
            BlendState = blend,
            SampleDescription = new(1, 0),
            SampleMask = uint.MaxValue,
            RasterizerState = new() { CullMode = CullMode.None, FillMode = FillMode.Solid },
            DepthStencilState = new() { DepthEnable = mesh, DepthWriteMask = DepthWriteMask.All, DepthFunc = ComparisonFunction.LessEqual, StencilEnable = false }
        }, $"Folia/root-v{pipelineVersion}-native-stages/{file}/{vs}/{ps}/{color}/{geometry}/{mesh}");
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
        GpuGraphics.DisposeResource(_windowFog);
        _windowFog = null;
        GpuGraphics.DisposeResource(_inkTint);
        _inkTint = null;
        GpuGraphics.DisposeResource(_panelTexture);
        GpuGraphics.DisposeResource(_machiBackend);
        _machiBackend = null;
        _installedMachiScene = null;
        _actorTransforms = [];
        GpuGraphics.DisposeResource(_stageRtv);
        _windowTexture = _panelTexture = null;
        _stageRtv = null;
        _targetStage = FoliaNativeStage.None;
    }

    private void DisposeNativeStages()
    {
        ReleaseStageTargets();
        DisposeMachiPipeline();
        GpuGraphics.DisposeResource(_hanabiSky);
        GpuGraphics.DisposeResource(_inkField);
        GpuGraphics.DisposeResource(_inkDevelop);
        GpuGraphics.DisposeResource(_windowMask);
        GpuGraphics.DisposeResource(_windowScene);
        GpuGraphics.DisposeResource(_windowFogPipeline);
        GpuGraphics.DisposeResource(_machiPanel);
        GpuGraphics.DisposeResource(_machiWeather);
        GpuGraphics.DisposeResource(_machiSteam);
        GpuGraphics.DisposeResource(_machiAtmosphere);
        GpuGraphics.DisposeResource(_machiLens);
        _hanabi = null;
        _metaphor = null;
        _nebbia = null;
        _machi = null;
        _machiPlan = null;
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
                if (_lyricsVisible)
                    _hanabi!.Write(ref painter, position, motion);
                _hanabiBloom = _lyricsVisible ? _hanabi!.Bloom(position) : .36f;
                break;
            case FoliaNativeStage.Metaphor:
                Vector4 development = default;
                if (_lyricsVisible)
                    _metaphorShot = _metaphor!.Write(ref painter, position, scale, motion, out development);
                _data[39] = development;
                var inkPalette = _metaphor!.Palette(development.W > .5f, _negativeInk);
                _data[4] = new(inkPalette.Paper, 1);
                _data[5] = new(inkPalette.Ink, 1);
                _data[6] = new(inkPalette.Tone, 1);
                _data[7] = new(inkPalette.Accent, 1);
                break;
            case FoliaNativeStage.Nebbia:
                Vector2 focus = _nebbia!.Write(ref painter, atlas, position, anchor, _nebbiaWindowSetting >= 6, out float radius, out float intensity);
                _data[38].Y = radius;
                _data[38].Z = intensity;
                _data[38].W = _nebbiaWindowSetting;
                _data[39] = new(focus, (float)_nebbiaTime, 0);
                break;
            case FoliaNativeStage.Machi:
                int active = atlas.Timeline.FindActive(anchor, position);
                int board = _machiPlan!.LyricTarget(active);
                UpdateMachiFrame(song, position, anchor, board, reset);
                _nativeForegroundCount = _machiPlan.WritePanel(instances, board, position, _animation, scale, _lyricsVisible);
                if (anchor < 0) return _nativeForegroundCount;
                var subtitles = new FoliaPainter(instances[_nativeForegroundCount..], width, height);
                // The traced stage is already linear HDR. Use linear white for the final overlay.
                var subtitleWhite = new Vector3(.807f, .828f, .933f);
                FoliaLyricLayout.Subtitle(ref subtitles, atlas, active, subtitleWhite, subtitleWhite);
                return _nativeForegroundCount + subtitles.Count;
        }
        // Subtitle sprites follow the native foreground and draw after its compositing pass.
        _nativeForegroundCount = painter.Count;
        if (_nativeStage == FoliaNativeStage.Hanabi && _lyricsVisible)
            _hanabi!.WriteCredits(ref painter, position);
        if (anchor >= 0)
        {
            Vector3 textColor = _nativeStage == FoliaNativeStage.Metaphor
                ? new(_data[5].X, _data[5].Y, _data[5].Z) : new(.91f, .92f, .97f);
            FoliaLyricLayout.Subtitle(ref painter, atlas, atlas.Timeline.FindActive(anchor, position), textColor, textColor);
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
                _bloom.Apply(commands, _textures[3]!, Rtv(0), false, _hanabiBloom, hanabiScreen: true);
                break;
            case FoliaNativeStage.Metaphor:
                var field = Rtv(1);
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                commands.ClearRenderTargetView(field, new Color4(0, 0, 0, 0));
                var tint = NativeRtv(0);
                commands.ResourceBarrierTransition(_inkTint!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                commands.ClearRenderTargetView(tint, new Color4(0, 0, 0, 0));
                var targets = stackalloc CpuDescriptorHandle[2] { field, tint };
                Bind(commands, 0);
                commands.OMSetRenderTargets(2, targets, false, null);
                commands.SetPipelineState(_inkField!);
                SetInstanceOffset(commands, 0);
                commands.DrawInstanced(6, (uint)_nativeForegroundCount, 0, 0);
                commands.ResourceBarrierTransition(_inkTint!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                RecordFullStage(commands, Rtv(0), _inkDevelop!, 1);
                commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                break;
            case FoliaNativeStage.Nebbia:
                if (_nebbiaWindowSetting % 3 == 2)
                {
                    RecordSharedBackground(commands);
                    CopyScratch(commands, 3, 4, ResourceStates.PixelShaderResource);
                }
                else
                {
                    commands.ResourceBarrierTransition(_windowFog!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
                    commands.RSSetViewport(0, 0, _windowFogWidth, _windowFogHeight, 0, 1);
                    commands.RSSetScissorRect(_windowFogWidth, _windowFogHeight);
                    RecordFullStage(commands, NativeRtv(1), _windowFogPipeline!);
                    commands.ResourceBarrierTransition(_windowFog!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
                }
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
            DrawRange(commands, Rtv(0), _backgroundCount + _nativeForegroundCount, _count - _nativeForegroundCount);
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
    }

    // Vector4 offsets in the MachiPost cbuffer.
    private const int MachiDepthPostIndex = 213, MachiSteamStart = MachiDepthPostIndex + 1;
    private const int MachiSteamCapacity = 2, MachiSteamStride = 5;
    private const int MachiSteamLighting = MachiSteamStart + MachiSteamCapacity * MachiSteamStride;
    private int _machiDrips, _machiSteamCount;
    private void SetMachiWeatherLayout(FoliaMachi asset)
    {
        var layout = asset.Weather;
        int roofs = Math.Min(layout.Roofs.Length, 4);
        int glints = Math.Min(layout.Glints.Length, 3);
        _machiDrips = Math.Min(layout.Drips.Length, 96);
        _lightData[12] = new(layout.GroundY, MathF.Sin(layout.WindYaw) * layout.WindSlant,
            MathF.Cos(layout.WindYaw) * layout.WindSlant, roofs);
        _machiSteamCount = asset.Steam.Length;
        if (_machiSteamCount > MachiSteamCapacity)
            throw new InvalidOperationException("Machi steam layout exceeds its shader capacity.");
        for (int i = 0; i < _machiSteamCount; ++i)
        {
            var emitter = asset.Steam[i];
            int slot = MachiSteamStart + i * MachiSteamStride;
            _lightData[slot] = new(emitter.Position, emitter.Height);
            _lightData[slot + 1] = new(emitter.BaseRadius, emitter.TipRadius, emitter.Extinction, emitter.WeatherResponse);
            _lightData[slot + 2] = new(emitter.Drift, emitter.Turbulence, emitter.SunVisibility);
            _lightData[slot + 3] = new(emitter.LightPosition, 0);
            _lightData[slot + 4] = new(emitter.LightRadiance, 0);
        }
        _lightData[13] = default;
        new Span<Vector4>(_lightData + 14, 4).Clear();
        for (int i = 0; i < roofs; ++i)
        {
            var r = layout.Roofs[i];
            _lightData[14+i] = new(r.X0, r.Z0, r.X1, r.Z1);
            ((float*)(_lightData+13))[i] = r.Y;
        }
        for (int i = 0; i < 3; ++i)
            _lightData[18+i] = i < glints ? new(layout.Glints[i].Position, layout.Glints[i].Radius) : default;
        for (int i = 0; i < _machiDrips; ++i)
        {
            var d = layout.Drips[i];
            _lightData[21+2*i] = new(d.X, d.Z, d.From, d.To);
            _lightData[22+2*i] = new(d.Period, d.Phase, 0, 0);
        }
    }

    private FoliaMachi.EnvironmentState _machiEnvironment;
    private float _machiChorus, _machiVacancyChorus;
    private float? _machiDoor;
    private double _machiCycleSeconds;
    private void UpdateMachiFrame(FoliaSong song, double position, int anchor, int board, bool resetLyrics)
    {
        bool firstEnvironmentFrame = _machiEnvironment.TimeOfDay == null;
        float delta = Math.Clamp(_data[1].W, 0, .1f);
        string timeOfDay = AppSettings.FoliaMachiTimeOfDay;
        bool cycling = timeOfDay == "cycle";
        bool startCycle = cycling && _machiEnvironment.TimeOfDay != timeOfDay;
        if (cycling)
            _machiCycleSeconds = startCycle ? 0 : (_machiCycleSeconds + delta) % FoliaMachi.CycleDurationSeconds;
        var environment = FoliaMachi.UpdateEnvironment(ref _machiEnvironment, timeOfDay,
            AppSettings.FoliaMachiWeather, song, DateTime.Now, delta, firstEnvironmentFrame, _machiPlan!.SceneId, _machiCycleSeconds);
        _machiMotionWeight = Math.Clamp(_machiMotionWeight + (AppSettings.FoliaMachiMotion ? delta : -delta) * .25f, 0, 1);
        // SMTC can briefly stop its clock between tracks. The street keeps its own time.
        var camera = _machi!.CreateCamera((float)_size.Width / _size.Height, _backgroundTime, _machiMotionWeight,
            AppSettings.FoliaMachiFocalLengthMm);
        bool focalLengthChanged = camera.VerticalFieldOfView != _machiFrame.Camera.VerticalFieldOfView;
        _machi.WriteActorTransforms(_actorTransforms, _machiPlan!, position, _backgroundTime, delta, ref _machiDoor);
        *(Matrix4x4*)_lightData = camera.ViewProjection;
        *(Matrix4x4*)(_lightData + 4) = camera.Inverse;
        _lightData[8] = new(environment.SkyBottom, environment.Mist);
        _lightData[9] = new(environment.Rain, environment.Snow, environment.Haze, environment.Daylight);
        float flash = 0;
        if (environment.Rain >= .5f && anchor >= 0 && song.Lines[anchor].IsChorus && (anchor == 0 || !song.Lines[anchor-1].IsChorus))
        {
            float since = (float)(position-song.Lines[anchor].Start);
            if (since is >= 0 and <= 1.2f)
            {
                float first = (since-.04f)/.05f, second = (since-.24f)/.06f;
                flash = environment.Rain * MathF.Min(1, MathF.Exp(-first*first) + .7f*MathF.Exp(-second*second) + .25f*MathF.Exp(-since*3.5f));
            }
        }
        float weatherTime = (float)(_backgroundTime % 3600);
        _lightData[10] = new(_machi.SceneHalf, weatherTime, flash, MachiHdrOutput ? 1 : 0);
        _lightData[MachiSteamLighting] = new((environment.HemiSky + environment.HemiGround) * .5f
            + environment.CityGlow + new Vector3(.014f, .02f, .028f), 0);
        _lightData[MachiSteamLighting + 1] = new(environment.SunDirection, 0);
        _lightData[MachiSteamLighting + 2] = new(environment.SunRadiance, 0);
        _lightData[11] = new(environment.SkyTop, camera.FocusDistance);
        // Lyrics replace the panel texture in place. World history belongs to the camera,
        // geometry and reconstruction, whose discontinuities the backend also detects.
        _machiFrame = new(camera, environment.SkyTop, environment.SunDirection, environment.SunRadiance,
            environment.Wetness, (float)_data[1].W, firstEnvironmentFrame || startCycle || focalLengthChanged, MachiRayReconstructionRequested)
        {
            SamplesPerPixel = AppSettings.FoliaMachiSamplesPerPixel, MaxBounces = AppSettings.FoliaMachiMaxBounces,
            HemiSky = environment.HemiSky, HemiGround = environment.HemiGround, CityGlow = environment.CityGlow,
            NoVacancy = _machiPlan.UpdateVacancy(board, delta, resetLyrics || firstEnvironmentFrame, ref _machiChorus, ref _machiVacancyChorus),
            AmbientTime = weatherTime, Rain = environment.Rain, Puddles = _machiEnvironment.PuddlePattern,
            PuddleFill = environment.PuddleFill, RainTime = weatherTime,
            RainShelters = *(Matrix4x4*)(_lightData + 14),
            SkyBottom = environment.SkyBottom, Artificial = environment.Artificial,
            SnowCover = environment.SnowCover, Mist = environment.Mist, Haze = environment.Haze
        };
    }

    private void RecordMachi(ID3D12GraphicsCommandList commands)
    {
        var panel = NativeRtv(0);
        commands.ResourceBarrierTransition(_panelTexture!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.ClearRenderTargetView(panel, new Color4(0, 0, 0, 0));
        commands.RSSetViewport(0, 0, FoliaMachi.PanelWidth, FoliaMachi.PanelHeight, 0, 1);
        commands.RSSetScissorRect(FoliaMachi.PanelWidth, FoliaMachi.PanelHeight);
        RecordNativeInstances(commands, panel, _machiPanel!);
        commands.ResourceBarrierTransition(_panelTexture!, ResourceStates.RenderTarget, ResourceStates.NonPixelShaderResource);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.UnorderedAccess);
        _machiBackend!.Record(commands, _textures[3]!, _panelTexture!, 0, in _machiFrame, _actorTransforms);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.UnorderedAccess, ResourceStates.PixelShaderResource);
        commands.ResourceBarrierTransition(_panelTexture!, ResourceStates.NonPixelShaderResource, ResourceStates.PixelShaderResource);
        if (!_machiBackend.DenoisingDisabled && !_machiBackend.DenoiserAvailable && !_machiBackend.RayReconstructionActive)
            RecordMachiFailure(_activeRequest);

        var depth = _machiBackend.SurfaceDepth;
        _lightData[MachiDepthPostIndex] = new(_machiBackend.SurfaceDepthUvOffset,
            AppSettings.FoliaMachiChromaticAberration ? 1 : 0, AppSettings.FoliaMachiDepthOfField ? 1 : 0);
        SetFoliaState(commands);
        commands.ResourceBarrierTransition(depth, ResourceStates.NonPixelShaderResource, ResourceStates.PixelShaderResource);
        var atmosphere = Rtv(1);
        var bloom = _bloom;
        if (bloom != null)
            atmosphere = bloom.Begin(commands);
        else
            commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        RecordFullStage(commands, atmosphere, _machiAtmosphere!, 2);
        commands.SetPipelineState(_machiSteam!);
        commands.DrawInstanced(6, (uint)_machiSteamCount, 0, 0);
        if (bloom != null)
        {
            float night = 1 - _lightData[9].W;
            bloom.Apply(commands, _textures[4]!, Rtv(1), false, .06f + .09f * night, linearHdr: true, streaks: .12f * night * night);
        }
        else
            commands.ResourceBarrierTransition(_textures[4]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        SetFoliaState(commands);
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        RecordFullStage(commands, Rtv(0), _machiLens!, 1);
        if (_lightData[9].X + _lightData[9].Y > .001f)
        {
            commands.SetPipelineState(_machiWeather!);
            const int WeatherInstances = 3072 * 2;
            commands.DrawInstanced(6, (uint)(WeatherInstances + _machiDrips), 0, 0);
        }
        commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        commands.ResourceBarrierTransition(depth, ResourceStates.PixelShaderResource, ResourceStates.NonPixelShaderResource);
    }

    private void RecordNativeInstances(ID3D12GraphicsCommandList commands, CpuDescriptorHandle target, ID3D12PipelineState pipeline)
    {
        Bind(commands, 0);
        commands.OMSetRenderTargets(target);
        commands.SetPipelineState(pipeline);
        SetInstanceOffset(commands, (uint)_backgroundCount);
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
