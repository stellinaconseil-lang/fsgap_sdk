using System.Text;
using System.Text.Json;
using FSGAP.SimConnect.Console;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// BLOCK 10A.5 (experimental livery / registration discovery): packet parsing, ledger bookkeeping and the sample's
/// audit-only analysis helpers. None of this needs MSFS; the live runs are manual (docs/audits/synaptic-a220-livery-discovery.md).
/// </summary>
public class LiveryDiscoveryTests
{
    private static readonly string FixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "synaptic");

    // -- enumeration packet parsing -------------------------------------------------------------------------------------

    [Fact]
    public void Parses_the_live_layout_28_byte_header_and_one_spare_512_byte_slot()
    {
        // Live MSFS 2024: 40 988 bytes announcing 79 entries = 28 + 80 × 512.
        var packet = Packet(requestId: 7, entryNumber: 3, outOf: 201, [("A220-300", "Air France A220-300"), ("A220-300 - No Cabin", "Delta A220-300")], spareSlots: 1);

        var parsed = LiveryListParser.Parse(packet);

        Assert.Equal(7u, parsed.RequestId);
        Assert.Equal(3u, parsed.EntryNumber);
        Assert.Equal(201u, parsed.OutOf);
        Assert.Equal(28, parsed.HeaderSize);
        Assert.Equal(512, parsed.EntrySize);
        Assert.Equal([new RawLiveryEntry("A220-300", "Air France A220-300"), new RawLiveryEntry("A220-300 - No Cabin", "Delta A220-300")], parsed.Entries);
    }

    [Fact]
    public void Parses_an_exact_packet_and_keeps_empty_livery_names()
    {
        var parsed = LiveryListParser.Parse(Packet(1, 0, 1, [("A220-300", string.Empty)], spareSlots: 0));

        Assert.Equal(new RawLiveryEntry("A220-300", string.Empty), Assert.Single(parsed.Entries));
    }

    [Fact]
    public void Request_id_is_read_before_parsing_to_filter_foreign_answers()
    {
        Assert.Equal(0x46534801u, LiveryListParser.RequestIdOf(Packet(0x46534801, 0, 1, [("T", "L")], 0)));
    }

    [Fact]
    public void Truncated_or_misaligned_packets_are_rejected_not_guessed()
    {
        Assert.Throws<FormatException>(() => LiveryListParser.Parse(new byte[20]));
        var misaligned = Packet(1, 0, 1, [("A", "B"), ("C", "D")], 0)[..^100];
        Assert.Throws<FormatException>(() => LiveryListParser.Parse(misaligned));
    }

    [Fact]
    public void An_empty_packet_has_no_entries()
    {
        Assert.Empty(LiveryListParser.Parse(Packet(1, 0, 1, [], 0)).Entries);
    }

    // -- ledger (cleanup bookkeeping) ----------------------------------------------------------------------------------

    [Fact]
    public void Ledger_only_clears_confirmed_removals_and_ignores_repeats()
    {
        var ledger = new AiObjectLedger();
        ledger.RecordCreated(10);
        ledger.RecordCreated(11);
        ledger.RecordCreated(11);
        ledger.RecordRemoved(10);
        ledger.RecordRemoved(10);
        ledger.RecordRemoved(99);

        Assert.Equal(2, ledger.Created);
        Assert.Equal(1, ledger.Removed);
        Assert.Equal([11u], ledger.Remaining);
    }

    // -- A220 filtering and duplicates ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("A220-300", true)]
    [InlineData(" a220-300 - no cabin ", true)]
    [InlineData("Asobo PassiveAircraft A220-300", false)]
    [InlineData("A220-300 EA", false)]
    [InlineData("FenixA321 CFM WF SC", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_two_exact_preset_titles_are_synaptic_a220(string? title, bool expected)
    {
        Assert.Equal(expected, LiveryDiscoveryAnalysis.IsSynapticA220(title));
    }

    [Fact]
    public void A_livery_offered_under_both_presets_is_one_group_with_two_titles()
    {
        (string, string)[] rows =
        [
            ("A220-300", "Air France A220-300"),
            ("A220-300 - No Cabin", "Air France A220-300"),
            ("A220-300", "Delta A220-300"),
            ("A220-300", "Delta A220-300"),
            ("A220-300", string.Empty),
            ("Asobo PassiveAircraft A220-300", "Air France A220-300"),
        ];

        var groups = LiveryDiscoveryAnalysis.GroupA220Liveries(rows);

        Assert.Equal(["Air France A220-300", "Delta A220-300"], groups.Select(g => g.LiveryName));
        Assert.Equal(["A220-300", "A220-300 - No Cabin"], groups[0].Titles);
        Assert.Equal(1, groups[1].ExactDuplicateRows);
    }

    [Fact]
    public void Probe_targets_prefer_the_representative_liveries_and_the_requested_preset()
    {
        var groups = LiveryDiscoveryAnalysis.GroupA220Liveries(
        [
            ("A220-300", "Swiss A220-300"), ("A220-300 - No Cabin", "Swiss A220-300"),
            ("A220-300", "Delta A220-300"), ("A220-300 - No Cabin", "Delta A220-300"),
            ("A220-300", "Air France A220-300"), ("A220-300 - No Cabin", "Air France A220-300"),
        ]);

        Assert.Equal(
            [("A220-300 - No Cabin", "Air France A220-300"), ("A220-300 - No Cabin", "Delta A220-300")],
            LiveryDiscoveryAnalysis.ChooseProbeTargets(groups, 2));
        Assert.Equal("A220-300", LiveryDiscoveryAnalysis.ChooseProbeTargets(groups, 1, preferCabin: true)[0].Title);
        Assert.Empty(LiveryDiscoveryAnalysis.ChooseProbeTargets(groups, 0));
    }

    // -- registration candidates -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("AIR FRANCE F-HZUF", "F-HZUF", "High")]
    [InlineData("AIR BALTIC YL-CSM", "YL-CSM", "High")]
    [InlineData("DELTA N324DU", "N324DU", "High")]
    [InlineData("SWISS HB-JCO", "HB-JCO", "High")]
    [InlineData("ITA AIRWAYS EI-HHU", "EI-HHU", "High")]
    [InlineData("KOREAN AIR HL8315", "HL8315", "High")]
    [InlineData("AIR CANADA G-GUAC", "G-GUAC", "High")]
    [InlineData("ACME QQ-ABC", "QQ-ABC", "Low")]
    [InlineData("TWO F-HZUF F-HZUG", "F-HZUF", "Low")]
    [InlineData("A_BCS3_SYN_HOUSE", null, "None")]
    [InlineData("A220-300", null, "None")]
    [InlineData("", null, "None")]
    [InlineData(null, null, "None")]
    public void Folder_registrations_are_derived_candidates_with_a_confidence(string? folder, string? expected, string confidenceName)
    {
        var confidence = Enum.Parse<ParserConfidence>(confidenceName);
        var candidate = LiveryDiscoveryAnalysis.ParseRegistration(folder);

        Assert.Equal(expected, candidate.Registration);
        Assert.Equal(confidence, candidate.Confidence);
        Assert.Equal(expected is null ? RegistrationSource.None : RegistrationSource.Derived, candidate.Source);
        Assert.NotEqual(RegistrationSource.Authoritative, candidate.Source);
    }

    [Theory]
    [InlineData(false, false, null, null, null, "CreationRejected")]
    [InlineData(true, false, null, null, null, "Other")]
    [InlineData(true, true, "", "F-HZUF", null, "Blank")]
    [InlineData(true, true, "FHZUF", "F-HZUF", null, "LiveryRegistration")]
    [InlineData(true, true, "C-FFCO", "F-HZUF", "c ffco", "InheritedStale")]
    [InlineData(true, true, "ASXGS", "F-HZUF", "C-FFCO", "GeneratedOrDefault")]
    public void Empty_tail_outcomes_are_classified(bool created, bool read, string? atcId, string? folderCandidate, string? userAtcId, string expectedName)
    {
        Assert.Equal(Enum.Parse<EmptyTailOutcome>(expectedName), LiveryDiscoveryAnalysis.ClassifyEmptyTail(created, read, atcId, folderCandidate, userAtcId));
    }

    // -- live fixtures (golden) --------------------------------------------------------------------------------------------

    [Fact]
    public void Live_enumeration_fixture_lists_eleven_liveries_under_both_presets()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "a220-livery-enumeration.json")));
        var rows = json.RootElement.GetProperty("a220Rows").EnumerateArray()
            .Select(r => (r.GetProperty("AircraftTitle").GetString()!, r.GetProperty("LiveryName").GetString()!))
            .ToArray();

        var groups = LiveryDiscoveryAnalysis.GroupA220Liveries(rows);

        Assert.Equal(24, rows.Length);
        Assert.Equal(11, groups.Count);
        Assert.All(groups, g => Assert.Equal(["A220-300", "A220-300 - No Cabin"], g.Titles));
    }

    [Fact]
    public void Live_probe_fixture_shows_blank_atc_id_for_empty_tails_and_nothing_left_behind()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDirectory, "a220-livery-probes.json")));
        var probes = json.RootElement.GetProperty("probes").EnumerateArray().ToArray();

        Assert.All(probes, p => Assert.True(p.GetProperty("queriedObjectIsNotUser").GetBoolean()));
        Assert.All(probes, p => Assert.True(p.GetProperty("RemovalConfirmed").GetBoolean()));
        Assert.All(
            probes.Where(p => p.GetProperty("requested").GetProperty("TailNumber").GetString() == string.Empty),
            p => Assert.Equal("Blank", p.GetProperty("emptyTailOutcome").GetString()));
        Assert.All(json.RootElement.GetProperty("runs").EnumerateArray(), r => Assert.Empty(r.GetProperty("ledger").GetProperty("Remaining").EnumerateArray()));
    }

    private static byte[] Packet(uint requestId, uint entryNumber, uint outOf, IReadOnlyList<(string Title, string Livery)> entries, int spareSlots)
    {
        var buffer = new byte[LiveryListParser.DocumentedHeaderSize + ((entries.Count + spareSlots) * LiveryListParser.DocumentedEntrySize)];
        BitConverter.GetBytes((uint)buffer.Length).CopyTo(buffer, 0);
        BitConverter.GetBytes(6u).CopyTo(buffer, 4);
        BitConverter.GetBytes(38u).CopyTo(buffer, 8);
        BitConverter.GetBytes(requestId).CopyTo(buffer, 12);
        BitConverter.GetBytes((uint)entries.Count).CopyTo(buffer, 16);
        BitConverter.GetBytes(entryNumber).CopyTo(buffer, 20);
        BitConverter.GetBytes(outOf).CopyTo(buffer, 24);
        for (var i = 0; i < entries.Count; i++)
        {
            var offset = LiveryListParser.DocumentedHeaderSize + (i * LiveryListParser.DocumentedEntrySize);
            Encoding.UTF8.GetBytes(entries[i].Title).CopyTo(buffer, offset);
            Encoding.UTF8.GetBytes(entries[i].Livery).CopyTo(buffer, offset + 256);
        }

        return buffer;
    }
}
