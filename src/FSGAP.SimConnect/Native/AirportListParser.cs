using System.Buffers.Binary;

namespace FSGAP.SimConnect.Native;

/// <summary>An airport entry as the simulator sent it: unvalidated text, native units (altitude in meters).</summary>
internal readonly record struct RawAirport(string Ident, string Region, double Latitude, double Longitude, double AltitudeMeters);

/// <summary>One <c>SIMCONNECT_RECV_AIRPORT_LIST</c> packet, parsed.</summary>
/// <param name="RequestId">The request it answers.</param>
/// <param name="EntryNumber">0-based packet number.</param>
/// <param name="OutOf">Total number of packets of the answer.</param>
/// <param name="Airports">Well-formed entries.</param>
/// <param name="SkippedEntries">Entries whose identifier was not plain text (never trusted as an airport).</param>
internal sealed record AirportListPacket(uint RequestId, uint EntryNumber, uint OutOf, IReadOnlyList<RawAirport> Airports, int SkippedEntries);

/// <summary>
/// Reads <c>SIMCONNECT_RECV_AIRPORT_LIST</c> packets copied from the native buffer. No unsafe memory access: the bytes are
/// already a managed copy, every offset is checked against the packet's own size first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layout</b> (from the SimConnect SDK, cross-checked against real MSFS 2024 packets by FSHANGAR, 2026-08-29):
/// </para>
/// <list type="bullet">
/// <item><description>
/// header: <c>Size, Version, Id, RequestId, ArraySize, EntryNumber, OutOf</c> (7 × uint32 = 28 bytes, documented). Real
/// MSFS 2024 packets carry 36 more header bytes (64 in total: 1 entry → 100 bytes, 361 entries → 13,060 bytes). Both
/// sizes are accepted; the actual one is derived from the packet size and must be one of them;
/// </description></item>
/// <item><description>
/// entry <c>SIMCONNECT_DATA_FACILITY_AIRPORT</c>, packed, 36 bytes: <c>char ident[9]; char region[3]; double latitude,
/// longitude, altitude</c> (altitude in meters).
/// </description></item>
/// </list>
/// <para>
/// A size that fits neither header means the assumed layout is wrong for this simulator: the packet is rejected
/// (<see cref="FormatException"/>) rather than read at a guessed offset.
/// </para>
/// </remarks>
internal static class AirportListParser
{
    internal const int DocumentedHeaderSize = 28;
    internal const int ObservedHeaderSize = 64;
    internal const int EntrySize = 36;

    /// <exception cref="FormatException">The packet does not match the known layout.</exception>
    internal static AirportListPacket Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < DocumentedHeaderSize)
        {
            throw new FormatException($"Airport list packet of {packet.Length} bytes is smaller than its header.");
        }

        var requestId = BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]);
        var arraySize = BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]);
        var entryNumber = BinaryPrimitives.ReadUInt32LittleEndian(packet[20..]);
        var outOf = BinaryPrimitives.ReadUInt32LittleEndian(packet[24..]);
        var entriesBytes = (long)arraySize * EntrySize;
        var headerSize = packet.Length - entriesBytes;
        if (headerSize is not (DocumentedHeaderSize or ObservedHeaderSize))
        {
            throw new FormatException(
                $"Airport list layout mismatch: {packet.Length} bytes for {arraySize} entries of {EntrySize} bytes leaves a {headerSize}-byte header (expected {DocumentedHeaderSize} or {ObservedHeaderSize}).");
        }

        var airports = new List<RawAirport>((int)arraySize);
        var skipped = 0;
        for (var i = 0; i < arraySize; i++)
        {
            var entry = packet.Slice((int)headerSize + (i * EntrySize), EntrySize);
            var ident = Text(entry[..9]);
            if (ident is not { Length: > 0 })
            {
                skipped++;
                continue;
            }

            airports.Add(new RawAirport(
                ident,
                Text(entry.Slice(9, 3)) ?? string.Empty,
                BinaryPrimitives.ReadDoubleLittleEndian(entry[12..]),
                BinaryPrimitives.ReadDoubleLittleEndian(entry[20..]),
                BinaryPrimitives.ReadDoubleLittleEndian(entry[28..])));
        }

        return new AirportListPacket(requestId, entryNumber, outOf, airports, skipped);
    }

    /// <summary>
    /// A NUL-terminated field of plain ASCII letters, digits and spaces (trimmed), or <see langword="null"/> when it contains anything
    /// else (a sign the bytes are not text at all). Empty stays empty.
    /// </summary>
    private static string? Text(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        var text = end < 0 ? field : field[..end];
        foreach (var b in text)
        {
            if (b is not ((>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)' '))
            {
                return null;
            }
        }

        return System.Text.Encoding.ASCII.GetString(text).Trim();
    }
}
