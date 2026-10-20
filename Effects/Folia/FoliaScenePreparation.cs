using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaSceneRequest(FoliaSong Song, string Family, int Weight,
    string Mode, string Background, float Width, float Height, float Scale, bool TemperaDecor = true)
{
    internal bool Matches(in FoliaSceneRequest other) => ReferenceEquals(Song, other.Song)
        && Family == other.Family && Weight == other.Weight && Mode == other.Mode && Background == other.Background
        && Width == other.Width && Height == other.Height && Scale == other.Scale
        && (Mode != "tempera" || TemperaDecor == other.TemperaDecor);

    internal static bool SameText(FoliaSong a, FoliaSong b) => a.Title == b.Title && a.Artist == b.Artist
        && ReferenceEquals(a.Lines, b.Lines);
}

/// <summary>Prepared off-thread, then transferred exclusively to the render worker. No GPU resources.</summary>
internal sealed class FoliaModePlan
{
    internal FoliaLumiere Lumiere { get; } = new();
    internal FoliaCadenza Cadenza { get; } = new();
    internal FoliaGlowWords GlowWords { get; } = new();
    internal FoliaPartita Partita { get; } = new();
    internal FoliaSonnet Sonnet { get; } = new();
    internal FoliaDiorama Diorama { get; } = new();
    internal FoliaFume Fume { get; } = new();
    internal FoliaTilt Tilt { get; } = new();
    internal FoliaPendolo Pendolo { get; } = new();
    internal FoliaTempera Tempera { get; } = new();
    internal FoliaMonet Monet { get; } = new();

    internal FoliaModePlan(FoliaTextAtlas atlas, in FoliaSceneRequest request, CancellationToken token)
    {
        if (atlas.Lines.Length != 0)
            switch (request.Mode)
            {
                case "lumiere": Lumiere.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "cadenza": Cadenza.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "partita": Partita.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "tempera": Tempera.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "fume": Fume.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "diorama": Diorama.Prepare(atlas, token); break;
                case "tilt": Tilt.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "pendolo": Pendolo.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "sonnet": Sonnet.Prepare(atlas, request.Width, request.Height, request.Scale, request.Family, request.Weight, token); break;
                case "monet": Monet.Prepare(atlas, request, token); break;
            }
        token.ThrowIfCancellationRequested();
        GlowWords.Prepare(atlas, request.Mode is "classic" or "partita" or "tilt", request.Mode == "classic", token);
        if (request.Mode == "cappella") _ = FoliaAssets.Emojis;
    }
}

internal sealed class FoliaPreparedScene(FoliaSceneRequest request, FoliaTextAtlas? atlas,
    FoliaModePlan? plan, Vector3[] colors, Exception? error = null) : IDisposable
{
    internal FoliaSceneRequest Request { get; } = request;
    internal FoliaTextAtlas? Atlas { get; } = atlas;
    internal FoliaModePlan? Plan { get; } = plan;
    internal Vector3[] Colors { get; } = colors;
    internal Exception? Error { get; } = error;
    private FoliaTextAtlas? _owner = atlas?.Retain();
    public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseLease();
}

/// <summary>One cancellable CPU worker with a single latest request. Active render state is never mutated here.</summary>
internal sealed class FoliaScenePreparation : IDisposable
{
    internal FoliaGlyphStore Store { get; }
    private readonly bool _ownsStore;
    internal FoliaScenePreparation(FoliaGlyphStore? store = null) { Store = store ?? new(); _ownsStore = store == null; }
    private readonly object _gate = new();
    private FoliaSceneRequest? _pending;
    private FoliaSceneRequest? _working;
    private FoliaPreparedScene? _ready;
    private readonly Queue<FoliaPixelBuffer> _retiredPixels = new();
    private CancellationTokenSource? _cancellation;
    private Task? _work;
    private bool _running, _stopped;
    private FoliaTextAtlas? _atlas;
    private FoliaSceneRequest _atlasRequest, _planRequest, _italicRequest;
    private bool _hasItalicRequest;
    private FoliaModePlan? _plan;
    private byte[]? _cover;
    private Vector3[] _colors = [];

    internal bool HasReady => Volatile.Read(ref _ready) != null;
    internal FoliaPreparedScene? TakeReady() => Interlocked.Exchange(ref _ready, null);

    internal void Request(in FoliaSceneRequest request)
    {
        lock (_gate)
        {
            if (_stopped) return;
            _pending = request;
            // Late album art must not cancel/restart expensive glyph preparation for the same text.
            if (_working is not { } working || !FoliaSceneRequest.SameText(working.Song, request.Song)
                || working.Family != request.Family || working.Weight != request.Weight || working.Mode != request.Mode
                || working.Width != request.Width || working.Height != request.Height || working.Scale != request.Scale
                || working.Background != request.Background)
                _cancellation?.Cancel();
            Interlocked.Exchange(ref _ready, null)?.Dispose();
            StartWorker();
        }
    }

    internal void RetirePixels(FoliaPixelBuffer? pixels)
    {
        if (pixels == null) return;
        lock (_gate)
        {
            if (_stopped) { pixels.Dispose(); return; }
            _retiredPixels.Enqueue(pixels);
            StartWorker();
        }
    }

    // Called only under _gate. Pixel decommit can take milliseconds, so retire it on the
    // same observed worker rather than moving a large allocation's free into the commit frame.
    private void StartWorker()
    {
        if (_running) return;
        _running = true;
        _work = Task.Run(RunWorker);
    }

    private void RunWorker()
    {
        while (true)
        {
            FoliaSceneRequest request = default;
            CancellationTokenSource? cancellation = null;
            FoliaPixelBuffer? retired;
            lock (_gate)
            {
                if (!_retiredPixels.TryDequeue(out retired))
                {
                    if (_stopped || _pending is not { } next)
                    {
                        _running = false;
                        return;
                    }
                    request = next;
                    _working = request;
                    _pending = null;
                    cancellation = _cancellation = new CancellationTokenSource();
                }
            }
            if (retired != null)
            {
                retired.Dispose();
                continue;
            }
            var token = cancellation!.Token;
            try
            {
                // Requests already coalesce into _pending, and obsolete work is cancellable.
                // An isolated mode/background change must not pay a debounce interval.
                token.ThrowIfCancellationRequested();
                if (_atlas == null || _atlasRequest.Family != request.Family || _atlasRequest.Weight != request.Weight
                    || !FoliaSceneRequest.SameText(_atlasRequest.Song, request.Song))
                    ReplaceAtlas(CreateAtlas(request, token), request);
                if (request.Mode == "tilt") PrepareTiltGlyphs(request, token);
                if (_atlas.IsShared) Store.PrepareCpuTiles(_atlas.SharedGlyphs, token);
                if (_plan == null || _planRequest.Mode != request.Mode || _planRequest.Width != request.Width
                    || _planRequest.Height != request.Height || _planRequest.Scale != request.Scale
                    || request.Mode == "tempera" && _planRequest.TemperaDecor != request.TemperaDecor
                    || request.Mode == "monet" && (_planRequest.Song.CoverWidth > 0) != (request.Song.CoverWidth > 0))
                {
                    var plan = new FoliaModePlan(_atlas, request, token);
                    token.ThrowIfCancellationRequested();
                    _plan = plan;
                    _planRequest = request;
                }
                if (request.Background == "latent" && !ReferenceEquals(_cover, request.Song.CoverRgba))
                {
                    var colors = FoliaLatent.Extract(request.Song, token);
                    token.ThrowIfCancellationRequested();
                    _colors = colors;
                    _cover = request.Song.CoverRgba;
                }
                var result = new FoliaPreparedScene(request, _atlas, _plan, _colors);
                lock (_gate)
                    if (!_stopped && !token.IsCancellationRequested)
                        Interlocked.Exchange(ref _ready, result)?.Dispose();
                    else result.Dispose();
            }
            catch (Exception) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                lock (_gate)
                    if (!_stopped && !token.IsCancellationRequested)
                        Interlocked.Exchange(ref _ready, new(request, null, null, [], error))?.Dispose();
            }
            finally
            {
                lock (_gate)
                {
                    _cancellation = null;
                    _working = null;
                    cancellation.Dispose();
                }
            }
        }
    }

    private FoliaTextAtlas CreateAtlas(in FoliaSceneRequest request, CancellationToken token, IReadOnlySet<string>? italics = null)
    {
        FoliaTextAtlas atlas;
        try { atlas = new(request.Song, request.Family, request.Weight, token, italics != null, Store, italics); }
        catch (Exception error) when (IsCapacityFailure(error))
        {
            // A large live pair cannot evict the visible scene. Keep the private CPU
            // capacity fallback, with the same selected italic glyphs as the shared atlas.
            atlas = new(request.Song, request.Family, request.Weight, token, italics != null, italicCharacters: italics);
        }
        if (token.IsCancellationRequested)
        {
            atlas.Dispose();
            token.ThrowIfCancellationRequested();
        }
        return atlas;
    }

    [MemberNotNull(nameof(_atlas))]
    private void ReplaceAtlas(FoliaTextAtlas atlas, in FoliaSceneRequest request)
    {
        var previous = _atlas;
        _atlas = atlas;
        _atlasRequest = request;
        _plan = null;
        _hasItalicRequest = false;
        previous?.Dispose();
    }

    private void PrepareTiltGlyphs(in FoliaSceneRequest request, CancellationToken token)
    {
        if (_hasItalicRequest && FoliaSceneRequest.SameText(_italicRequest.Song, request.Song)
            && _italicRequest.Family == request.Family && _italicRequest.Weight == request.Weight
            && _italicRequest.Width == request.Width && _italicRequest.Height == request.Height && _italicRequest.Scale == request.Scale)
            return;
        // Tilt's segmentation uses normal metrics. Plan it before requesting italic
        // outlines, so titles, translations and upright segments never delay the switch.
        var tilt = new FoliaTilt();
        tilt.Prepare(_atlas!, request.Width, request.Height, request.Scale, request.Family, request.Weight, token);
        var required = tilt.RequiredItalicCharacters(_atlas!);
        token.ThrowIfCancellationRequested();
        if (!_atlas!.HasItalicGlyphs(required)) ReplaceAtlas(CreateAtlas(request, token, required), request);
        _italicRequest = request;
        _hasItalicRequest = true;
    }

    public void Dispose()
    {
        Task? work;
        lock (_gate)
        {
            _stopped = true;
            _pending = null;
            _cancellation?.Cancel();
            work = _work;
        }
        // The owning render worker calls this after its frame barrier. CPU preparation never
        // dispatches to UI or waits for rendering, so joining here has no circular dependency.
        work?.GetAwaiter().GetResult();
        Interlocked.Exchange(ref _ready, null)?.Dispose();
        _atlas?.Dispose();
        _atlas = null;
        _plan = null;
        _cover = null;
        _colors = [];
        _atlasRequest = _planRequest = _italicRequest = default;
        _hasItalicRequest = false;
        _work = null;
        if (_ownsStore) Store.Dispose();
    }

    private static bool IsCapacityFailure(Exception error)
    {
        if (error is FoliaAtlasCapacityException) return true;
        if (error is not AggregateException aggregate) return false;
        foreach (var inner in aggregate.InnerExceptions) if (!IsCapacityFailure(inner)) return false;
        return aggregate.InnerExceptions.Count > 0;
    }
}
