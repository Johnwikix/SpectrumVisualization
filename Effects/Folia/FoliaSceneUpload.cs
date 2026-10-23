using System;
using System.Diagnostics;
using Vortice.Direct3D12;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Render-worker-owned staging. Candidates stay unbound until every image is ready.</summary>
internal sealed unsafe class FoliaImageUpload(int slot, int width, int height, Format format) : IDisposable
{
    internal int Slot { get; } = slot;
    internal int Width { get; } = width;
    internal int Height { get; } = height;
    internal Format Format { get; } = format;
    internal ID3D12Resource? Upload, Texture;
    private byte* _mapped;
    private int _row, _copiedRow;
    private bool _complete;
    private readonly int _rowBytes = checked(width * (format == Format.R16G16B16A16_Float ? 8 : format == Format.R8_UNorm ? 1 : 4));
    private uint Pitch => (uint)((_rowBytes + 255) & ~255);
    private int StagingRows => Math.Min(Height, Math.Max(1, 4 * 1024 * 1024 / (int)Pitch));

    internal bool Advance(ID3D12Device device, ReadOnlySpan<byte> pixels)
    {
        if (_complete) return true;
        if (Texture == null)
        {
            Texture = device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format, (uint)Width, (uint)Height, 1, 1), ResourceStates.CopyDest);
        }
        if (Upload == null)
        {
            Upload = device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None,
                ResourceDescription.Buffer(Pitch * (ulong)StagingRows), ResourceStates.GenericRead);
            _mapped = Upload.Map<byte>(0);
        }
        if (_row < Height)
        {
            // The host completes the preceding frame before Advance, so this small mapped
            // staging region can be reused after each GPU copy without another allocator/fence.
            long start = Stopwatch.GetTimestamp();
            int rowsPerChunk = Math.Max(1, 1024 * 1024 / _rowBytes);
            do
            {
                int rows = Math.Min(rowsPerChunk, Math.Min(Height - _row, StagingRows - (_row - _copiedRow)));
                if (Pitch == _rowBytes)
                    pixels.Slice(_row * _rowBytes, rows * _rowBytes)
                        .CopyTo(new Span<byte>(_mapped + (nuint)(_row - _copiedRow) * Pitch, rows * _rowBytes));
                else
                    for (int y = _row; y < _row + rows; y++)
                        pixels.Slice(y * _rowBytes, _rowBytes).CopyTo(new Span<byte>(_mapped + (nuint)(y - _copiedRow) * Pitch, _rowBytes));
                _row += rows;
            } while (_row < Height && _row - _copiedRow < StagingRows && Stopwatch.GetElapsedTime(start).TotalMilliseconds < .75);
            if (_row == Height)
            {
                Upload.Unmap(0);
                _mapped = null;
            }
            return false;
        }
        return false;
    }

    internal void Record(ID3D12GraphicsCommandList commands)
    {
        if (_copiedRow == _row) return;
        commands.CopyTextureRegion(new TextureCopyLocation(Texture!, 0), 0, (uint)_copiedRow, 0,
            new TextureCopyLocation(Upload!, new PlacedSubresourceFootPrint
            {
                Footprint = new SubresourceFootPrint(Format, (uint)Width, (uint)StagingRows, 1, Pitch)
            }), new Box(0, 0, 0, Width, _row - _copiedRow, 1));
        _copiedRow = _row;
        if (_copiedRow == Height)
        {
            commands.ResourceBarrierTransition(Texture!, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
            _complete = true;
        }
    }

    public void Dispose()
    {
        if (_mapped != null) Upload!.Unmap(0);
        _mapped = null;
        GpuGraphics.DisposeResource(Upload);
        GpuGraphics.DisposeResource(Texture);
        Upload = Texture = null;
    }
}

internal sealed class FoliaSceneUpload : IDisposable
{
    internal FoliaPreparedScene Scene { get; }
    internal object ModeAtlasSource { get; }
    internal FoliaImageUpload? Glyphs { get; }
    internal FoliaImageUpload? Cover { get; }
    internal FoliaImageUpload? ModeAtlas { get; }
    private bool _complete;
    private readonly FoliaGpuGlyphAtlas? _resident;
    private FoliaGpuGlyphAtlas.FoliaGlyphBatch? _glyphBatch;
    internal bool Complete => _complete;

    internal FoliaSceneUpload(FoliaPreparedScene scene, FoliaTextAtlas activeAtlas, FoliaSong activeSong, object? modeSource,
        FoliaGpuGlyphAtlas? resident = null)
    {
        Scene = scene;
        var atlas = scene.Atlas!;
        _resident = resident;
        if (!atlas.IsShared && !ReferenceEquals(atlas, activeAtlas)) Glyphs = new(0, atlas.Width, atlas.Height, Format.R16G16B16A16_Float);
        if (!ReferenceEquals(scene.Song.CoverRgba, activeSong.CoverRgba)
            || !ReferenceEquals(scene.Song.DisplayCoverRgba, activeSong.DisplayCoverRgba))
            Cover = new(1, Math.Max(1, scene.Request.Song.CoverWidth), Math.Max(1, scene.Request.Song.CoverHeight), Format.R8G8B8A8_UNorm);
        bool emoji = scene.Request.Mode == "cappella" && atlas.Lines.Length > 0;
        ModeAtlasSource = emoji ? FoliaAssets.Emojis : atlas;
        if (!ReferenceEquals(modeSource, ModeAtlasSource) && (emoji || !atlas.IsShared))
            ModeAtlas = emoji ? new(5, FoliaAssets.Emojis.Width, FoliaAssets.Emojis.Height, Format.B8G8R8A8_UNorm)
                : new(5, atlas.Width, atlas.Height, Format.R8_UNorm);
    }

    internal void Advance(ID3D12Device device, ReadOnlySpan<byte> emptyCover)
    {
        if (_complete) return;
        bool glyphsReady;
        if (Scene.Atlas!.IsShared)
        {
            if (_glyphBatch is { Submitted: true })
            {
                _glyphBatch.CompleteAfterFence();
                _glyphBatch.Dispose();
                _glyphBatch = null;
            }
            glyphsReady = true;
            foreach (var glyph in Scene.Atlas.SharedGlyphs)
                if (!glyph.Resident) { glyphsReady = false; break; }
            if (!glyphsReady && _glyphBatch == null) _glyphBatch = _resident!.Prepare(Scene.Atlas.SharedGlyphs);
        }
        else glyphsReady = Glyphs == null || Glyphs.Advance(device, Scene.Atlas.Pixels);

        // Independent images can upload alongside glyph batches. Missing images and
        // resident glyphs consume no frames; actual copies still cross the host fence.
        var song = Scene.Song;
        bool coverReady = Cover == null || Cover.Advance(device, song.CoverRgba.Length == 0 ? emptyCover : song.DisplayCoverRgba ?? song.CoverRgba);
        bool modeReady = ModeAtlas == null || ModeAtlas.Advance(device,
            ModeAtlasSource is FoliaAssets.EmojiAtlas emojis ? emojis.Pixels.AsSpan() : Scene.Atlas.ShadowPixels);
        _complete = glyphsReady && coverReady && modeReady;
    }

    internal void Record(ID3D12GraphicsCommandList commands)
    {
        _glyphBatch?.Record(commands);
        Glyphs?.Record(commands);
        Cover?.Record(commands);
        ModeAtlas?.Record(commands);
    }

    public void Dispose()
    {
        Glyphs?.Dispose();
        // All upload disposal is at the host's preceding-frame fence (or final drain).
        // Completed work from an obsolete song is still reusable by later songs.
        if (_glyphBatch is { Submitted: true }) _glyphBatch.CompleteAfterFence();
        _glyphBatch?.Dispose();
        Cover?.Dispose();
        ModeAtlas?.Dispose();
        Scene.Dispose();
    }
}
