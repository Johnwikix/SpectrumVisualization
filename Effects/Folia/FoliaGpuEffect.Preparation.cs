using System;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private readonly FoliaScenePreparation _preparation;
    private FoliaSceneUpload? _upload, _activeScene;
    private FoliaSceneRequest _requested, _activeRequest;
    private bool _hasRequest, _hasScene, _lyricsVisible;
    private static readonly byte[] EmptyCover = [14, 17, 24, 255];

    private FoliaSceneRequest DesiredScene()
    {
        float dpi = _size.PixelsPerDip;
        var song = playback.Song;
        // Metadata and decoded cover can become visible before a slow lyric lookup finishes.
        // Each published snapshot still commits its atlas, layout and images together.
        return new(song, AppSettings.FoliaFontFamily, AppSettings.FoliaFontWeight,
            AppSettings.FoliaLyricMode, AppSettings.FoliaBackground, _size.OutputWidth / dpi,
            _size.OutputHeight / dpi, AppSettings.FoliaFontScale, AppSettings.FoliaTemperaDecor, AppSettings.FoliaResolvedTheme);
    }

    private bool UpdatePreparedScene()
    {
        _glyphAtlas.FinishFrame();
        var request = DesiredScene();
        if (!_hasRequest || !_requested.Matches(request))
        {
            _requested = request;
            _hasRequest = true;
            _upload?.Dispose();
            _upload = null;
            RequestBackgroundTransition(request);
            _preparation.Request(request);
        }
        bool textChanged = false;
        var ready = _preparation.TakeReady();
        if (ready != null)
        {
            if (!ready.Request.Matches(request) || ready.Error != null)
            {
                if (ready.Error != null) App.WriteCrashLog("Folia scene preparation", ready.Error.Message, ready.Error);
                ready.Dispose();
            }
            else if (_upload != null && _upload.Scene.Request.Matches(ready.Request)) ready.Dispose();
            else
            {
                _upload?.Dispose();
                try { _upload = new(ready, _atlas!, _song!, _modeAtlasSource, _glyphAtlas); }
                catch { ready.Dispose(); throw; }
            }
        }
        if (_upload is { Complete: false })
            try { _upload.Advance(_device, EmptyCover); }
            catch (Exception error) when (_glyphStore.GpuEnabled || error is FoliaCpuGlyphsRequiredException)
            {
                // Allocation / generator setup failure is recoverable. The observed CPU
                // worker generates the missing tiles; the render thread never runs msdfgen.
                if (error is not FoliaCpuGlyphsRequiredException) App.WriteCrashLog("Folia glyph upload; using CPU fallback", error.Message, error);
                _glyphStore.SetGpuEnabled(false);
                _upload.Dispose();
                _upload = null;
                _hasRequest = false;
            }
        if (_glyphVersion != _glyphAtlas.Version)
        {
            _glyphVersion = _glyphAtlas.Version;
            Descriptors();
        }
        // The host selects temporal reset before PrepareFrame. If completion races that check,
        // leave the result for the next frame, so the SDK and effect reset on the same frame.
        if (_upload is { Complete: true } && _transitionPending && (!_temporal || _frame.Reset))
            CaptureBackgroundTransition();
        if (_upload is { Complete: true } upload && !_captureBackground && (!_temporal || _frame.Reset))
        {
            var scene = upload.Scene;
            var atlas = scene.Atlas!;
            var plan = scene.Plan!;
            textChanged = !ReferenceEquals(_atlas, atlas);
            InstallImage(upload.Glyphs);
            InstallImage(upload.Cover);
            InstallImage(upload.ModeAtlas);
            if (textChanged) _preparation.RetirePixels(atlas.DetachDistancePixels());
            _modeAtlasSource = upload.ModeAtlasSource;
            if (!ReferenceEquals(_pendolo, plan.Pendolo)) _clockMeshVersion = -1;
            _atlas = atlas;
            _song = scene.Song;
            _lumiere = plan.Lumiere;
            _cadenza = plan.Cadenza;
            _glowWords = plan.GlowWords;
            _partita = plan.Partita;
            _sonnet = plan.Sonnet;
            _diorama = plan.Diorama;
            _fume = plan.Fume;
            _tilt = plan.Tilt;
            _pendolo = plan.Pendolo;
            _tempera = plan.Tempera;
            _monet = plan.Monet;
            _latent.SetPalette(request.Song, scene.Colors);
            _activeRequest = request;
            CommitBackgroundTransition();
            _hasScene = true;
            Descriptors();
            _activeScene?.Dispose();
            _activeScene = upload;
            _upload = null;
        }
        // The committed atlas, layouts and textures remain one coherent foreground until
        // the replacement upload is complete. Only an explicit empty session hides it.
        _lyricsVisible = _hasScene && playback.Song.Title.Length != 0;
        return textChanged;
    }
}
