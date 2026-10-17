using System;
using System.Threading;
using System.Threading.Tasks;
namespace WinExSpectrumTest.Rendering;

/// <summary>App-scoped maintenance. Only CPU shader compilation occurs here; live GPU resources remain owned by the render worker.</summary>
internal static class ShaderCacheMaintenance
{
    private static readonly object Gate = new();
    private static Task<int>? _work;
    private static bool _stopping;
    private static long _generation;
    internal static long Generation => Interlocked.Read(ref _generation);
    internal static event Action? Rebuilt;

    internal static Task<int> RebuildAsync()
    {
        lock (Gate)
        {
            if (_stopping) return Task.FromException<int>(new InvalidOperationException("Shader maintenance has stopped."));
            if (_work is { IsCompleted: false }) return _work;
            return _work = Task.Run(() =>
            {
                int count = ShaderCompiler.Rebuild(ShaderCatalog.ForEach);
                GpuPipelineCache.ClearForRebuild();
                Interlocked.Increment(ref _generation);
                Rebuilt?.Invoke();
                return count;
            });
        }
    }

    internal static Task StopAsync()
    {
        lock (Gate)
        {
            _stopping = true;
            return _work is null ? Task.CompletedTask : _work;
        }
    }
}
