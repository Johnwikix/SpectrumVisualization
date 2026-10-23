using WinExSpectrumTest.Service;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Security;
using System.Security.Cryptography;

namespace WinExSpectrumTest.Rendering;

/// <summary>Cold-path, checksummed artifacts. Read-only installs and incomplete writes are ordinary cache misses.</summary>
internal static class RenderCacheFile
{
    private const int HeaderSize = 44;
    internal static string DirectoryFor(string name) => Path.Combine(AppContext.BaseDirectory, name);

    internal static byte[]? Read(string directory, string key, int maximumBytes)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(directory, key + ".bin"));
            Span<byte> header = stackalloc byte[HeaderSize];
            stream.ReadExactly(header);
            if (!header[..8].SequenceEqual("SPCACHE1"u8)) return null;
            int length = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            if (length <= 0 || length > maximumBytes || stream.Length != HeaderSize + (long)length) return null;
            byte[] payload = new byte[length];
            stream.ReadExactly(payload);
            Span<byte> checksum = stackalloc byte[32];
            SHA256.HashData(payload, checksum);
            return CryptographicOperations.FixedTimeEquals(header[12..], checksum) ? payload : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { AppLog.Caught(ex); return null; }
    }

    // Delete only artifacts this cache owns, never recurse or remove an installation directory.
    internal static void Clear(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string path in Directory.EnumerateFiles(directory, "*.bin"))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length == 64 && name.AsSpan().IndexOfAnyExcept("0123456789ABCDEF".AsSpan()) < 0)
                File.Delete(path);
        }
    }

    internal static bool Write(string directory, string key, ReadOnlySpan<byte> payload)
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, key + ".bin");
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Span<byte> header = stackalloc byte[HeaderSize];
                "SPCACHE1"u8.CopyTo(header);
                BinaryPrimitives.WriteInt32LittleEndian(header[8..], payload.Length);
                SHA256.HashData(payload, header[12..]);
                stream.Write(header);
                stream.Write(payload);
            }
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { AppLog.Caught(ex); return false; }
        finally
        {
            if (temporary != null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { AppLog.Caught(ex); }
            }
        }
    }
}
