using System;
using System.Collections.Generic;
using System.Threading;

namespace WinExSpectrumTest.Effects.Folia;

internal readonly record struct FoliaGlyphKey(string Family, int Weight, bool Italic, string Text);
internal readonly record struct FoliaGlyphRect(int Page, int X, int Y, int Width, int Height);
internal sealed record FoliaGlyphSource(FoliaGlyphOutline Outline, int Width, int Height, float X, float Y,
    float Advance, float BearingX, float BearingY, float CenterY, float LineHeight);

internal sealed class FoliaCachedGlyph(FoliaGlyphKey key, FoliaGlyphSource source, FoliaGlyphRect rect)
{
    internal FoliaGlyphKey Key { get; } = key;
    internal FoliaGlyphSource Source { get; } = source;
    internal FoliaGlyphRect Rect { get; } = rect;
    internal FoliaGlyph Glyph { get; } = new((float)rect.X / FoliaGlyphStore.PageSize, (float)rect.Y / FoliaGlyphStore.PageSize,
        (float)rect.Width / FoliaGlyphStore.PageSize, (float)rect.Height / FoliaGlyphStore.PageSize,
        source.Width / 96f, source.Height / 96f, source.Advance / 96f, source.BearingX / 96f, source.BearingY / 96f,
        source.CenterY, source.LineHeight, rect.Page);
    internal int Owners;
    internal long LastUse;
    internal volatile bool Resident;
    internal byte[]? CpuTile;
}

internal sealed class FoliaAtlasCapacityException() : Exception("The live glyph scenes exceed the incremental atlas capacity.");

/// <summary>CPU metadata and slot allocator. A scene pins every referenced glyph until its last render lease ends.</summary>
internal sealed class FoliaGlyphStore : IDisposable
{
    internal const int PageSize = 2048, MaximumPages = 8;
    private readonly object _gate = new();
    private readonly Dictionary<FoliaGlyphKey, FoliaCachedGlyph> _entries = new();
    private readonly List<FoliaGlyphRect> _free = new();
    private long _use;
    private int _pages;
    private bool _disposed;
    private volatile bool _gpuEnabled;
    internal bool GpuEnabled => _gpuEnabled;
    internal long Reused { get; private set; }
    internal long Evicted { get; private set; }
    internal int Count { get { lock (_gate) return _entries.Count; } }
    internal void SetGpuEnabled(bool enabled) => _gpuEnabled = enabled;

    internal bool TryAcquire(FoliaGlyphKey key, out FoliaCachedGlyph? glyph)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_entries.TryGetValue(key, out glyph)) return false;
            glyph.Owners++;
            glyph.LastUse = ++_use;
            Reused++;
            return true;
        }
    }

    internal FoliaCachedGlyph Acquire(FoliaGlyphKey key, Func<FoliaGlyphSource> create)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out var found))
            {
                found.Owners++;
                found.LastUse = ++_use;
                Reused++;
                return found;
            }
        }
        var source = create();
        bool transferred = false;
        try
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_entries.TryGetValue(key, out var found))
                {
                    found.Owners++;
                    found.LastUse = ++_use;
                    Reused++;
                    return found;
                }
                var entry = new FoliaCachedGlyph(key, source, Allocate(source.Width, source.Height)) { Owners = 1, LastUse = ++_use };
                _entries.Add(key, entry);
                transferred = true;
                return entry;
            }
        }
        finally { if (!transferred) source.Outline.Dispose(); }
    }

    private FoliaGlyphRect Allocate(int width, int height)
    {
        if (width > PageSize || height > PageSize) throw new FoliaAtlasCapacityException();
        while (true)
        {
            int best = -1, waste = int.MaxValue;
            for (int i = 0; i < _free.Count; i++)
            {
                var slot = _free[i];
                int remaining = slot.Width * slot.Height - width * height;
                if (slot.Width >= width && slot.Height >= height && remaining < waste) { best = i; waste = remaining; }
            }
            if (best >= 0)
            {
                var slot = _free[best];
                _free.RemoveAt(best);
                if (slot.Width > width) _free.Add(new(slot.Page, slot.X + width, slot.Y, slot.Width - width, height));
                if (slot.Height > height) _free.Add(new(slot.Page, slot.X, slot.Y + height, slot.Width, slot.Height - height));
                return new(slot.Page, slot.X, slot.Y, width, height);
            }
            // Keep a small cross-song working set before evicting older songs. Extra
            // pages above this budget are reserved for simultaneously pinned scenes.
            if (_pages < 4)
            {
                _free.Add(new(_pages++, 0, 0, PageSize, PageSize));
                continue;
            }
            // Reclaim unreferenced glyphs before growing. Active and in-flight scenes are never evicted.
            FoliaCachedGlyph? oldest = null;
            foreach (var entry in _entries.Values)
                if (entry.Owners == 0 && (oldest == null || entry.LastUse < oldest.LastUse)) oldest = entry;
            if (oldest != null)
            {
                _entries.Remove(oldest.Key);
                _free.Add(oldest.Rect);
                Coalesce();
                oldest.Source.Outline.Dispose();
                Evicted++;
                continue;
            }
            if (_pages == MaximumPages) throw new FoliaAtlasCapacityException();
            _free.Add(new(_pages++, 0, 0, PageSize, PageSize));
        }
    }

    private void Coalesce()
    {
        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < _free.Count && !merged; i++)
                for (int j = i + 1; j < _free.Count; j++)
                {
                    var a = _free[i]; var b = _free[j];
                    if (a.Page != b.Page) continue;
                    FoliaGlyphRect combined;
                    if (a.Y == b.Y && a.Height == b.Height && (a.X + a.Width == b.X || b.X + b.Width == a.X))
                        combined = new(a.Page, Math.Min(a.X, b.X), a.Y, a.Width + b.Width, a.Height);
                    else if (a.X == b.X && a.Width == b.Width && (a.Y + a.Height == b.Y || b.Y + b.Height == a.Y))
                        combined = new(a.Page, a.X, Math.Min(a.Y, b.Y), a.Width, a.Height + b.Height);
                    else continue;
                    _free[i] = combined;
                    _free.RemoveAt(j);
                    merged = true;
                    break;
                }
        } while (merged);
    }

    internal void Release(FoliaCachedGlyph[] glyphs)
    {
        lock (_gate)
            foreach (var entry in glyphs)
                if (entry != null) entry.Owners--;
    }

    // Only the CPU worker calls this. Scratch belongs to this synchronous operation,
    // and no font/outline generation can migrate onto the render thread during fallback.
    internal void PrepareCpuTiles(FoliaCachedGlyph[] glyphs, CancellationToken token)
    {
        // A render-side fallback may disable GPU generation concurrently. Keep one
        // snapshot for sizing and filling, then the next request handles that change.
        bool useGpu = GpuEnabled;
        int maximum = 0;
        foreach (var glyph in glyphs)
            if (!glyph.Resident && (!useGpu || !glyph.Source.Outline.CanGenerateOnGpu) && glyph.CpuTile == null)
                maximum = Math.Max(maximum, glyph.Rect.Width * glyph.Rect.Height);
        if (maximum == 0) return;
        var field = new float[maximum * 4];
        var coverage = new float[maximum];
        var horizontal = new float[maximum];
        var kernel = FoliaGlyphShadow.Kernel(96 * .09f);
        foreach (var glyph in glyphs)
        {
            token.ThrowIfCancellationRequested();
            if (glyph.Resident || useGpu && glyph.Source.Outline.CanGenerateOnGpu || glyph.CpuTile != null) continue;
            var source = glyph.Source;
            glyph.CpuTile = FoliaGlyphCache.Get(source.Outline, source.Width, source.Height, source.X, source.Y, field, coverage, horizontal, kernel);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var glyph in _entries.Values) glyph.Source.Outline.Dispose();
            _entries.Clear();
            _free.Clear();
        }
    }
}
