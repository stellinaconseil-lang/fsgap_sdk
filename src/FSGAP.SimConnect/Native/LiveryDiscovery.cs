using System.Text;

namespace FSGAP.SimConnect.Native;

// BLOCK 10A.5 — EXPERIMENTAL, discovery only. Types of the livery-enumeration and AI-probe experiment. Internal; no
// production path uses them, and nothing here is part of the public API.

/// <summary>One (aircraft title, livery name) pair returned by livery enumeration.</summary>
internal readonly record struct RawLiveryEntry(string AircraftTitle, string LiveryName);

/// <summary>A complete livery enumeration answer, with what was learned about the packet layout.</summary>
internal sealed record LiveryEnumeration(
    IReadOnlyList<RawLiveryEntry> Entries,
    int Packets,
    int HeaderSize,
    int EntrySize,
    string FirstPacketHeaderHex,
    TimeSpan Elapsed);

/// <summary>Where to place a probe aircraft.</summary>
internal sealed record AiProbePosition(double LatitudeDegrees, double LongitudeDegrees, double AltitudeFeet, double HeadingDegrees, bool OnGround);

/// <summary>The identity strings read from one SimObject.</summary>
internal sealed record RawObjectIdentity(
    string? Title,
    string? LiveryName,
    string? LiveryFolder,
    string? AtcId,
    string? AtcAirline,
    string? AtcModel,
    string? AtcType);

/// <summary>One create / read / remove cycle.</summary>
internal sealed record AiProbeResult
{
    public required string ContainerTitle { get; init; }

    public required string Livery { get; init; }

    public required string TailNumber { get; init; }

    public int CreateHResult { get; init; }

    public uint? ObjectId { get; init; }

    public IReadOnlyList<string> Exceptions { get; init; } = [];

    public RawObjectIdentity? Identity { get; init; }

    /// <summary>The user aircraft's title read in the same cycle, to prove the probe did not read object 0.</summary>
    public string? UserAircraftTitle { get; init; }

    public TimeSpan CreateLatency { get; init; }

    public TimeSpan ReadLatency { get; init; }

    public int? RemoveHResult { get; init; }

    public bool RemovalConfirmed { get; init; }

    public TimeSpan RemoveLatency { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Bookkeeping of the AI objects the experiment created, so that nothing is left in the simulator: every created id is
/// recorded before anything else happens to it, and only a confirmed removal clears it.
/// </summary>
internal sealed class AiObjectLedger
{
    private readonly object _gate = new();
    private readonly HashSet<uint> _alive = [];
    private int _created;
    private int _removed;

    public int Created { get { lock (_gate) { return _created; } } }

    public int Removed { get { lock (_gate) { return _removed; } } }

    public IReadOnlyList<uint> Remaining { get { lock (_gate) { return _alive.Order().ToArray(); } } }

    public void RecordCreated(uint objectId)
    {
        lock (_gate)
        {
            if (_alive.Add(objectId))
            {
                _created++;
            }
        }
    }

    public void RecordRemoved(uint objectId)
    {
        lock (_gate)
        {
            if (_alive.Remove(objectId))
            {
                _removed++;
            }
        }
    }
}

/// <summary>
/// Parses one <c>SIMCONNECT_RECV_ENUMERATE_SIMOBJECT_AND_LIVERY_LIST</c> packet from managed bytes: the list template
/// header (size, version, id, request id, array size, entry number, out of) then <c>ArraySize</c> fixed-size entries,
/// each two NUL-terminated strings (aircraft title, livery name) in two equal halves.
/// </summary>
/// <remarks>
/// Entries are 512 bytes (2 × 256), verified live on 201 packets. The header is 28 bytes (documented) or 64 (MSFS 2024 was
/// seen adding 36 bytes to the airport list header); after it, the packet must hold at least <c>ArraySize</c> 512-byte
/// slots and a whole number of them. Anything else is a <see cref="FormatException"/>, never a guessed offset.
/// </remarks>
internal static class LiveryListParser
{
    internal const int DocumentedHeaderSize = 28;

    /// <summary>Two 256-character strings (aircraft title, livery name).</summary>
    internal const int DocumentedEntrySize = 512;

    internal sealed record Packet(uint RequestId, uint EntryNumber, uint OutOf, int HeaderSize, int EntrySize, IReadOnlyList<RawLiveryEntry> Entries);

    internal static uint RequestIdOf(ReadOnlySpan<byte> packet) =>
        packet.Length >= 16 ? BitConverter.ToUInt32(packet[12..16]) : throw new FormatException("Packet too short.");

    internal static Packet Parse(byte[] packet)
    {
        if (packet.Length < DocumentedHeaderSize)
        {
            throw new FormatException($"Livery list packet of {packet.Length} bytes is shorter than its header.");
        }

        var requestId = BitConverter.ToUInt32(packet, 12);
        var arraySize = BitConverter.ToUInt32(packet, 16);
        var entryNumber = BitConverter.ToUInt32(packet, 20);
        var outOf = BitConverter.ToUInt32(packet, 24);
        if (arraySize == 0)
        {
            return new Packet(requestId, entryNumber, outOf, DocumentedHeaderSize, 0, []);
        }

        // Observed live on MSFS 2024 (BLOCK 10A.5): a 40 988-byte packet announcing 79 entries = 28-byte header + 80 slots
        // of 512 bytes, i.e. the documented 2 × 256-char entry with room for one more slot than announced. So the
        // 28 + n × 512 layout (n ≥ ArraySize) is the only one accepted.
        (int Header, int Entry)? layout = null;
        foreach (var candidateHeader in new[] { DocumentedHeaderSize, 64 })
        {
            var rest = packet.Length - candidateHeader;
            if (rest >= arraySize * DocumentedEntrySize && rest % DocumentedEntrySize == 0)
            {
                layout = (candidateHeader, DocumentedEntrySize);
                break;
            }
        }

        if (layout is not { } found)
        {
            throw new FormatException($"Livery list packet of {packet.Length} bytes does not hold {arraySize} entries after a 28- or 64-byte header.");
        }

        var (header, entry) = found;
        var half = entry / 2;
        var entries = new RawLiveryEntry[arraySize];
        for (var i = 0; i < arraySize; i++)
        {
            var offset = header + (i * entry);
            entries[i] = new RawLiveryEntry(CString(packet, offset, half), CString(packet, offset + half, half));
        }

        return new Packet(requestId, entryNumber, outOf, header, entry, entries);
    }

    private static string CString(byte[] buffer, int offset, int length)
    {
        var span = buffer.AsSpan(offset, length);
        var end = span.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? span : span[..end]).Trim();
    }
}
