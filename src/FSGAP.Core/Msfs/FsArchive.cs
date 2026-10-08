using System.Text;
using System.Text.Json;

namespace FSGAP.Core.Msfs;

/// <summary>One file stored in an MSFS 2024 package archive.</summary>
/// <param name="Path">Path inside the package, as the archive stores it (backslash-separated, typically lower case).</param>
/// <param name="Offset">Byte offset of the stored data, relative to the end of the header.</param>
/// <param name="StoredSize">Stored size in bytes.</param>
/// <param name="Size">Uncompressed size in bytes.</param>
public sealed record FsArchiveEntry(string Path, long Offset, long StoredSize, long Size)
{
    /// <summary>True when the entry is stored as is (no compression), so its bytes can be read directly.</summary>
    public bool IsStoredRaw => StoredSize == Size;
}

/// <summary>
/// Read-only access to the MSFS 2024 package archive (<c>*.fsarchive</c>) — ONLY the unencrypted variant, and only its
/// uncompressed entries. Observed layout (MSFS 2024 streamed packages, BLOCK W3-RC3C-B1):
/// <c>"RASA"</c> magic, uint32 version 2 at offset 4, uint32 header length at offset 12, a UTF-8 JSON header at offset
/// 32 (<c>{"encryptionSetup":{"scheme":"notEncrypted",…},"fileInfoList":[{"path","byteOffset","byteSize",
/// "uncompressed_size","hash"},…]}</c>), then the data. Anything else — another magic or version, an encrypted scheme,
/// an unreadable header — is reported as "not readable" (null), never guessed at; a compressed entry is never decoded.
/// </summary>
public static class FsArchive
{
    private const int HeaderOffset = 32;
    private const int MaxHeaderBytes = 64 * 1024 * 1024;
    private static ReadOnlySpan<byte> Magic => "RASA"u8;

    /// <summary>The entries of an unencrypted archive; null when the file is not one (or cannot be read).</summary>
    public static IReadOnlyList<FsArchiveEntry>? TryReadIndex(string archivePath)
    {
        try
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[HeaderOffset];
            if (stream.Read(head) != HeaderOffset || !head[..4].SequenceEqual(Magic) || BitConverter.ToUInt32(head[4..8]) != 2)
            {
                return null;
            }

            var headerLength = BitConverter.ToUInt32(head[12..16]);
            if (headerLength == 0 || headerLength > MaxHeaderBytes || HeaderOffset + (long)headerLength > stream.Length)
            {
                return null;
            }

            var json = new byte[headerLength];
            stream.ReadExactly(json);
            using var doc = JsonDocument.Parse(TrimTrailingZeros(json));
            var root = doc.RootElement;
            if (!root.TryGetProperty("encryptionSetup", out var encryption)
                || !encryption.TryGetProperty("scheme", out var scheme)
                || scheme.GetString() != "notEncrypted"
                || !root.TryGetProperty("fileInfoList", out var files)
                || files.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var entries = new List<FsArchiveEntry>(files.GetArrayLength());
            foreach (var file in files.EnumerateArray())
            {
                if (file.TryGetProperty("path", out var path) && path.GetString() is { Length: > 0 } p
                    && file.TryGetProperty("byteOffset", out var offset) && offset.TryGetInt64(out var o)
                    && file.TryGetProperty("byteSize", out var stored) && stored.TryGetInt64(out var s)
                    && file.TryGetProperty("uncompressed_size", out var size) && size.TryGetInt64(out var u))
                {
                    entries.Add(new FsArchiveEntry(p, o, s, u));
                }
            }

            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The bytes of an uncompressed entry; null when it is compressed, out of bounds or unreadable.</summary>
    public static byte[]? TryReadEntry(string archivePath, FsArchiveEntry entry, long maxBytes = 1024 * 1024)
    {
        if (!entry.IsStoredRaw || entry.Size < 0 || entry.Size > maxBytes || entry.Offset < 0)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> head = stackalloc byte[HeaderOffset];
            if (stream.Read(head) != HeaderOffset)
            {
                return null;
            }

            var dataStart = HeaderOffset + (long)BitConverter.ToUInt32(head[12..16]);
            var start = dataStart + entry.Offset;
            if (start + entry.Size > stream.Length)
            {
                return null;
            }

            stream.Seek(start, SeekOrigin.Begin);
            var bytes = new byte[entry.Size];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Decodes an entry as UTF-8 text (BOM tolerated), or null.</summary>
    public static string? TryReadText(string archivePath, FsArchiveEntry entry) =>
        TryReadEntry(archivePath, entry) is { } bytes ? new UTF8Encoding(false).GetString(bytes).TrimStart('﻿') : null;

    private static ReadOnlyMemory<byte> TrimTrailingZeros(byte[] json)
    {
        var length = json.Length;
        while (length > 0 && json[length - 1] == 0)
        {
            length--;
        }

        return json.AsMemory(0, length);
    }
}
