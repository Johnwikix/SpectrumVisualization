using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WinExSpectrumTest.Service;

/// <summary>Bounded, asynchronous local diagnostics. Rendering threads never perform file I/O.</summary>
internal static class AppLog
{
    private const long FileSizeLimit = 4 * 1024 * 1024;
    private const int RetainedFileCount = 30;
    private static readonly object FileGate = new();
    private static readonly object RateGate = new();
    private static readonly Dictionary<(string File, int Line, Type Type, int HResult, string Message), Rate> Rates = new();
    private static readonly Channel<Entry> Pending = Channel.CreateBounded<Entry>(new BoundedChannelOptions(256)
    {
        SingleReader = false, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
    });
    private static Task? _writer;
    private static StreamWriter? _stream;
    private static DateOnly _date;
    private static string? _directory;
    private static long _dropped;
    private static int _stopped;

    public static string DirectoryPath => _directory ?? throw new InvalidOperationException("Diagnostics have not been initialized.");

    public static void Initialize(string directory)
    {
        _directory = directory;
        // File/directory creation belongs to the writer. An unwritable log must not prevent startup.
        _writer = Task.Run(WriteLoopAsync);
    }

    public static void SetDirectory(string directory)
    {
        lock (FileGate)
        {
            _stream?.Dispose();
            _stream = null;
            _directory = directory;
        }
    }

    public static void Info(string message) => Enqueue(new(DateTimeOffset.Now, "INF", message, null));

    public static void Caught(Exception exception, string? message = null,
        [CallerMemberName] string member = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        // Keep the first full exception for every call site; summarize repeating failures every 30 seconds.
        // This also bounds formatting/queue traffic if a driver fails on every frame.
        long suppressed = 0;
        lock (RateGate)
        {
            var key = (file, line, exception.GetType(), exception.HResult, exception.Message);
            long now = Environment.TickCount64;
            if (Rates.TryGetValue(key, out Rate? rate))
            {
                if (now - rate.Last < 30_000) { rate.Suppressed++; return; }
                suppressed = rate.Suppressed;
                rate.Suppressed = 0;
                rate.Last = now;
            }
            else if (Rates.Count < 1024) Rates.Add(key, new Rate { Last = now });
        }
        string location = $"{Path.GetFileName(file)}:{line} {member}";
        string detail = $"{location}: {message ?? "Caught exception"}";
        if (suppressed != 0) detail += $" (repeated {suppressed} times since previous entry)";
        Enqueue(new(DateTimeOffset.Now, exception is OperationCanceledException ? "INF" : "ERR", detail, exception));
    }

    public static void Fatal(string source, Exception? exception, string? message = null)
    {
        // Fatal callbacks may be the last managed code to run. Bypass the background queue and flush now.
        lock (FileGate)
        {
            while (Pending.Reader.TryRead(out Entry entry)) WriteSafely(entry);
            WriteSafely(new(DateTimeOffset.Now, "FTL", $"{source}: {message}", exception));
            FlushSafely();
        }
    }

    public static void Shutdown()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        lock (RateGate)
        {
            foreach (var (key, rate) in Rates)
                if (rate.Suppressed != 0)
                    Pending.Writer.TryWrite(new(DateTimeOffset.Now, "WRN",
                        $"{Path.GetFileName(key.File)}:{key.Line}: suppressed {rate.Suppressed} repeated exceptions (HRESULT 0x{key.HResult:X8}).", null));
        }
        Pending.Writer.TryComplete();
        try { _writer?.GetAwaiter().GetResult(); }
        catch (Exception error) { Debug.WriteLine(error); } // Logger failures cannot recursively log themselves.
        lock (FileGate)
        {
            FlushSafely();
            try { _stream?.Dispose(); }
            catch (Exception error) { Debug.WriteLine(error); }
            _stream = null;
        }
    }

    private static void Enqueue(Entry entry)
    {
        if (Volatile.Read(ref _stopped) == 0 && !Pending.Writer.TryWrite(entry)) Interlocked.Increment(ref _dropped);
    }

    private static async Task WriteLoopAsync()
    {
        await foreach (Entry entry in Pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            lock (FileGate)
            {
                long dropped = Interlocked.Exchange(ref _dropped, 0);
                if (dropped != 0) WriteSafely(new(DateTimeOffset.Now, "WRN", $"Log queue full: {dropped} entries dropped.", null));
                WriteSafely(entry);
            }
        }
    }

    private static void WriteSafely(Entry entry)
    {
        try
        {
            DateOnly date = DateOnly.FromDateTime(entry.Time.LocalDateTime);
            if (_stream == null || date != _date || _stream.BaseStream.Length >= FileSizeLimit)
            {
                _stream?.Dispose();
                _stream = null;
                Directory.CreateDirectory(DirectoryPath);
                // Separate files per process also allow a rejected second instance to leave diagnostics.
                string prefix = $"Spectrum-{date:yyyyMMdd}-{Environment.ProcessId}";
                int sequence = 0;
                string path;
                do { path = Path.Combine(DirectoryPath, $"{prefix}-{sequence++:D3}.log"); }
                while (File.Exists(path) && new FileInfo(path).Length >= FileSizeLimit);
                _stream = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
                _date = date;
                PruneSafely(path);
            }
            _stream.WriteLine($"{entry.Time:yyyy-MM-dd HH:mm:ss.fff zzz} [{entry.Level}] [PID {Environment.ProcessId}] {entry.Message}");
            if (entry.Exception != null)
            {
                _stream.WriteLine($"HRESULT=0x{entry.Exception.HResult:X8}");
                _stream.WriteLine(entry.Exception.ToString());
            }
        }
        catch (Exception error)
        {
            Debug.WriteLine(error); // Disk full/access denied must not replace the original failure.
            try { _stream?.Dispose(); } catch (Exception disposal) { Debug.WriteLine(disposal); }
            _stream = null;
        }
    }

    private static void PruneSafely(string activePath)
    {
        try
        {
            var files = new DirectoryInfo(DirectoryPath).GetFiles("Spectrum-*.log");
            Array.Sort(files, static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            for (int i = RetainedFileCount; i < files.Length; i++)
                if (!string.Equals(files[i].FullName, activePath, StringComparison.OrdinalIgnoreCase))
                    try { files[i].Delete(); } catch (Exception error) { Debug.WriteLine(error); }
        }
        catch (Exception error) { Debug.WriteLine(error); }
    }

    private static void FlushSafely()
    {
        try
        {
            _stream?.Flush();
            if (_stream?.BaseStream is FileStream file) file.Flush(flushToDisk: true);
        }
        catch (Exception error) { Debug.WriteLine(error); }
    }

    private sealed class Rate { public long Last; public long Suppressed; }
    private readonly record struct Entry(DateTimeOffset Time, string Level, string Message, Exception? Exception);
}
