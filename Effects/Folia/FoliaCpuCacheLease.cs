using System;

namespace WinExSpectrumTest.Effects.Folia;

/// <summary>Keeps cross-song CPU caches while scene preparation or cover extraction can use them.</summary>
internal sealed class FoliaCpuCacheLease : IDisposable
{
    private static readonly object Gate = new();
    private static int _owners;
    private bool _released;

    internal FoliaCpuCacheLease()
    {
        lock (Gate) _owners++;
    }

    // The owner must stop and join its CPU worker first, so it cannot refill a cleared cache.
    // A newly created effect takes its own lease before an outgoing effect can release the last one.
    public void Dispose()
    {
        lock (Gate)
        {
            if (_released) return;
            _released = true;
            if (--_owners != 0) return;
            FoliaSourcePlanner.ClearResults();
            FoliaGlyphCache.ClearMemory();
        }
    }
}
