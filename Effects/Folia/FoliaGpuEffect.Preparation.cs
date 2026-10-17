using System;
using Vortice.DXGI;
using WinExSpectrumTest.Model;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private readonly FoliaScenePreparation _preparation = new();
    private FoliaSceneRequest _requested, _activeRequest;
    private bool _hasRequest, _hasScene, _lyricsVisible;
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
            _preparation.Request(request);
        }
        bool textChanged = false;
        // The host selects temporal reset before PrepareFrame. If completion races that check,
        // leave the result for the next frame, so the SDK and effect reset on the same frame.
        if (!_temporal || _frame.Reset)
        {
            var ready = _preparation.TakeReady();
            if (ready != null && ready.Request.Matches(request))
            {
                if (ready.Error != null) App.WriteCrashLog("Folia scene preparation", ready.Error.Message, ready.Error);
                else
                {
                    var atlas = ready.Atlas!;
                    var plan = ready.Plan!;
                    textChanged = !ReferenceEquals(_atlas, atlas);
                    if (textChanged)
                    {
                        SetImage(0, atlas.Width, atlas.Height, atlas.Pixels, Format.R16G16B16A16_Float);
                        atlas.ReleaseDistancePixels();
                    }
                    if (!_hasScene || !ReferenceEquals(_song!.CoverRgba, request.Song.CoverRgba))
                        SetImage(1, Math.Max(1, request.Song.CoverWidth), Math.Max(1, request.Song.CoverHeight),
                            request.Song.CoverRgba.Length > 0 ? request.Song.CoverRgba : EmptyCover, Format.R8G8B8A8_UNorm);
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
                    _latent.SetPalette(request.Song, ready.Colors);
                    _activeRequest = request;
                    _hasScene = true;
                    Descriptors();
                }
            }
        }
        _lyricsVisible = _hasScene && FoliaSceneRequest.SameText(_song!, request.Song);
        return textChanged;
    }
}
