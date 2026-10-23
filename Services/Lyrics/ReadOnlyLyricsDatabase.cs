using WinExSpectrumTest.Service;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinExSpectrumTest.Services.Lyrics;

/// <summary>
/// Short-lived, private SQLite reader. Its VFS rejects writable opens/deletes and exposes only v1 file
/// methods, so SQLite cannot create or modify a WAL shared-memory index, even if journal mode changes.
/// WAL databases deliberately fall back to other lyric sources rather than relaxing the read-only contract.
/// </summary>
internal sealed unsafe partial class ReadOnlyLyricsDatabase : IDisposable
{
    private const int ReadOnly = 1, Row = 100, Done = 101;
    private const string VfsName = "spectrum-lyrics-readonly";
    private static readonly Lock VfsGate = new();
    private static readonly Dictionary<nint, nint> FileMethods = [];
    private static nint _baseVfs;
    private static bool _registered;
    private nint _database;

    internal ReadOnlyLyricsDatabase(string path)
    {
        RegisterVfs();
        int result = Native.Open(path, out _database, ReadOnly | 0x10000 /* FULLMUTEX */, VfsName);
        if (result != 0)
        {
            Dispose();
            throw new IOException($"Cannot open the lyrics database read-only ({result}).");
        }
        Native.BusyTimeout(_database, 100);
    }

    internal List<string?[]> Query(string sql, CancellationToken token, params string[] parameters)
    {
        token.ThrowIfCancellationRequested();
        using var registration = token.Register(() => Native.Interrupt(_database));
        nint statement = 0;
        try
        {
            Check(Native.Prepare(_database, sql, -1, out statement, 0));
            if (Native.StatementReadOnly(statement) == 0) throw new InvalidOperationException("Only read-only SQL is allowed.");
            for (int i = 0; i < parameters.Length; i++)
                Check(Native.BindText(statement, i + 1, parameters[i], -1, -1));
            var rows = new List<string?[]>();
            int result;
            while ((result = Native.Step(statement)) == Row)
            {
                token.ThrowIfCancellationRequested();
                var row = new string?[Native.ColumnCount(statement)];
                for (int i = 0; i < row.Length; i++)
                {
                    if (Native.ColumnBytes(statement, i) > 2 * 1024 * 1024) throw new FormatException("Lyrics field exceeds the size limit.");
                    row[i] = Marshal.PtrToStringUTF8(Native.ColumnText(statement, i));
                }
                rows.Add(row);
                if (rows.Count > 128) throw new FormatException("Lyrics query exceeds the row limit.");
            }
            token.ThrowIfCancellationRequested();
            if (result != Done) Check(result);
            return rows;
        }
        finally { if (statement != 0) Native.Finalize(statement); }
    }

    internal bool HasTable(string name, CancellationToken token) =>
        Query("SELECT name FROM sqlite_master WHERE type='table' AND name=? LIMIT 1", token, name).Count != 0;

    private static void Check(int result)
    {
        if (result != 0) throw new IOException($"Read-only lyrics query failed ({result}).");
    }

    public void Dispose()
    {
        if (_database == 0) return;
        Native.Close(_database);
        _database = 0;
    }

    private static void RegisterVfs()
    {
        lock (VfsGate)
        {
            if (_registered) return;
            _baseVfs = Native.FindVfs(null);
            if (_baseVfs == 0) throw new IOException("Windows SQLite VFS is unavailable.");
            var vfs = *(Vfs*)_baseVfs;
            vfs.Next = 0;
            vfs.Name = Marshal.StringToCoTaskMemUTF8(VfsName);
            vfs.Open = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, nint, int, int*, int>)&OpenReadOnly;
            vfs.Delete = (nint)(delegate* unmanaged[Cdecl]<nint, byte*, int, int>)&RejectDelete;
            // SQLite retains these pointers globally. They intentionally live until process exit.
            var copy = (Vfs*)NativeMemory.Alloc((nuint)sizeof(Vfs));
            *copy = vfs;
            Check(Native.RegisterVfs((nint)copy, 0));
            _registered = true;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OpenReadOnly(nint vfs, byte* path, nint file, int flags, int* outputFlags)
    {
        // No READWRITE, CREATE, DELETEONCLOSE or unnamed temporary files in the player's folders.
        if ((flags & ReadOnly) == 0 || (flags & (2 | 4 | 8)) != 0 || path == null) return 8; // SQLITE_READONLY
        try
        {
            var open = (delegate* unmanaged[Cdecl]<nint, byte*, nint, int, int*, int>)((Vfs*)_baseVfs)->Open;
            int result = open(_baseVfs, path, file, flags, outputFlags);
            if (result != 0) return result;
            nint original = *(nint*)file;
            lock (VfsGate)
            {
                if (!FileMethods.TryGetValue(original, out nint replacement))
                {
                    var methods = *(IoMethods*)original;
                    methods.Version = 1; // No xShmMap/xShmLock: never open or write the source -shm file.
                    methods.Write = (nint)(delegate* unmanaged[Cdecl]<nint, nint, int, long, int>)&RejectWrite;
                    methods.Truncate = (nint)(delegate* unmanaged[Cdecl]<nint, long, int>)&RejectTruncate;
                    var copy = (IoMethods*)NativeMemory.Alloc((nuint)sizeof(IoMethods));
                    *copy = methods;
                    replacement = (nint)copy;
                    FileMethods.Add(original, replacement);
                }
                *(nint*)file = replacement;
            }
            return 0;
        }
        catch (global::System.Exception caughtException) { AppLog.Caught(caughtException); return 7; } // SQLITE_NOMEM; exceptions must never cross a native callback.
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RejectDelete(nint vfs, byte* path, int sync) => 8;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RejectWrite(nint file, nint buffer, int amount, long offset) => 8;
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int RejectTruncate(nint file, long size) => 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct Vfs
    {
        internal int Version, FileSize, MaxPath;
        internal nint Next, Name, AppData, Open, Delete, Access, FullPathname, DlOpen, DlError, DlSym, DlClose,
            Randomness, Sleep, CurrentTime, LastError, CurrentTimeInt64, SetSystemCall, GetSystemCall, NextSystemCall;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoMethods
    {
        internal int Version;
        internal nint Close, Read, Write, Truncate, Sync, FileSize, Lock, Unlock, CheckReservedLock,
            FileControl, SectorSize, DeviceCharacteristics, ShmMap, ShmLock, ShmBarrier, ShmUnmap, Fetch, Unfetch;
    }

    private static partial class Native
    {
        private const string Library = "winsqlite3.dll";
        [LibraryImport(Library, EntryPoint = "sqlite3_open_v2", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Open(string path, out nint database, int flags, string vfs);
        [LibraryImport(Library, EntryPoint = "sqlite3_close_v2")] internal static partial int Close(nint database);
        [LibraryImport(Library, EntryPoint = "sqlite3_busy_timeout")] internal static partial int BusyTimeout(nint database, int milliseconds);
        [LibraryImport(Library, EntryPoint = "sqlite3_interrupt")] internal static partial void Interrupt(nint database);
        [LibraryImport(Library, EntryPoint = "sqlite3_prepare_v2", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int Prepare(nint database, string sql, int length, out nint statement, nint tail);
        [LibraryImport(Library, EntryPoint = "sqlite3_stmt_readonly")] internal static partial int StatementReadOnly(nint statement);
        [LibraryImport(Library, EntryPoint = "sqlite3_bind_text", StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int BindText(nint statement, int index, string value, int length, nint destructor);
        [LibraryImport(Library, EntryPoint = "sqlite3_step")] internal static partial int Step(nint statement);
        [LibraryImport(Library, EntryPoint = "sqlite3_column_count")] internal static partial int ColumnCount(nint statement);
        [LibraryImport(Library, EntryPoint = "sqlite3_column_text")] internal static partial nint ColumnText(nint statement, int index);
        [LibraryImport(Library, EntryPoint = "sqlite3_column_bytes")] internal static partial int ColumnBytes(nint statement, int index);
        [LibraryImport(Library, EntryPoint = "sqlite3_finalize")] internal static partial int Finalize(nint statement);
        [LibraryImport(Library, EntryPoint = "sqlite3_vfs_find", StringMarshalling = StringMarshalling.Utf8)] internal static partial nint FindVfs(string? name);
        [LibraryImport(Library, EntryPoint = "sqlite3_vfs_register")] internal static partial int RegisterVfs(nint vfs, int makeDefault);
    }
}
