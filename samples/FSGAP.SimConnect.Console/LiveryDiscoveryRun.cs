// BLOCK 10A.5 — EXPERIMENTAL live driver of the livery / registration discovery (docs/audits/synaptic-a220-livery-discovery.md).
//
// Everything goes through the sample's one SimConnectSimulator (one native connection), using its internal, experimental
// entry points. Sequence: enumerate every aircraft livery; keep the Synaptic A220 rows; probe a few liveries one at a
// time as non-ATC AI aircraft (empty tail number first, then one control with a synthetic tail), each created, read by
// its own object id and removed before the next; write sanitized JSON to the output directory; report the ledger.
// Never writes to the user aircraft. Never keeps an object alive on purpose.
using System.Diagnostics;
using System.Text.Json;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Console;

internal static class LiveryDiscoveryRun
{
    /// <summary>Synthetic, obviously fake tail number of the single control probe. Never persisted.</summary>
    internal const string ControlTailNumber = "FSGAP01";

    /// <summary>Hard upper bound on probes per run (§19).</summary>
    internal const int MaxProbes = 10;

    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    internal static async Task RunAsync(SimConnectSimulator simulator, string outputDirectory, int probeCount, bool preferCabin, Action<string, string> print, CancellationToken cancellationToken)
    {
        probeCount = Math.Clamp(probeCount, 0, MaxProbes);
        var ledger = new AiObjectLedger();
        try
        {
            await WaitUntilReadyAsync(simulator, print, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(outputDirectory);

            // -- Phase A: enumerate everything -----------------------------------------------------------------------
            var memoryBefore = SimulatorWorkingSetMb();
            var enumeration = await simulator.ExperimentalEnumerateAircraftLiveriesAsync(cancellationToken).ConfigureAwait(false);
            var rows = enumeration.Entries.Select(e => (e.AircraftTitle, e.LiveryName)).ToArray();
            var summary = LiveryDiscoveryAnalysis.Summarize(rows);
            print("livery", $"enumeration: {summary.Rows} rows, {summary.UniqueTitles} titles, {summary.UniqueLiveries} livery names, {enumeration.Packets} packets, header {enumeration.HeaderSize} B, entry {enumeration.EntrySize} B, {enumeration.Elapsed.TotalMilliseconds:0} ms");
            print("livery", $"first packet header: {enumeration.FirstPacketHeaderHex}");
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "enumeration-all.json"),
                JsonSerializer.Serialize(new { summary, enumeration.Packets, enumeration.HeaderSize, enumeration.EntrySize, ElapsedMs = enumeration.Elapsed.TotalMilliseconds, Rows = rows.Select(r => new { Title = r.AircraftTitle, Livery = r.LiveryName }) }, Json),
                cancellationToken).ConfigureAwait(false);

            // -- Synaptic A220 rows (exact titles only) ---------------------------------------------------------------
            var a220 = rows.Where(r => LiveryDiscoveryAnalysis.IsSynapticA220(r.AircraftTitle)).ToArray();
            var groups = LiveryDiscoveryAnalysis.GroupA220Liveries(a220);
            print("livery", $"Synaptic A220 rows: {a220.Length} ({a220.Count(r => r.AircraftTitle == "A220-300")} cabin, {a220.Count(r => r.AircraftTitle == "A220-300 - No Cabin")} no-cabin), {groups.Count} distinct livery names");
            foreach (var row in a220.OrderBy(r => r.LiveryName, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.AircraftTitle, StringComparer.Ordinal))
            {
                print("livery", $"  A220 | {row.AircraftTitle} | {row.LiveryName}");
            }

            var nearA220 = rows.Where(r => !LiveryDiscoveryAnalysis.IsSynapticA220(r.AircraftTitle) && (r.AircraftTitle.Contains("A220", StringComparison.OrdinalIgnoreCase) || r.AircraftTitle.Contains("A223", StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.AircraftTitle).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            print("livery", $"other titles containing A220/A223 (not Synaptic by the exact rule): {(nearA220.Length == 0 ? "none" : string.Join(" | ", nearA220))}");

            // -- Phase B: probes, one at a time ------------------------------------------------------------------------
            var probes = new List<object>();
            var targets = LiveryDiscoveryAnalysis.ChooseProbeTargets(groups, probeCount, preferCabin);
            var position = await ProbePositionAsync(simulator, print, cancellationToken).ConfigureAwait(false);
            var userAtcId = simulator.AircraftDetector.Current?.Registration;
            if (position is null && targets.Count > 0)
            {
                print("livery", "no usable user position: probes skipped");
                targets = [];
            }

            AiProbeResult? firstSuccess = null;
            foreach (var (title, livery) in targets)
            {
                var result = await ProbeAsync(simulator, title, livery, string.Empty, position!, ledger, probes, userAtcId, print, cancellationToken).ConfigureAwait(false);
                if (firstSuccess is null && result.Identity is not null)
                {
                    firstSuccess = result;
                }
            }

            // -- Control: explicit synthetic tail on one livery ---------------------------------------------------------
            if (firstSuccess is not null)
            {
                await ProbeAsync(simulator, firstSuccess.ContainerTitle, firstSuccess.Livery, ControlTailNumber, position!, ledger, probes, userAtcId, print, cancellationToken).ConfigureAwait(false);
            }

            var memoryAfter = SimulatorWorkingSetMb();
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "a220-livery-enumeration.json"),
                JsonSerializer.Serialize(new
                {
                    _comment = "BLOCK 10A.5 live capture: SimConnect_EnumerateSimObjectsAndLiveries (aircraft) filtered to the two Synaptic A220 preset titles. Titles and livery names exactly as MSFS returned them.",
                    totals = summary,
                    a220Rows = a220.Select(r => new { r.AircraftTitle, r.LiveryName }),
                    a220Groups = groups,
                    otherTitlesContainingA220 = nearA220,
                    layout = new { enumeration.Packets, enumeration.HeaderSize, enumeration.EntrySize, ElapsedMs = Math.Round(enumeration.Elapsed.TotalMilliseconds) },
                }, Json),
                cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(outputDirectory, "a220-livery-probes.json"),
                JsonSerializer.Serialize(new
                {
                    _comment = "BLOCK 10A.5 live capture: non-ATC AI aircraft created with SimConnect_AICreateNonATCAircraft_EX1 (title, livery, tail), identity read from the object's own id, then removed with SimConnect_AIRemoveObject.",
                    userAircraftAtcIdAtStart = userAtcId,
                    simulatorWorkingSetMb = new { before = memoryBefore, after = memoryAfter },
                    ledger = new { ledger.Created, ledger.Removed, Remaining = ledger.Remaining },
                    probes,
                }, Json),
                cancellationToken).ConfigureAwait(false);
            print("livery", $"simulator working set: {memoryBefore} MB before, {memoryAfter} MB after");
        }
        catch (OperationCanceledException)
        {
            print("livery", "cancelled");
        }
        catch (Exception ex)
        {
            print("livery", $"discovery failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            await CleanupAsync(simulator, ledger, print).ConfigureAwait(false);
        }
    }

    /// <summary>Explicit cleanup path (§31): removes the given object ids, e.g. those a crashed run reported.</summary>
    internal static async Task CleanupIdsAsync(SimConnectSimulator simulator, IEnumerable<uint> ids, Action<string, string> print, CancellationToken cancellationToken)
    {
        await WaitUntilReadyAsync(simulator, print, cancellationToken).ConfigureAwait(false);
        var ledger = new AiObjectLedger();
        foreach (var id in ids)
        {
            ledger.RecordCreated(id);
        }

        await CleanupAsync(simulator, ledger, print).ConfigureAwait(false);
    }

    private static async Task<AiProbeResult> ProbeAsync(
        SimConnectSimulator simulator,
        string title,
        string livery,
        string tail,
        AiProbePosition position,
        AiObjectLedger ledger,
        List<object> probes,
        string? userAtcId,
        Action<string, string> print,
        CancellationToken cancellationToken)
    {
        var memoryBefore = SimulatorWorkingSetMb();
        var clock = Stopwatch.StartNew();
        var r = await simulator.ExperimentalProbeAiAircraftAsync(title, livery, tail, position, Settle, ledger, cancellationToken).ConfigureAwait(false);
        var total = clock.Elapsed;
        var memoryAfter = SimulatorWorkingSetMb();
        var folderCandidate = LiveryDiscoveryAnalysis.ParseRegistration(r.Identity?.LiveryFolder);
        var outcome = tail.Length == 0
            ? LiveryDiscoveryAnalysis.ClassifyEmptyTail(r.CreateHResult == 0 && r.ObjectId is not null, r.Identity is not null, r.Identity?.AtcId, folderCandidate.Registration, userAtcId)
            : (EmptyTailOutcome?)null;
        print("probe", $"'{title}' / '{livery}' tail='{tail}' -> object {(r.ObjectId?.ToString() ?? "none")} (hr 0x{r.CreateHResult:X8}), create {r.CreateLatency.TotalMilliseconds:0} ms, read {r.ReadLatency.TotalMilliseconds:0} ms, remove hr {(r.RemoveHResult is { } h ? $"0x{h:X8}" : "n/a")} confirmed={r.RemovalConfirmed} ({r.RemoveLatency.TotalMilliseconds:0} ms)");
        print("probe", $"   object: TITLE='{r.Identity?.Title}' LIVERY NAME='{r.Identity?.LiveryName}' LIVERY FOLDER='{r.Identity?.LiveryFolder}' ATC ID='{r.Identity?.AtcId}' ATC AIRLINE='{r.Identity?.AtcAirline}' ATC MODEL='{r.Identity?.AtcModel}' ATC TYPE='{r.Identity?.AtcType}'");
        print("probe", $"   user aircraft read in the same cycle: TITLE='{r.UserAircraftTitle}' (object 0); folder candidate {folderCandidate.Registration ?? "none"} [{folderCandidate.Confidence}]{(outcome is { } o ? $"; empty-tail outcome {o}" : string.Empty)}{(r.Error is null ? string.Empty : $"; ERROR {r.Error}")}{(r.Exceptions.Count == 0 ? string.Empty : $"; simconnect: {string.Join(", ", r.Exceptions)}")}");
        probes.Add(new
        {
            requested = new { r.ContainerTitle, r.Livery, TailNumber = r.TailNumber },
            r.CreateHResult,
            assignedObjectId = r.ObjectId,
            queriedObjectIsNotUser = r.ObjectId is > 0,
            observed = r.Identity,
            userAircraftTitleSameCycle = r.UserAircraftTitle,
            folderRegistrationCandidate = folderCandidate,
            emptyTailOutcome = outcome?.ToString(),
            createLatencyMs = Math.Round(r.CreateLatency.TotalMilliseconds),
            readLatencyMs = Math.Round(r.ReadLatency.TotalMilliseconds),
            removeLatencyMs = Math.Round(r.RemoveLatency.TotalMilliseconds),
            totalMs = Math.Round(total.TotalMilliseconds),
            r.RemoveHResult,
            r.RemovalConfirmed,
            r.Exceptions,
            r.Error,
            simulatorWorkingSetMb = new { before = memoryBefore, after = memoryAfter },
        });
        return r;
    }

    private static async Task CleanupAsync(SimConnectSimulator simulator, AiObjectLedger ledger, Action<string, string> print)
    {
        foreach (var id in ledger.Remaining)
        {
            try
            {
                var (hr, confirmed, _) = await simulator.ExperimentalRemoveAiObjectAsync(id, ledger).ConfigureAwait(false);
                print("livery", $"cleanup: object {id} remove hr 0x{hr:X8}, confirmed={confirmed}");
            }
            catch (Exception ex)
            {
                print("livery", $"cleanup: object {id} could not be removed ({ex.Message}); rerun with --livery-cleanup {id}");
            }
        }

        print("livery", $"LEDGER created={ledger.Created} removed={ledger.Removed} remaining={ledger.Remaining.Count}{(ledger.Remaining.Count == 0 ? string.Empty : " ids " + string.Join(",", ledger.Remaining))}");
    }

    private static async Task WaitUntilReadyAsync(SimConnectSimulator simulator, Action<string, string> print, CancellationToken cancellationToken)
    {
        print("livery", "waiting for the simulator connection and a loaded aircraft");
        while (simulator.Status.State != SimulatorConnectionState.Connected || simulator.AircraftDetector.Current is null)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Where to put probes: about 330 m north of the user aircraft, on the ground, when the user is parked; about 3.3 km
    /// north, on the ground, when the user is moving or airborne. Never on top of the user aircraft.
    /// </summary>
    private static async Task<AiProbePosition?> ProbePositionAsync(SimConnectSimulator simulator, Action<string, string> print, CancellationToken cancellationToken)
    {
        var flight = (await simulator.Telemetry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false)).Flight;
        if (!flight.LatitudeDegrees.TryGetValue(out var lat) || !flight.LongitudeDegrees.TryGetValue(out var lon))
        {
            return null;
        }

        var parked = flight.OnGround.GetValueOrDefault(false) && flight.GroundSpeedKnots.GetValueOrDefault(99) < 2;
        var position = new AiProbePosition(
            lat + (parked ? 0.003 : 0.03),
            lon,
            parked ? flight.AltitudeFeet.GetValueOrDefault(0) : 0,
            flight.HeadingMagneticDegrees.GetValueOrDefault(0),
            OnGround: true);
        print("livery", $"probe position: {(parked ? "~330 m" : "~3.3 km")} north of the user aircraft, on the ground (user {(parked ? "parked" : "moving or airborne")})");
        return position;
    }

    private static long SimulatorWorkingSetMb()
    {
        try
        {
            return Process.GetProcessesByName("FlightSimulator2024").Select(p => p.WorkingSet64).DefaultIfEmpty(0).Sum() / (1024 * 1024);
        }
        catch (Exception)
        {
            return -1;
        }
    }
}
