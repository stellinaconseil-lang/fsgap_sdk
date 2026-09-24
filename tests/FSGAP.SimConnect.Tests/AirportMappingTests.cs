using System.Buffers.Binary;
using System.Text;
using FSGAP.Abstractions.Geography;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Services;

namespace FSGAP.SimConnect.Tests;

/// <summary>Native airport-list packets and the selection of the answer, without a simulator.</summary>
public class AirportMappingTests
{
    private static readonly GeoPosition ParkedAtNice = new(43.664477, 7.226887);

    /// <summary>Builds a packet the way the simulator lays it out.</summary>
    internal static byte[] Packet(uint requestId, (string Ident, string Region, double Lat, double Lon, double AltMeters)[] entries,
        int headerSize = AirportListParser.ObservedHeaderSize, uint entryNumber = 0, uint outOf = 1)
    {
        var bytes = new byte[headerSize + (entries.Length * AirportListParser.EntrySize)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 18); // SIMCONNECT_RECV_ID_AIRPORT_LIST
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), requestId);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)entries.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(20), entryNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24), outOf);
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = bytes.AsSpan(headerSize + (i * AirportListParser.EntrySize), AirportListParser.EntrySize);
            Encoding.ASCII.GetBytes(entries[i].Ident).CopyTo(entry);
            Encoding.ASCII.GetBytes(entries[i].Region).CopyTo(entry[9..]);
            BinaryPrimitives.WriteDoubleLittleEndian(entry[12..], entries[i].Lat);
            BinaryPrimitives.WriteDoubleLittleEndian(entry[20..], entries[i].Lon);
            BinaryPrimitives.WriteDoubleLittleEndian(entry[28..], entries[i].AltMeters);
        }

        return bytes;
    }

    // ---- packets -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(AirportListParser.ObservedHeaderSize)]
    [InlineData(AirportListParser.DocumentedHeaderSize)]
    public void A_packet_is_read_with_the_observed_or_the_documented_header(int headerSize)
    {
        var packet = AirportListParser.Parse(Packet(7, [("LFMN", "LF", 43.665278, 7.215, 4.9), ("LFMD", "LF", 43.546389, 6.954167, 2.4)], headerSize, 1, 3));

        Assert.Equal(7u, packet.RequestId);
        Assert.Equal(1u, packet.EntryNumber);
        Assert.Equal(3u, packet.OutOf);
        Assert.Equal(["LFMN", "LFMD"], packet.Airports.Select(a => a.Ident));
        Assert.Equal("LF", packet.Airports[0].Region);
        Assert.Equal(43.665278, packet.Airports[0].Latitude);
        Assert.Equal(7.215, packet.Airports[0].Longitude);
        Assert.Equal(4.9, packet.Airports[0].AltitudeMeters);
    }

    [Fact]
    public void The_sizes_recorded_live_on_msfs_2024_are_accepted()
    {
        // FSHANGAR live captures, 2026-08-29: 1 entry → 100 bytes, 361 entries → 13,060 bytes (64-byte header).
        Assert.Equal(100, Packet(1, [("LFMN", "LF", 43.6, 7.2, 4)]).Length);
        Assert.Equal(13_060, Packet(1, Enumerable.Range(0, 361).Select(i => ($"A{i:D3}", "LF", 43.0, 7.0, 0.0)).ToArray()).Length);
        Assert.Equal(361, AirportListParser.Parse(Packet(1, Enumerable.Range(0, 361).Select(i => ($"A{i:D3}", "LF", 43.0, 7.0, 0.0)).ToArray())).Airports.Count);
    }

    [Fact]
    public void A_size_that_fits_no_known_layout_is_rejected_not_guessed()
    {
        var packet = Packet(1, [("LFMN", "LF", 43.6, 7.2, 4)]);
        var wrong = new byte[packet.Length + 4];
        packet.CopyTo(wrong, 0);

        Assert.Throws<FormatException>(() => AirportListParser.Parse(wrong));
        Assert.Throws<FormatException>(() => AirportListParser.Parse(new byte[10]));
    }

    [Fact]
    public void An_entry_whose_identifier_is_not_text_is_skipped()
    {
        var packet = Packet(1, [("LFMN", "LF", 43.6, 7.2, 4), ("XXXX", "LF", 43.0, 7.0, 0)]);
        packet[AirportListParser.ObservedHeaderSize + AirportListParser.EntrySize] = 0xFF; // corrupt the 2nd identifier

        var parsed = AirportListParser.Parse(packet);

        Assert.Equal(["LFMN"], parsed.Airports.Select(a => a.Ident));
        Assert.Equal(1, parsed.SkippedEntries);
    }

    [Fact]
    public void An_empty_list_is_an_empty_packet()
    {
        Assert.Empty(AirportListParser.Parse(Packet(1, [])).Airports);
    }

    // ---- selection -----------------------------------------------------------------------------------------------

    private static RawAirport Raw(string ident, double lat, double lon, string region = "LF", double altMeters = 0) =>
        new(ident, region, lat, lon, altMeters);

    [Fact]
    public void Identifiers_and_regions_are_trimmed_and_upper_cased_and_elevation_converted_to_feet()
    {
        var airport = Assert.Single(AirportSelection.Select([Raw(" lfmn ", 43.665278, 7.215, " lf", 4.9)], ParkedAtNice, AirportSearchOptions.Default));

        Assert.Equal("LFMN", airport.Icao);
        Assert.Equal("LF", airport.Region);
        Assert.Equal(16.08, airport.ElevationFeet!.Value, 2);
        Assert.Equal(new GeoPosition(43.665278, 7.215), airport.Position);
    }

    [Fact]
    public void Missing_metadata_stays_null_and_unusable_entries_are_dropped()
    {
        var result = AirportSelection.Select(
            [Raw("LF5QW", 43.689, 7.241, region: "", altMeters: double.NaN), Raw("", 43.6, 7.2), Raw("BAD", double.NaN, 7.2), Raw("FAR", 95, 7.2)],
            ParkedAtNice,
            AirportSearchOptions.Default);

        var airport = Assert.Single(result);
        Assert.Equal("LF5QW", airport.Icao);
        Assert.Null(airport.Region);
        Assert.Null(airport.ElevationFeet);
    }

    [Fact]
    public void Results_are_sorted_by_distance_limited_and_filtered_by_radius()
    {
        RawAirport[] list =
        [
            Raw("LFMD", 43.546389, 6.954167),   // 13 NM
            Raw("LFMN", 43.665278, 7.215),      // 0.5 NM
            Raw("LFKQP", 43.661982, 7.1838),    // 1.9 NM
            Raw("LFML", 43.4393, 5.2214),       // ~88 NM, outside 50 NM
        ];

        var all = AirportSelection.Select(list, ParkedAtNice, AirportSearchOptions.Default);
        var two = AirportSelection.Select(list, ParkedAtNice, new AirportSearchOptions { MaxResults = 2 });
        var tight = AirportSelection.Select(list, ParkedAtNice, new AirportSearchOptions { MaxDistanceNauticalMiles = 1 });

        Assert.Equal(["LFMN", "LFKQP", "LFMD"], all.Select(a => a.Icao));
        Assert.True(all.Zip(all.Skip(1)).All(p => p.First.DistanceNauticalMiles <= p.Second.DistanceNauticalMiles));
        Assert.Equal(["LFMN", "LFKQP"], two.Select(a => a.Icao));
        Assert.Equal(["LFMN"], tight.Select(a => a.Icao));
    }

    [Fact]
    public void A_duplicate_identifier_keeps_its_nearest_entry_whatever_the_order()
    {
        RawAirport near = Raw("LFMN", 43.665278, 7.215);
        RawAirport far = Raw("lfmn", 43.7, 7.3);

        var a = Assert.Single(AirportSelection.Select([far, near], ParkedAtNice, AirportSearchOptions.Default));
        var b = Assert.Single(AirportSelection.Select([near, far], ParkedAtNice, AirportSearchOptions.Default));

        Assert.Equal(near.Latitude, a.Position.LatitudeDegrees);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Equal_distances_are_ordered_by_identifier()
    {
        var from = new GeoPosition(0, 0);

        var result = AirportSelection.Select([Raw("BBBB", 0, 0.1), Raw("AAAA", 0, -0.1)], from, AirportSearchOptions.Default);

        Assert.Equal(["AAAA", "BBBB"], result.Select(a => a.Icao));
    }

    [Fact]
    public void Nearest_across_the_antimeridian()
    {
        var from = new GeoPosition(-17.0, 179.95);

        var result = AirportSelection.Select([Raw("EAST", -17.0, -179.95), Raw("WEST", -17.0, 179.0)], from, AirportSearchOptions.Default);

        Assert.Equal("EAST", result[0].Icao); // 0.1° away across 180°, not 359.9°
    }

    [Fact]
    public void No_airport_gives_an_empty_answer()
    {
        Assert.Empty(AirportSelection.Select([], ParkedAtNice, AirportSearchOptions.Default));
    }

    /// <summary>
    /// FSHANGAR's nearest-airport rule (NearestAirportMath.FindNearest: haversine in km, strict &lt;, first wins),
    /// ported verbatim as the parity reference. FSGAP must pick the same airport on any list without duplicates.
    /// </summary>
    private static string? LegacyNearest(double lat, double lon, IReadOnlyList<RawAirport> airports)
    {
        static double Rad(double d) => d * Math.PI / 180.0;
        string? best = null;
        var bestKm = double.MaxValue;
        foreach (var a in airports)
        {
            var dLat = Rad(a.Latitude - lat);
            var dLon = Rad(a.Longitude - lon);
            var h = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) + (Math.Cos(Rad(lat)) * Math.Cos(Rad(a.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
            var km = 6371.0 * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));
            if (km < bestKm)
            {
                bestKm = km;
                best = a.Ident;
            }
        }

        return best;
    }

    [Fact]
    public void Same_nearest_airport_as_the_legacy_rule_on_random_bubbles()
    {
        var rng = new Random(20260924);
        var unlimited = new AirportSearchOptions { MaxDistanceNauticalMiles = 1000, MaxResults = 1 };
        for (var bubble = 0; bubble < 50; bubble++)
        {
            var airports = Enumerable.Range(0, 300)
                .Select(i => Raw($"A{bubble:D2}{i:D3}", 43.66 + ((rng.NextDouble() - 0.5) * 3), 7.2 + ((rng.NextDouble() - 0.5) * 4)))
                .ToArray();
            for (var point = 0; point < 20; point++)
            {
                var lat = 43.66 + ((rng.NextDouble() - 0.5) * 2);
                var lon = 7.2 + ((rng.NextDouble() - 0.5) * 3);

                var fsgap = AirportSelection.Select(airports, new GeoPosition(lat, lon), unlimited)[0].Icao;

                Assert.Equal(LegacyNearest(lat, lon, airports), fsgap);
            }
        }
    }
}
