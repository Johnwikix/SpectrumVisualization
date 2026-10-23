using System;
using System.Threading;

namespace WinExSpectrumTest.Helper;

/// <summary>Session-local instance ownership; activation also survives a first instance still starting.</summary>
internal sealed class SingleInstance : IDisposable
{
    private const string Name = @"Local\SennpaiStudio.SpectrumVisualization";
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _registration;
    private bool _owned;
    private bool _disposed;

    internal SingleInstance(string name = Name)
    {
        _mutex = new(false, name + ".Mutex");
        try { _activation = new(false, EventResetMode.AutoReset, name + ".Activate"); }
        catch (Exception error)
        {
            Service.AppLog.Caught(error, "Single-instance activation event creation failed.");
            _mutex.Dispose();
            throw;
        }
    }

    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_owned) return true;
        try { _owned = _mutex.WaitOne(0); }
        catch (AbandonedMutexException error)
        {
            Service.AppLog.Caught(error, "Previous instance ended without releasing ownership; recovered.");
            _owned = true;
        }
        return _owned;
    }

    public void RequestActivation() => _activation.Set();

    public void Listen(Action activate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) =>
        {
            try { activate(); }
            catch (Exception error) { Service.AppLog.Caught(error, "Existing-instance activation failed."); }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _registration?.Unregister(null); }
        finally
        {
            try { _activation.Dispose(); }
            finally
            {
                try
                {
                    // Main owns and releases the mutex on the same STA thread.
                    if (_owned) _mutex.ReleaseMutex();
                }
                finally { _mutex.Dispose(); }
            }
        }
    }
}
