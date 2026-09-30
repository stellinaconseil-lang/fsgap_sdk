using System.Buffers.Binary;
using System.Text;

namespace FSGAP.SimConnect.Native;

/// <summary>One (aircraft title, livery name) pair of a livery enumeration answer, as decoded (trimmed, possibly empty).</summary>
internal readonly record struct RawLiveryEntry(string AircraftTitle, string LiveryName);

/// <summary>
/// Decodes one <c>SIMCONNECT_RECV_ENUMERATE_SIMOBJECT_AND_LIVERY_LIST</c> packet from managed bytes.
/// </summary>
/// <remarks>
/// <para><b>OFFICIAL SDK layout</b> (MSFS 2024 SimConnect documentation):</para>
/// <list type="bullet">
/// <item><description>
/// <c>SIMCONNECT_RECV</c> (<c>dwSize</c>, <c>dwVersion</c>, <c>dwID</c>), then <c>SIMCONNECT_RECV_LIST_TEMPLATE</c>
/// (<c>dwRequestID</c>, <c>dwArraySize</c> = elements in this packet, <c>dwEntryNumber</c> = index of this packet from
/// 0 to <c>dwOutOf</c> − 1, <c>dwOutOf</c> = number of packets of the list).
/// </description></item>
/// <item><description>
/// <c>rgData[dwArraySize]</c> of <c>SIMCONNECT_ENUMERATE_SIMOBJECT_LIVERY</c> =
/// <c>SIMCONNECT_STRING(AircraftTitle, 256)</c> + <c>SIMCONNECT_STRING(LiveryName, 256)</c>: 512 bytes per element.
/// </description></item>
/// </list>
/// <para><b>EMPIRICALLY CONFIRMED</b> (BLOCK 10A.5, MSFS 2024 1.8.16.0, five identical runs, 201 packets each):</para>
/// <list type="bullet">
/// <item><description>the seven header DWORDs are packed: the first element starts at byte 28;</description></item>
/// <item><description>
/// a packet can carry more 512-byte slots than <c>dwArraySize</c> (79 announced, 80 present): the extra slots are
/// ignored, only the first <c>dwArraySize</c> elements are read;
/// </description></item>
/// <item><description>strings are NUL-terminated ASCII in practice; they are decoded as UTF-8 with replacement.</description></item>
/// </list>
/// <para>
/// Every read is bounds-checked. A packet that is shorter than it claims, whose element count does not fit, or whose
/// payload is not a whole number of 512-byte slots is rejected with a <see cref="FormatException"/>; no offset is ever
/// guessed.
/// </para>
/// </remarks>
internal static class LiveryListParser
{
    /// <summary><c>SIMCONNECT_RECV_ID_ENUMERATE_SIMOBJECT_AND_LIVERY_LIST</c>.</summary>
    internal const uint MessageId = 38;

    /// <summary>SIMCONNECT_RECV (3 DWORDs) + SIMCONNECT_RECV_LIST_TEMPLATE (4 DWORDs), packed (empirically confirmed).</summary>
    internal const int HeaderSize = 28;

    /// <summary>Size of one <c>SIMCONNECT_STRING(…, 256)</c> field (official).</summary>
    internal const int StringFieldSize = 256;

    /// <summary>Two string fields per element (official).</summary>
    internal const int EntrySize = 2 * StringFieldSize;

    /// <summary>Sanity bound on the elements of one packet: far above the 79 observed, far below anything absurd.</summary>
    internal const uint MaxEntriesPerPacket = 4096;

    /// <summary>Sanity bound on the packets of one list (15 815 rows came in 201 packets).</summary>
    internal const uint MaxPackets = 100_000;

    internal sealed record Packet(uint RequestId, uint EntryNumber, uint OutOf, IReadOnlyList<RawLiveryEntry> Entries);

    /// <summary>The request id of a packet, read before anything else so that other requests' answers are skipped cheaply.</summary>
    /// <exception cref="FormatException">The packet is too short to hold a request id.</exception>
    internal static uint RequestIdOf(ReadOnlySpan<byte> packet) =>
        packet.Length >= 16
            ? BinaryPrimitives.ReadUInt32LittleEndian(packet[12..16])
            : throw new FormatException($"Livery list packet of {packet.Length} bytes is too short for a request id.");

    /// <exception cref="FormatException">The packet does not match the layout above.</exception>
    internal static Packet Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < HeaderSize)
        {
            throw new FormatException($"Livery list packet of {packet.Length} bytes is shorter than its {HeaderSize}-byte header.");
        }

        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(packet[0..4]);
        var id = BinaryPrimitives.ReadUInt32LittleEndian(packet[8..12]);
        var requestId = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..16]);
        var arraySize = BinaryPrimitives.ReadUInt32LittleEndian(packet[16..20]);
        var entryNumber = BinaryPrimitives.ReadUInt32LittleEndian(packet[20..24]);
        var outOf = BinaryPrimitives.ReadUInt32LittleEndian(packet[24..28]);

        if (id != MessageId)
        {
            throw new FormatException($"Packet id {id} is not a livery list ({MessageId}).");
        }

        if (declaredSize > (uint)packet.Length || declaredSize < HeaderSize)
        {
            throw new FormatException($"Livery list packet declares {declaredSize} bytes but {packet.Length} were received (truncated or corrupt).");
        }

        var body = packet[HeaderSize..(int)declaredSize];
        if (body.Length % EntrySize != 0)
        {
            throw new FormatException($"Livery list payload of {body.Length} bytes is not a whole number of {EntrySize}-byte elements.");
        }

        if (arraySize > MaxEntriesPerPacket || (long)arraySize * EntrySize > body.Length)
        {
            throw new FormatException($"Livery list packet announces {arraySize} elements but holds room for {body.Length / EntrySize}.");
        }

        if (outOf > MaxPackets || (outOf > 0 && entryNumber >= outOf) || (outOf == 0 && entryNumber != 0))
        {
            throw new FormatException($"Livery list packet {entryNumber} of {outOf} is out of range.");
        }

        var entries = new RawLiveryEntry[arraySize];
        for (var i = 0; i < entries.Length; i++)
        {
            var element = body.Slice(i * EntrySize, EntrySize);
            entries[i] = new RawLiveryEntry(Field(element[..StringFieldSize]), Field(element[StringFieldSize..]));
        }

        return new Packet(requestId, entryNumber, outOf, entries);
    }

    /// <summary>A fixed 256-byte string field: up to its first NUL (or the whole field), decoded, trimmed.</summary>
    private static string Field(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? field : field[..end]).Trim();
    }
}

/// <summary>
/// Collects the packets of one livery enumeration request into the complete list. Pure and single-threaded (the caller
/// serializes <see cref="Accept"/>): packets of other requests are ignored, the list is complete when packets
/// 0 … <c>dwOutOf</c> − 1 have all arrived, in any order.
/// </summary>
internal sealed class LiveryListAssembly(uint requestId)
{
    private readonly Dictionary<uint, IReadOnlyList<RawLiveryEntry>> _packets = [];
    private uint? _outOf;

    /// <summary>The request this assembly belongs to.</summary>
    public uint RequestId { get; } = requestId;

    /// <summary>Whether every packet of the list has arrived.</summary>
    public bool IsComplete => _outOf is { } outOf && _packets.Count >= Math.Max(outOf, 1u);

    /// <summary>Packets received so far.</summary>
    public int PacketCount => _packets.Count;

    /// <summary>
    /// Offers one raw packet. Returns <see langword="false"/> for a packet of another request (not an error).
    /// </summary>
    /// <exception cref="FormatException">
    /// The packet is malformed, disagrees with earlier packets on the packet count, or repeats a packet number.
    /// </exception>
    public bool Accept(ReadOnlySpan<byte> raw)
    {
        if (LiveryListParser.RequestIdOf(raw) != RequestId)
        {
            return false;
        }

        var packet = LiveryListParser.Parse(raw);
        if (_outOf is { } expected && expected != packet.OutOf)
        {
            throw new FormatException($"Livery list packets disagree on the packet count ({expected} then {packet.OutOf}).");
        }

        if (!_packets.TryAdd(packet.EntryNumber, packet.Entries))
        {
            throw new FormatException($"Livery list packet {packet.EntryNumber} was received twice.");
        }

        _outOf = packet.OutOf;
        return true;
    }

    /// <summary>The complete list, in packet order then element order.</summary>
    /// <exception cref="InvalidOperationException">The list is not complete yet.</exception>
    public IReadOnlyList<RawLiveryEntry> ToList()
    {
        if (!IsComplete)
        {
            throw new InvalidOperationException($"The livery list is incomplete ({_packets.Count} of {_outOf?.ToString() ?? "?"} packets).");
        }

        return _packets.OrderBy(p => p.Key).SelectMany(p => p.Value).ToArray();
    }
}
