using System.Reflection;
using System.Text;
using System.Text.Json;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// BLOCK 10B.1: the production MSFS livery enumeration (<see cref="IInstalledLiveryService"/>): packet parsing, packet
/// assembly, mapping, and the service on the transport (one session, errors, sharing, lifecycle). No MSFS needed.
/// </summary>
public class InstalledLiveryServiceTests
{
    private const uint RequestId = 0x46534C01;

    // -- packet parsing (official layout + empirical details) -----------------------------------------------------------

    [Fact]
    public void Parses_the_official_layout_28_byte_header_then_512_byte_elements()
    {
        var parsed = LiveryListParser.Parse(Packet(RequestId, 3, 201, [("A220-300", "Air France A220-300"), ("C172", "Asobo")]));

        Assert.Equal(RequestId, parsed.RequestId);
        Assert.Equal(3u, parsed.EntryNumber);
        Assert.Equal(201u, parsed.OutOf);
        Assert.Equal([new RawLiveryEntry("A220-300", "Air France A220-300"), new RawLiveryEntry("C172", "Asobo")], parsed.Entries);
    }

    [Fact]
    public void Spare_slots_beyond_the_announced_count_are_ignored_as_observed_live()
    {
        // Live MSFS 2024: 40 988 bytes announcing 79 elements = 28 + 80 × 512.
        var parsed = LiveryListParser.Parse(Packet(RequestId, 0, 1, [("A", "B")], spareSlots: 1, spareFill: (byte)'X'));

        Assert.Equal([new RawLiveryEntry("A", "B")], parsed.Entries);
    }

    [Fact]
    public void Empty_fields_decode_as_empty_strings()
    {
        var parsed = LiveryListParser.Parse(Packet(RequestId, 0, 1, [("A220-300", string.Empty), (string.Empty, "Orphan")]));

        Assert.Equal([new RawLiveryEntry("A220-300", string.Empty), new RawLiveryEntry(string.Empty, "Orphan")], parsed.Entries);
    }

    [Fact]
    public void A_field_without_a_terminator_is_read_within_its_256_bytes_only()
    {
        var packet = Packet(RequestId, 0, 1, [("x", "y")]);
        packet.AsSpan(LiveryListParser.HeaderSize, 256).Fill((byte)'T');
        packet.AsSpan(LiveryListParser.HeaderSize + 256, 256).Fill((byte)'L');

        var entry = Assert.Single(LiveryListParser.Parse(packet).Entries);

        Assert.Equal(new string('T', 256), entry.AircraftTitle);
        Assert.Equal(new string('L', 256), entry.LiveryName);
    }

    [Fact]
    public void Fields_are_trimmed_and_utf8_decoded()
    {
        var entry = Assert.Single(LiveryListParser.Parse(Packet(RequestId, 0, 1, [("  Airbus A320 Neo  ", "Iberia Españа ")])).Entries);

        Assert.Equal("Airbus A320 Neo", entry.AircraftTitle);
        Assert.Equal("Iberia Españа", entry.LiveryName);
    }

    [Fact]
    public void A_packet_without_elements_is_valid()
    {
        Assert.Empty(LiveryListParser.Parse(Packet(RequestId, 0, 1, [])).Entries);
    }

    public static TheoryData<string, Func<byte[]>> MalformedPackets() => new()
    {
        { "shorter than the header", () => new byte[20] },
        { "truncated: declares more bytes than received", () => Packet(RequestId, 0, 1, [("A", "B")])[..^512] },
        { "declared size below the header", () => WithUInt(Packet(RequestId, 0, 1, []), 0, 12) },
        { "not a livery list message", () => WithUInt(Packet(RequestId, 0, 1, [("A", "B")]), 8, 18) },
        { "payload not a whole number of elements", () => WithSize(Packet(RequestId, 0, 1, [("A", "B")]), extraBytes: 100) },
        { "announces more elements than it holds", () => WithUInt(Packet(RequestId, 0, 1, [("A", "B")]), 16, 2) },
        { "absurd element count", () => WithUInt(Packet(RequestId, 0, 1, [("A", "B")]), 16, uint.MaxValue) },
        { "packet number beyond the packet count", () => WithUInt(Packet(RequestId, 0, 2, [("A", "B")]), 20, 2) },
        { "absurd packet count", () => WithUInt(Packet(RequestId, 0, 1, [("A", "B")]), 24, uint.MaxValue) },
    };

    [Theory]
    [MemberData(nameof(MalformedPackets))]
    public void Malformed_packets_are_rejected_never_read_out_of_bounds(string why, Func<byte[]> packet)
    {
        var error = Assert.Throws<FormatException>(() => LiveryListParser.Parse(packet()));

        Assert.False(string.IsNullOrEmpty(error.Message), why);
    }

    [Fact]
    public void A_packet_too_short_for_a_request_id_is_rejected_before_parsing()
    {
        Assert.Throws<FormatException>(() => LiveryListParser.RequestIdOf(new byte[10]));
    }

    // -- packet assembly --------------------------------------------------------------------------------------------------

    [Fact]
    public void One_packet_lists_are_complete_immediately()
    {
        var assembly = new LiveryListAssembly(RequestId);

        Assert.True(assembly.Accept(Packet(RequestId, 0, 1, [("A", "B")])));

        Assert.True(assembly.IsComplete);
        Assert.Equal([new RawLiveryEntry("A", "B")], assembly.ToList());
    }

    [Fact]
    public void A_zero_packet_count_answer_is_a_complete_empty_list()
    {
        var assembly = new LiveryListAssembly(RequestId);

        assembly.Accept(Packet(RequestId, 0, 0, []));

        Assert.True(assembly.IsComplete);
        Assert.Empty(assembly.ToList());
    }

    [Fact]
    public void Packets_arriving_out_of_order_complete_only_with_the_last_and_keep_packet_order()
    {
        var assembly = new LiveryListAssembly(RequestId);

        assembly.Accept(Packet(RequestId, 2, 3, [("T3", "L3")]));
        assembly.Accept(Packet(RequestId, 0, 3, [("T1", "L1")]));
        Assert.False(assembly.IsComplete);
        Assert.Throws<InvalidOperationException>(() => assembly.ToList());
        assembly.Accept(Packet(RequestId, 1, 3, [("T2", "L2")]));

        Assert.True(assembly.IsComplete);
        Assert.Equal(["T1", "T2", "T3"], assembly.ToList().Select(e => e.AircraftTitle));
    }

    [Fact]
    public void Packets_of_another_request_are_ignored()
    {
        var assembly = new LiveryListAssembly(RequestId);

        Assert.False(assembly.Accept(Packet(RequestId + 1, 0, 1, [("Other", "Request")])));

        Assert.False(assembly.IsComplete);
        Assert.Equal(0, assembly.PacketCount);
    }

    [Fact]
    public void Packets_disagreeing_on_the_packet_count_are_rejected()
    {
        var assembly = new LiveryListAssembly(RequestId);
        assembly.Accept(Packet(RequestId, 0, 3, [("A", "B")]));

        Assert.Throws<FormatException>(() => assembly.Accept(Packet(RequestId, 1, 4, [("C", "D")])));
    }

    [Fact]
    public void A_repeated_packet_number_is_rejected()
    {
        var assembly = new LiveryListAssembly(RequestId);
        assembly.Accept(Packet(RequestId, 0, 2, [("A", "B")]));

        Assert.Throws<FormatException>(() => assembly.Accept(Packet(RequestId, 0, 2, [("A", "B")])));
    }

    [Fact]
    public void The_live_a220_rows_mixed_with_other_aircraft_survive_a_multi_packet_answer()
    {
        // 24 A220 rows captured live (BLOCK 10A.5) + 200 unrelated rows, split 79 per packet as MSFS does.
        var a220 = LoadA220Rows();
        var rows = a220.Concat(Enumerable.Range(0, 200).Select(i => ($"Other aircraft {i}", $"Livery {i % 7}"))).ToArray();
        var packets = rows.Chunk(79).Select((chunk, i) => Packet(RequestId, (uint)i, (uint)Math.Ceiling(rows.Length / 79.0), chunk, spareSlots: 1)).ToArray();
        var assembly = new LiveryListAssembly(RequestId);

        foreach (var packet in packets.Reverse())
        {
            assembly.Accept(packet);
        }

        var list = assembly.ToList();
        Assert.Equal(3, packets.Length);
        Assert.Equal(rows.Length, list.Count);
        Assert.Equal(rows.Select(r => r.Item1), list.Select(e => e.AircraftTitle));
        Assert.Equal(24, list.Count(e => e.AircraftTitle is "A220-300" or "A220-300 - No Cabin"));
    }

    // -- mapping ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Mapping_keeps_order_and_duplicates_nulls_empty_livery_names_and_drops_untitled_rows()
    {
        RawLiveryEntry[] raw =
        [
            new("A220-300", "Air France A220-300"),
            new("A220-300 - No Cabin", "Air France A220-300"),
            new("A220-300", "Air France A220-300"),
            new("A220-300", string.Empty),
            new(string.Empty, "Nothing to name"),
            new("  ", "Nothing either"),
        ];

        var rows = InstalledLiveryMapper.ToInstalledLiveries(raw);

        Assert.Equal(
            [
                new InstalledLivery { AircraftTitle = "A220-300", LiveryName = "Air France A220-300" },
                new InstalledLivery { AircraftTitle = "A220-300 - No Cabin", LiveryName = "Air France A220-300" },
                new InstalledLivery { AircraftTitle = "A220-300", LiveryName = "Air France A220-300" },
                new InstalledLivery { AircraftTitle = "A220-300", LiveryName = null },
            ],
            rows);
    }

    [Fact]
    public void An_empty_answer_maps_to_an_empty_list()
    {
        Assert.Empty(InstalledLiveryMapper.ToInstalledLiveries([]));
    }

    // -- the service on the transport -------------------------------------------------------------------------------------

    private static async Task<(Harness H, FakeSession Session)> ConnectedAsync(bool pollTelemetry = false)
    {
        var h = new Harness(pollTelemetry: pollTelemetry);
        var session = h.Factory.SimulatorPresent(Identity.Of("A220-300 - No Cabin"));
        session.Liveries = [new("A220-300", "Air France A220-300"), new("A220-300 - No Cabin", "Air France A220-300"), new("C172", string.Empty)];
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        return (h, session);
    }

    [Fact]
    public async Task The_simulator_is_an_installed_livery_service_answering_from_its_one_session()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        IInstalledLiveryService service = h.Simulator;

        var rows = await service.GetInstalledAircraftLiveriesAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal(["A220-300", "A220-300 - No Cabin", "C172"], rows.Select(r => r.AircraftTitle));
        Assert.Null(rows[2].LiveryName);
        Assert.Equal(1, session.LiveryRequests);
        Assert.Single(h.Factory.Sessions);
        Assert.Equal(1, h.Factory.MaxLiveSessions);
    }

    [Fact]
    public async Task Each_call_asks_the_simulator_again_nothing_is_cached()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;

        await h.Simulator.GetInstalledAircraftLiveriesAsync();
        session.Liveries = [];
        var second = await h.Simulator.GetInstalledAircraftLiveriesAsync();

        Assert.Empty(second);
        Assert.Equal(2, session.LiveryRequests);
    }

    [Fact]
    public async Task Without_a_connection_the_simulator_is_reported_unavailable()
    {
        await using var h = new Harness();

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => h.Simulator.GetInstalledAircraftLiveriesAsync());

        Assert.Equal(SimulatorServiceError.SimulatorUnavailable, error.Error);
        Assert.Equal(0, h.Factory.Attempts);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task Timeouts_malformed_answers_and_rejections_are_query_failures(Type exceptionType)
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.LiveryFailure = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => h.Simulator.GetInstalledAircraftLiveriesAsync());
        session.LiveryFailure = null;

        Assert.Equal(SimulatorServiceError.QueryFailed, error.Error);
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, (await h.Simulator.GetInstalledAircraftLiveriesAsync()).Count);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_native_request()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.LiveryGate = new TaskCompletionSource();

        var calls = Enumerable.Range(0, 6).Select(_ => h.Simulator.GetInstalledAircraftLiveriesAsync()).ToArray();
        await Eventually.TrueAsync(() => session.LiveryRequests == 1, "the shared request");
        session.LiveryGate.SetResult();
        var results = await Task.WhenAll(calls).WaitAsync(Eventually.Timeout);

        Assert.All(results, r => Assert.Equal(3, r.Count));
        Assert.Equal(1, session.LiveryRequests);
    }

    [Fact]
    public async Task A_caller_cancelling_does_not_cancel_the_shared_request_for_the_others()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.LiveryGate = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        var cancelled = h.Simulator.GetInstalledAircraftLiveriesAsync(cts.Token);
        var other = h.Simulator.GetInstalledAircraftLiveriesAsync();
        await Eventually.TrueAsync(() => session.LiveryRequests == 1, "the shared request");
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Eventually.Timeout));
        session.LiveryGate.SetResult();

        Assert.Equal(3, (await other.WaitAsync(Eventually.Timeout)).Count);
    }

    [Fact]
    public async Task A_connection_loss_during_the_request_is_reported_unavailable()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.LiveryGate = new TaskCompletionSource();

        var call = h.Simulator.GetInstalledAircraftLiveriesAsync();
        await Eventually.TrueAsync(() => session.LiveryRequests == 1, "the request");
        session.Drop();

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => call.WaitAsync(Eventually.Timeout));
        Assert.Equal(SimulatorServiceError.SimulatorUnavailable, error.Error);
    }

    [Fact]
    public async Task After_a_reconnection_the_new_session_answers()
    {
        var h = new Harness();
        await using var _ = h;
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        var second = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        first.Liveries = [new("First", "L")];
        second.Liveries = [new("Second", "L")];
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.Equal("First", (await h.Simulator.GetInstalledAircraftLiveriesAsync())[0].AircraftTitle);
        var timers = h.Clock.TimersCreated;

        first.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        await Assert.ThrowsAsync<SimulatorServiceException>(() => h.Simulator.GetInstalledAircraftLiveriesAsync());
        await h.ElapseRetryAsync(timers);
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        Assert.Equal("Second", (await h.Simulator.GetInstalledAircraftLiveriesAsync())[0].AircraftTitle);
        Assert.Equal(1, h.Factory.MaxLiveSessions);
    }

    [Fact]
    public async Task Stop_during_the_request_reports_unavailable_and_dispose_refuses_new_requests()
    {
        var (h, session) = await ConnectedAsync();
        session.LiveryGate = new TaskCompletionSource();

        var call = h.Simulator.GetInstalledAircraftLiveriesAsync();
        await Eventually.TrueAsync(() => session.LiveryRequests == 1, "the request");
        await h.Simulator.StopAsync();

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => call.WaitAsync(Eventually.Timeout));
        Assert.Equal(SimulatorServiceError.SimulatorUnavailable, error.Error);
        await h.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Simulator.GetInstalledAircraftLiveriesAsync());
    }

    [Fact]
    public async Task Telemetry_keeps_flowing_on_the_same_session_while_an_enumeration_is_pending()
    {
        var (h, session) = await ConnectedAsync(pollTelemetry: true);
        await using var _ = h;
        session.SetGroup(new FastGroupVars { IndicatedAirspeedKnots = 250 });
        session.LiveryGate = new TaskCompletionSource();

        var call = h.Simulator.GetInstalledAircraftLiveriesAsync();
        await Eventually.TrueAsync(
            () => h.Simulator.Telemetry.GetSnapshotAsync().GetAwaiter().GetResult().Flight.IndicatedAirspeedKnots.IsKnown || AdvanceOnce(h),
            "telemetry while the enumeration is pending");

        Assert.False(call.IsCompleted);
        session.LiveryGate.SetResult();
        Assert.Equal(3, (await call.WaitAsync(Eventually.Timeout)).Count);
        Assert.Single(h.Factory.Sessions);
    }

    // -- architecture -------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_generic_row_carries_exactly_what_msfs_enumerates()
    {
        Assert.Equal(
            ["AircraftTitle", "LiveryName"],
            typeof(InstalledLivery).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Order());
        Assert.Equal(
            [nameof(IInstalledLiveryService.GetInstalledAircraftLiveriesAsync)],
            typeof(IInstalledLiveryService).GetMethods().Select(m => m.Name));
    }

    [Fact]
    public void No_ai_object_api_is_public_on_the_transport()
    {
        var publicNames = typeof(SimConnectSimulator).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(m => m.Name);

        Assert.DoesNotContain(publicNames, n => n.Contains("Probe", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Experimental", StringComparison.OrdinalIgnoreCase)
            || n.Contains("AICreate", StringComparison.OrdinalIgnoreCase)
            || n.Contains("RemoveAi", StringComparison.OrdinalIgnoreCase));
        Assert.True(typeof(IInstalledLiveryService).IsAssignableFrom(typeof(SimConnectSimulator)));
    }

    // -- helpers ------------------------------------------------------------------------------------------------------------

    private static bool AdvanceOnce(Harness h)
    {
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        Thread.Sleep(10);
        return false;
    }

    private static (string, string)[] LoadA220Rows()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synaptic", "a220-livery-enumeration.json")));
        return json.RootElement.GetProperty("a220Rows").EnumerateArray()
            .Select(r => (r.GetProperty("AircraftTitle").GetString()!, r.GetProperty("LiveryName").GetString()!))
            .ToArray();
    }

    private static byte[] Packet(uint requestId, uint entryNumber, uint outOf, IReadOnlyList<(string Title, string Livery)> entries, int spareSlots = 0, byte spareFill = 0)
    {
        var buffer = new byte[LiveryListParser.HeaderSize + ((entries.Count + spareSlots) * LiveryListParser.EntrySize)];
        BitConverter.GetBytes((uint)buffer.Length).CopyTo(buffer, 0);
        BitConverter.GetBytes(6u).CopyTo(buffer, 4);
        BitConverter.GetBytes(LiveryListParser.MessageId).CopyTo(buffer, 8);
        BitConverter.GetBytes(requestId).CopyTo(buffer, 12);
        BitConverter.GetBytes((uint)entries.Count).CopyTo(buffer, 16);
        BitConverter.GetBytes(entryNumber).CopyTo(buffer, 20);
        BitConverter.GetBytes(outOf).CopyTo(buffer, 24);
        for (var i = 0; i < entries.Count; i++)
        {
            var offset = LiveryListParser.HeaderSize + (i * LiveryListParser.EntrySize);
            Encoding.UTF8.GetBytes(entries[i].Title).CopyTo(buffer, offset);
            Encoding.UTF8.GetBytes(entries[i].Livery).CopyTo(buffer, offset + LiveryListParser.StringFieldSize);
        }

        buffer.AsSpan(buffer.Length - (spareSlots * LiveryListParser.EntrySize)).Fill(spareFill);
        return buffer;
    }

    private static byte[] WithUInt(byte[] packet, int offset, uint value)
    {
        BitConverter.GetBytes(value).CopyTo(packet, offset);
        return packet;
    }

    private static byte[] WithSize(byte[] packet, int extraBytes)
    {
        var grown = new byte[packet.Length + extraBytes];
        packet.CopyTo(grown, 0);
        BitConverter.GetBytes((uint)grown.Length).CopyTo(grown, 0);
        return grown;
    }
}
