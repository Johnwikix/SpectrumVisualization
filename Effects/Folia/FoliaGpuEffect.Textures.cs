using System;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private readonly int[] _imageWidths = new int[6], _imageHeights = new int[6];
    private readonly Format[] _imageFormats = new Format[6];
    private static int ImagePixelSize(Format format) => format == Format.R16G16B16A16_Float ? 8 : format == Format.R8_UNorm ? 1 : 4;
    private void ReleaseImage(int slot)
    {
        GpuGraphics.DisposeResource(_textures[slot]);
        GpuGraphics.DisposeResource(_uploads[slot]);
        _textures[slot] = _uploads[slot] = null;
        _pending[slot] = false;
    }
    private void InstallImage(FoliaImageUpload? image)
    {
        if (image == null) return;
        int slot = image.Slot;
        ReleaseImage(slot);
        _textures[slot] = image.Texture;
        image.Texture = null;
        // The preceding frame's fence has already completed the final copy.
        GpuGraphics.DisposeResource(image.Upload);
        image.Upload = null;
    }
    private void SetImage(int slot, int width, int height, ReadOnlySpan<byte> pixels, Format format)
    {
        _textures[slot]?.Dispose();
        _uploads[slot]?.Dispose();
        int bytesPerPixel = ImagePixelSize(format);
        uint pitch = (uint)((width * bytesPerPixel + 255) & ~255);
        _uploads[slot] = _device.CreateCommittedResource(new HeapProperties(HeapType.Upload), HeapFlags.None, ResourceDescription.Buffer(pitch * (ulong)height), ResourceStates.GenericRead);
        byte* mapped = _uploads[slot]!.Map<byte>(0);
        for (int y = 0; y < height; y++)
            pixels.Slice(y * width * bytesPerPixel, width * bytesPerPixel).CopyTo(new Span<byte>(mapped + y * pitch, width * bytesPerPixel));
        _uploads[slot]!.Unmap(0);
        _textures[slot] = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None, ResourceDescription.Texture2D(format, (uint)width, (uint)height, 1, 1), ResourceStates.CopyDest);
        _imageWidths[slot] = width;
        _imageHeights[slot] = height;
        _imageFormats[slot] = format;
        _pending[slot] = true;
    }
    private void UploadImages(ID3D12GraphicsCommandList commands)
    {
        for (int i = 0; i < 6; i++)
        {
            if (!_pending[i])
                continue;
            commands.CopyTextureRegion(new TextureCopyLocation(_textures[i]!, 0), 0, 0, 0, new TextureCopyLocation(_uploads[i]!, new PlacedSubresourceFootPrint { Footprint = new SubresourceFootPrint(_imageFormats[i], (uint)_imageWidths[i], (uint)_imageHeights[i], 1, (uint)((_imageWidths[i] * ImagePixelSize(_imageFormats[i]) + 255) & ~255)) }), null);
            commands.ResourceBarrierTransition(_textures[i]!, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);
            _pending[i] = false;
        }
    }
    private void Descriptors()
    {
        for (int table = 0; table < 4; table++)
        {
            for (int slot = 0; slot < 5; slot++)
            {
                // Do not bind a private render target as an SRV during its own pass.
                var texture = slot switch
                {
                    0 when _atlas!.IsShared => _glyphAtlas.Distance,
                    3 when table is not (2 or 3) => _textures[1],
                    4 => (table is 1 or 3 ? _textures[4] : null) ?? _textures[1],
                    _ => _textures[slot]
                };
                if (table == 1 && slot == 3 && _windowFog != null)
                    texture = _windowFog;
                _device.CreateShaderResourceView(texture!, slot == 0 ? FoliaGpuGlyphAtlas.Srv(texture!) : null, _srv.GetCPUDescriptorHandleForHeapStart() + (table * 9 + slot) * _srvStride);
            }
            var shadow = _atlas!.IsShared ? _glyphAtlas.Shadow! : _textures[5]!;
            _device.CreateShaderResourceView(shadow, FoliaGpuGlyphAtlas.Srv(shadow), _srv.GetCPUDescriptorHandleForHeapStart() + (table * 9 + 5) * _srvStride);
            _device.CreateShaderResourceView(_textures[5] ?? _textures[1]!, null, _srv.GetCPUDescriptorHandleForHeapStart() + (table * 9 + 6) * _srvStride);
            _device.CreateShaderResourceView(_textures[6] ?? _textures[1]!, null, _srv.GetCPUDescriptorHandleForHeapStart() + (table * 9 + 7) * _srvStride);
            var extra = table switch
            {
                0 => _backgroundHistory,
                1 => _machiBackend?.SurfaceDepth ?? _windowTexture ?? _inkTint,
                2 => _machiBackend?.SurfaceDepth,
                _ => null
            };
            _device.CreateShaderResourceView(extra ?? _textures[1]!, null, _srv.GetCPUDescriptorHandleForHeapStart() + (table * 9 + 8) * _srvStride);
        }
    }
}
