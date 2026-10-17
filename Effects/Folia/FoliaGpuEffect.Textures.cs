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
    private void InstallImage(FoliaImageUpload? image)
    {
        if (image == null) return;
        int slot = image.Slot;
        GpuGraphics.DisposeResource(_textures[slot]);
        GpuGraphics.DisposeResource(_uploads[slot]);
        _textures[slot] = image.Texture;
        _uploads[slot] = image.Upload;
        image.Texture = image.Upload = null;
        _imageWidths[slot] = image.Width;
        _imageHeights[slot] = image.Height;
        _imageFormats[slot] = image.Format;
        // Candidate copies and their final transition completed on the preceding frame's fence.
        _pending[slot] = false;
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
                var texture = slot == 3 ? ((table == 2 || table == 3) ? _textures[3] : _textures[1]) : slot == 4 ? ((table == 1 || table == 3) ? _textures[4] : _textures[1]) : _textures[slot];
                _device.CreateShaderResourceView(texture!, null, _srv.GetCPUDescriptorHandleForHeapStart() + (table * 8 + slot) * _srvStride);
            }
            _device.CreateShaderResourceView(_textures[5] ?? _textures[1]!, null, _srv.GetCPUDescriptorHandleForHeapStart() + (table * 8 + 6) * _srvStride);
            _device.CreateShaderResourceView(_textures[6] ?? _textures[1]!, null, _srv.GetCPUDescriptorHandleForHeapStart() + (table * 8 + 7) * _srvStride);
        }
    }
}
