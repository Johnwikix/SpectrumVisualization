using WinExSpectrumTest.Service;
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

    private FoliaSceneRequest DesiredScene(FoliaSong song)
    {
        float dpi = _size.PixelsPerDip;
        string mode = AppSettings.FoliaLyricMode;
        string machiScene = AppSettings.FoliaMachiScene;
        if (_failedMachiSelection is { } failed && (mode != "machi" || !ReferenceEquals(song, failed.Song)
            || machiScene != failed.Scene || AppSettings.FoliaMachiDenoiser != failed.Denoiser))
            _failedMachiSelection = null;
        if (mode == "machi" && (!_hardwareRaytracing || _failedMachiSelection != null)) mode = "classic";
        string? idleScene = null;
        if (mode == "machi" && machiScene == "auto" && song.Title.Length == 0 && _machiPlan != null)
            idleScene = _activeRequest.Song.Title.Length != 0 ? _machiPlan.SceneId : _activeRequest.MachiIdleScene;
        return new(song, AppSettings.FoliaFontFamily, AppSettings.FoliaFontWeight,
            mode, AppSettings.FoliaBackground, _size.OutputWidth / dpi,
            _size.OutputHeight / dpi, AppSettings.FoliaFontScale, AppSettings.FoliaTemperaDecor, AppSettings.FoliaResolvedTheme, machiScene, idleScene);
    }

    private bool ShowsSong(FoliaSong current) => _hasScene && current.Title.Length != 0
        && FoliaSong.SameTrack(_activeRequest.Song, current);

    private bool UpdatePreparedScene(FoliaSong current)
    {
        _glyphAtlas.FinishFrame();
        var request = DesiredScene(current);
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
                if (ready.Error != null)
                {
                    App.WriteCrashLog("Folia scene preparation", ready.Error.Message, ready.Error);
                    if (request.Mode == "machi" && ready.Request.Matches(request))
                        RecordMachiFailure(request);
                }
                ready.Dispose();
            }
            else if (_upload != null && _upload.Scene.Request.Matches(ready.Request)) ready.Dispose();
            else
            {
                _upload?.Dispose();
                try { _upload = new(ready, _atlas!, _song!, _modeAtlasSource, _glyphAtlas); }
                catch (global::System.Exception caughtException) { AppLog.Caught(caughtException); ready.Dispose(); throw; }
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
        if (_upload is { Complete: true } candidate && _transitionPending && (!_temporal || _frame.Reset))
        {
            _transitionPending = false;
            if (BackgroundChanges(candidate.Scene))
                CaptureBackgroundTransition();
        }
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
            if (_hasScene && _activeRequest.Mode == "pendolo" && request.Mode == "pendolo")
                plan.Pendolo.ContinueClockFrom(_pendolo);
            _pendolo = plan.Pendolo;
            _tempera = plan.Tempera;
            _monet = plan.Monet;
            _hanabi = plan.Hanabi;
            _metaphor = plan.Metaphor;
            _nebbia = plan.Nebbia;
            _machi = plan.Machi;
            _machiPlan = plan.MachiPlan;
            _latent.SetPalette(request.Song, scene.Colors);
            _activeRequest = request;
            CommitBackgroundTransition();
            _hasScene = true;
            Descriptors();
            _activeScene?.Dispose();
            _activeScene = upload;
            _upload = null;
        }
        // Keep running the committed background during handoff. Its lyrics and metadata
        // belong to the old track as soon as SMTC confirms a different track.
        _lyricsVisible = ShowsSong(current);
        return textChanged;
    }
}
