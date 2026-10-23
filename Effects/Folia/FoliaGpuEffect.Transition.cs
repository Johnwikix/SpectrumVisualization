using System;
using Vortice.Direct3D12;
using Vortice.DXGI;
using WinExSpectrumTest.Rendering;

namespace WinExSpectrumTest.Effects.Folia;

internal sealed unsafe partial class FoliaGpuEffect
{
    private const float BackgroundTransitionSeconds = .45f;
    private ID3D12Resource? _backgroundHistory;
    private ID3D12PipelineState _backgroundSnapshot = null!, _backgroundBlend = null!;
    private int _historyWidth, _historyHeight;
    private bool _captureBackground, _holdBackground, _transitionPending;
    private float _backgroundAlpha, _transitionElapsed;

    private void RequestBackgroundTransition(in FoliaSceneRequest request)
    {
        // A superseded candidate must not leave the live scene hidden by its snapshot.
        _transitionPending = false;
        _captureBackground = false;
        if (_holdBackground) CommitBackgroundTransition();
        if (!_hasScene || request.Song.Title.Length == 0) return;
        if (_activeRequest.Song.Title == request.Song.Title && _activeRequest.Song.Artist == request.Song.Artist
            && ReferenceEquals(_activeRequest.Song.CoverRgba, request.Song.CoverRgba)
            && _activeRequest.Background == request.Background && _activeRequest.Theme == request.Theme) return;
        _transitionPending = true;
    }

    private void CaptureBackgroundTransition()
    {
        if (_backgroundHistory == null)
        {
            // A blurred background needs neither a second full scene nor a full-resolution
            // history. Bound the reusable FP16 snapshot to 518400 pixels (about 4 MiB).
            double scale = Math.Min(1, Math.Min(960d / Math.Max(_size.Width, _size.Height),
                Math.Sqrt(518400d / ((double)_size.Width * _size.Height))));
            _historyWidth = Math.Max(1, (int)(_size.Width * scale));
            _historyHeight = Math.Max(1, (int)(_size.Height * scale));
            _backgroundHistory = _device.CreateCommittedResource(new HeapProperties(HeapType.Default), HeapFlags.None,
                ResourceDescription.Texture2D(Format.R16G16B16A16_Float, (uint)_historyWidth, (uint)_historyHeight,
                    1, 1, 1, 0, ResourceFlags.AllowRenderTarget), ResourceStates.PixelShaderResource);
            _device.CreateRenderTargetView(_backgroundHistory, null, Rtv(5));
            Descriptors();
        }
        // Capture only at the completed upload boundary. The outgoing background keeps
        // animating throughout CPU preparation and staging, including interrupted blends.
        _transitionPending = false;
        _captureBackground = true;
    }

    private void CommitBackgroundTransition()
    {
        if (!_holdBackground) return;
        _holdBackground = false;
        _transitionElapsed = 0;
        _backgroundAlpha = 1;
    }

    private void AdvanceBackgroundTransition(double elapsed)
    {
        if (_holdBackground || _backgroundAlpha <= 0) return;
        _transitionElapsed = Math.Min(BackgroundTransitionSeconds, _transitionElapsed + (float)Math.Max(0, elapsed));
        float t = _transitionElapsed / BackgroundTransitionSeconds;
        _backgroundAlpha = 1 - t * t * (3 - 2 * t);
    }

    private void RecordBackgroundTransition(ID3D12GraphicsCommandList commands)
    {
        if (_backgroundAlpha > 0)
        {
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
            commands.OMSetRenderTargets(Rtv(0));
            Bind(commands, 0);
            commands.SetPipelineState(_backgroundBlend);
            commands.DrawInstanced(3, 1, 0, 0);
            commands.ResourceBarrierTransition(_textures[3]!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        }
        if (!_captureBackground) return;
        commands.ResourceBarrierTransition(_backgroundHistory!, ResourceStates.PixelShaderResource, ResourceStates.RenderTarget);
        commands.OMSetRenderTargets(Rtv(5));
        // Table 2 binds the stage, and deliberately does not bind history as an SRV.
        Bind(commands, 2);
        commands.SetPipelineState(_backgroundSnapshot);
        commands.RSSetViewport(0, 0, _historyWidth, _historyHeight, 0, 1);
        commands.RSSetScissorRect(_historyWidth, _historyHeight);
        commands.DrawInstanced(3, 1, 0, 0);
        commands.ResourceBarrierTransition(_backgroundHistory!, ResourceStates.RenderTarget, ResourceStates.PixelShaderResource);
        commands.RSSetViewport(0, 0, _size.Width, _size.Height, 0, 1);
        commands.RSSetScissorRect(_size.Width, _size.Height);
        _captureBackground = false;
        _holdBackground = true;
        _backgroundAlpha = 1;
    }

    private void ResetBackgroundTransition()
    {
        GpuGraphics.DisposeResource(_backgroundHistory);
        _backgroundHistory = null;
        _captureBackground = _holdBackground = _transitionPending = false;
        _backgroundAlpha = _transitionElapsed = 0;
    }
}
