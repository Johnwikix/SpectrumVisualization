using System;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private readonly FoliaScenePreparation _preparation = new();
    private FoliaSceneUpload? _upload, _activeScene;
    private FoliaSceneRequest _requested, _activeRequest;
    private bool _hasRequest, _hasScene, _lyricsVisible, _metadataVisible;
    private static readonly byte[] EmptyCover = [14, 17, 24, 255];

    private FoliaSceneRequest DesiredScene()
    {
        float dpi = _size.PixelsPerDip;
        return new(playback.Song, AppSettings.FoliaFontFamily, AppSettings.FoliaFontWeight,
            AppSettings.FoliaLyricMode, AppSettings.FoliaBackground, _size.OutputWidth / dpi,
            _size.OutputHeight / dpi, AppSettings.FoliaFontScale);
    }

    private bool UpdatePreparedScene()
    {
        var request = DesiredScene();
        if (!_hasRequest || !_requested.Matches(request))
        {
            _requested = request;
            _hasRequest = true;
            _upload?.Dispose();
            _upload = null;
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
            else
            {
                try { _upload = new(ready, _atlas!, _song!, _modeAtlasSource); }
                catch { ready.Dispose(); throw; }
            }
        }
        if (_upload is { Complete: false }) _upload.Advance(_device, EmptyCover);
        // The host selects temporal reset before PrepareFrame. If completion races that check,
        // leave the result for the next frame, so the SDK and effect reset on the same frame.
        if (_upload is { Complete: true } upload && (!_temporal || _frame.Reset))
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
            _song = request.Song;
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
            _hasScene = true;
            Descriptors();
            _activeScene?.Dispose();
            _activeScene = upload;
            _upload = null;
        }
        _lyricsVisible = _hasScene && FoliaSceneRequest.SameText(_song!, request.Song);
        _metadataVisible = _hasScene && _song!.Title == request.Song.Title && _song.Artist == request.Song.Artist;
        return textChanged;
    }
}
