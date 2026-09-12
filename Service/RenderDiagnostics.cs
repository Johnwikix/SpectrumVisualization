using System;
using System.Diagnostics;
using System.IO;

namespace WinExSpectrumTest.Service;

// Opt-in diagnostics for repeatable desktop verification. No work in release builds.
internal static class RenderDiagnostics
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("SPECTRUM_DIAGNOSTICS") == "1";
    private static long _start;
    private static long _previous;
    private static long _publications;
    private static int _frames;
    private static double _maxInterval;

    [Conditional("DEBUG")]
    public static void RecordFrame(string? effect, long publications)
    {
        if (!Enabled) return;
        long now = Stopwatch.GetTimestamp();
        if (_start == 0) { _start = _previous = now; _publications = publications; }
        _maxInterval = Math.Max(_maxInterval, Stopwatch.GetElapsedTime(_previous, now).TotalMilliseconds);
        _previous = now;
        _frames++;
        double elapsed = Stopwatch.GetElapsedTime(_start, now).TotalSeconds;
        if (elapsed < 5) return;
        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "render-metrics.log"),
            $"{DateTime.Now:O} {effect}: FPS={_frames / elapsed:F1}, maxIntervalMs={_maxInterval:F1}, FFT/s={(publications - _publications) / elapsed:F1}\n");
        _start = now; _frames = 0; _maxInterval = 0; _publications = publications;
    }
}
