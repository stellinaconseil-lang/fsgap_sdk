// BLOCK 10C.3 — RESEARCH ONLY: live qualification of reviewed Synaptic A220 controlled-degradation candidates
// (SynapticDegradationCatalog.cs). One SimConnectSimulator, one native connection. The pilot sits in the cockpit; this
// harness samples flight / engine / generic system / Synaptic overlay telemetry at 4 Hz and, on an explicit, confirmed
// command, writes exactly one whitelisted L:var to its explicit applied or restore value (set semantics, never a toggle),
// then reads it back. Never an arbitrary name, never a simulation variable, never an event. Not a failure provider.
//
// Commands (stdin): list, baseline <ID>, cancel <ID>, apply <ID> (+YES), restore <ID> (+YES), verified <ID>, halt <reason>,
// note <text>, result <ID> <RESULT> [text], status, quit.
using System.Globalization;
using System.Text;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Sessions;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Console;

internal static class SynapticDegradationProbe
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadbackDelay = TimeSpan.FromMilliseconds(1500);
    private const string LocalUnit = "number";

    private sealed record Column(string Name, string Unit, string Id);

    private sealed class Group(string name, Column[] columns)
    {
        public string Name { get; } = name;

        public Column[] Columns { get; } = columns;

        public HashSet<string> Excluded { get; } = [];

        public double?[] Last { get; set; } = new double?[columns.Length];
    }

    private static Column C(string name, string unit, string id) => new(name, unit, id);

    private static Column L(string name) => new("L:A22X " + name, LocalUnit, "a22x_" + name.ToLowerInvariant().Replace(' ', '_'));

    private static Group[] Groups() =>
    [
        new("flight",
        [
            C("SIM ON GROUND", "Bool", "on_ground"), C("GROUND VELOCITY", "Knots", "gs_kt"), C("AIRSPEED INDICATED", "Knots", "ias_kt"),
            C("INDICATED ALTITUDE", "Feet", "ind_alt_ft"), C("PLANE ALTITUDE", "Feet", "geo_alt_ft"), C("PLANE HEADING DEGREES TRUE", "Degrees", "hdg_true"),
            C("BRAKE PARKING POSITION", "Bool", "park_pos"),
        ]),
        new("engines",
        [
            C("TURB ENG N1:1", "Percent", "n1_1"), C("TURB ENG N1:2", "Percent", "n1_2"), C("TURB ENG N2:1", "Percent", "n2_1"), C("TURB ENG N2:2", "Percent", "n2_2"),
            C("GENERAL ENG EXHAUST GAS TEMPERATURE:1", "Celsius", "egt_1"), C("GENERAL ENG EXHAUST GAS TEMPERATURE:2", "Celsius", "egt_2"),
            C("GENERAL ENG THROTTLE LEVER POSITION:1", "Percent", "tla_1"), C("GENERAL ENG THROTTLE LEVER POSITION:2", "Percent", "tla_2"),
        ]),
        new("generic-systems",
        [
            C("ENG HYDRAULIC PRESSURE:1", "psi", "eng_hyd_1_psi"), C("ENG HYDRAULIC PRESSURE:2", "psi", "eng_hyd_2_psi"), C("HYDRAULIC PRESSURE", "psi", "hyd_psi_diag"),
            C("ELECTRICAL MAIN BUS VOLTAGE", "Volts", "main_bus_v_diag"), C("PRESSURIZATION CABIN ALTITUDE", "Feet", "cabin_alt_diag"),
            C("PRESSURIZATION CABIN ALTITUDE RATE", "Feet per minute", "cabin_rate_diag"), C("PNEUMATICS APU BLEED AIR", "Bool", "apu_bleed_diag"),
            C("AILERON LEFT DEFLECTION PCT", "Percent Over 100", "ail_l"), C("ELEVATOR DEFLECTION PCT", "Percent Over 100", "elev"), C("RUDDER DEFLECTION PCT", "Percent Over 100", "rudder"),
        ]),
        new("synaptic",
        [
            L("Caution PBA"), L("Warning PBA"),
            L("L Gen Off"), L("R Gen Off"), L("APU Gen Off"), L("L Gen Off Lamp"), L("R Gen Off Lamp"), L("APU Gen Off Lamp"),
            L("L Gen Fail Lamp"), L("R Gen Fail Lamp"), L("APU Gen Fail Lamp"), L("L Gen Disc Lamp"), L("R Gen Disc Lamp"), L("RAT Gen Lamp"),
            L("Hyd 1 SOV"), L("Hyd 2 SOV"), L("Hyd 1 SOV Lamp"), L("Hyd 2 SOV Lamp"), L("PTU"), L("ACMP 2B"), L("ACMP 3A"), L("ACMP 3B"),
            L("L Bleed Off"), L("R Bleed Off"), L("APU Bleed Off"), L("L Bleed Off Lamp"), L("R Bleed Off Lamp"), L("L Bleed Fail Lamp"), L("R Bleed Fail Lamp"),
            L("Crossbleed"), L("L Pack Off"), L("R Pack Off"), L("L Pack Off Lamp"), L("R Pack Off Lamp"), L("L Pack Fail Lamp"), L("R Pack Fail Lamp"), L("Pack Flow"),
            L("PFCC 1 Off"), L("PFCC 2 Off"), L("PFCC 3 Off"), L("PFCC 1 Off Lamp"), L("PFCC 2 Off Lamp"), L("PFCC 3 Off Lamp"),
            L("L Eng Fire"), L("R Eng Fire"), L("APU Switch"), L("Probe Heat Lamp"), L("L Boost Pump"), L("R Boost Pump"),
        ]),
    ];

    internal static async Task RunAsync(SimConnectSimulator simulator, string rootDirectory, Action<string, string> print, CancellationToken cancellationToken)
    {
        var campaign = Path.Combine(rootDirectory, "degradation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(campaign);
        await using var samplesFile = new StreamWriter(Path.Combine(campaign, "samples.csv"), append: false, new UTF8Encoding(false)) { AutoFlush = true };
        await using var journalFile = new StreamWriter(Path.Combine(campaign, "journal.jsonl"), append: false, new UTF8Encoding(false));
        var journal = new NativeFailureJournal(journalFile);
        var session = new SynapticDegradationSession();
        var gate = new object();
        var groups = Groups();
        var allColumns = groups.SelectMany(g => g.Columns).ToArray();
        await samplesFile.WriteLineAsync("time,step,title,atc_model,atc_type,atc_id," + string.Join(",", allColumns.Select(c => c.Id))).ConfigureAwait(false);
        Dictionary<string, double?> previousSynaptic = [];

        string Phase()
        {
            lock (gate)
            {
                return session.Halted ? "HALTED" : session.OpenId is { } o ? $"{o}={session.StepOf(o)}" : "CLEAN";
            }
        }

        double? Get(string id)
        {
            lock (gate)
            {
                foreach (var g in groups)
                {
                    var i = Array.FindIndex(g.Columns, c => c.Id == id);
                    if (i >= 0)
                    {
                        return g.Last[i];
                    }
                }
            }

            return null;
        }

        double? GetVariable(string variable) => allColumns.FirstOrDefault(c => c.Name == variable) is { } col ? Get(col.Id) : null;

        string F(string id, string format = "0") => Get(id) is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "n/a";

        print("degrade", "BLOCK 10C.3 Synaptic controlled degradation probe. Commands: list, baseline|cancel|apply|restore|verified <ID>, halt <reason>, note <text>, result <ID> <RESULT> [text], status, quit");
        print("degrade", $"campaign folder {Path.GetFullPath(campaign)}");
        while (!cancellationToken.IsCancellationRequested && (simulator.Status.State != SimulatorConnectionState.Connected || simulator.AircraftDetector.Current is null))
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        var descriptor = simulator.AircraftDetector.Current;
        journal.Write("environment", new { descriptor?.Title, AtcModel = descriptor?.Model, AtcType = descriptor?.Manufacturer, AtcId = descriptor?.Registration, descriptor?.LiveryFolder, candidates = SynapticDegradationSession.Candidates });
        print("degrade", $"aircraft: title='{descriptor?.Title}' atcModel='{descriptor?.Model}' atcType='{descriptor?.Manufacturer}' qualified={IsQualified(descriptor)}");

        async Task WatchConnectionAsync()
        {
            var opened = 0;
            await foreach (var status in simulator.WatchStatusAsync(cancellationToken).ConfigureAwait(false))
            {
                if (status.State == SimulatorConnectionState.Connected)
                {
                    opened++;
                    journal.Write("connection", new { state = status.State.ToString(), opened });
                }
                else if (opened > 0)
                {
                    lock (gate)
                    {
                        session.ConnectionLost();
                    }

                    journal.Write("connection", new { state = status.State.ToString(), status.Detail, halt = session.HaltReason });
                    print("WARNING", $"connection {status.State}; {Phase()}");
                }
            }
        }

        async Task WatchAircraftAsync()
        {
            AircraftDescriptor? previous = null;
            await foreach (var current in simulator.AircraftDetector.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                var same = previous is not null && current is not null && AircraftContinuity.IsSameLoadedAircraft(previous, current);
                journal.Write("descriptor", new { current?.Title, AtcModel = current?.Model, AtcType = current?.Manufacturer, AtcId = current?.Registration, sameLoadedAircraft = same });
                if (previous is not null && !same)
                {
                    lock (gate)
                    {
                        session.AircraftReplaced($"'{previous.Title}' -> '{current?.Title}'");
                    }

                    print("WARNING", $"aircraft changed; {Phase()}");
                }

                previous = current ?? previous;
            }
        }

        async Task<double[]?> ReadGroupAsync(Group g)
        {
            var active = g.Columns.Where(c => !g.Excluded.Contains(c.Id)).ToArray();
            try
            {
                return (await simulator.ReadAsync(active.Select(c => new SimulatorVariable(c.Name, c.Unit)).ToArray(), cancellationToken).ConfigureAwait(false)).ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && simulator.Status.State == SimulatorConnectionState.Connected)
            {
                var dropped = new List<string>();
                foreach (var c in active)
                {
                    try
                    {
                        await simulator.ReadAsync([new SimulatorVariable(c.Name, c.Unit)], cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception one) when (one is not OperationCanceledException)
                    {
                        g.Excluded.Add(c.Id);
                        dropped.Add(c.Name);
                    }
                }

                journal.Write("var-excluded", new { group = g.Name, error = ex.Message, dropped });
                print("degrade", $"group {g.Name} read failed; excluded: {(dropped.Count == 0 ? "none (transient)" : string.Join("; ", dropped))}");
                return null;
            }
        }

        async Task SampleLoopAsync()
        {
            var nextPrint = DateTime.UtcNow;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (simulator.Status.State == SimulatorConnectionState.Connected)
                {
                    foreach (var g in groups)
                    {
                        if (await ReadGroupAsync(g).ConfigureAwait(false) is not { } values)
                        {
                            continue;
                        }

                        var row = new double?[g.Columns.Length];
                        var k = 0;
                        for (var i = 0; i < g.Columns.Length; i++)
                        {
                            row[i] = g.Excluded.Contains(g.Columns[i].Id) ? null : values[k++];
                        }

                        lock (gate)
                        {
                            g.Last = row;
                        }
                    }

                    var d = simulator.AircraftDetector.Current;
                    await samplesFile.WriteLineAsync($"{DateTimeOffset.Now:O},{Phase()},{Csv(d?.Title)},{Csv(d?.Model)},{Csv(d?.Manufacturer)},{Csv(d?.Registration)}," +
                        string.Join(",", allColumns.Select(c => Get(c.Id) is { } v ? v.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty))).ConfigureAwait(false);

                    // Every change of a Synaptic variable is printed and journaled: lamps and switches are discrete.
                    foreach (var c in groups.Single(g => g.Name == "synaptic").Columns)
                    {
                        var now = Get(c.Id);
                        if (previousSynaptic.TryGetValue(c.Id, out var before) && before != now)
                        {
                            print("a22x", $"[{Phase()}] {c.Name}: {before?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} -> {now?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}");
                            journal.Write("a22x-change", new { phase = Phase(), variable = c.Name, before, after = now });
                        }

                        previousSynaptic[c.Id] = now;
                    }

                    if (DateTime.UtcNow >= nextPrint)
                    {
                        nextPrint = DateTime.UtcNow.AddSeconds(2);
                        print("t", $"[{Phase()}] gnd={F("on_ground")} gs={F("gs_kt", "0.0")} park={F("park_pos")} N1={F("n1_1", "0.0")}/{F("n1_2", "0.0")} N2={F("n2_1", "0.0")}/{F("n2_2", "0.0")} EGT={F("egt_1")}/{F("egt_2")} " +
                            $"engHyd={F("eng_hyd_1_psi")}/{F("eng_hyd_2_psi")} cabin={F("cabin_alt_diag")}/{F("cabin_rate_diag")} caut={F("a22x_caution_pba")} warn={F("a22x_warning_pba")}");
                    }
                }

                await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        DegradationConditions Conditions() => new(
            IsQualified(simulator.AircraftDetector.Current),
            Get("on_ground") is >= 0.5,
            Get("gs_kt") ?? double.MaxValue,
            Get("park_pos") is >= 0.5,
            Get("n2_1") ?? 0,
            Get("n2_2") ?? 0);

        async Task<double?> ReadVariableNowAsync(string variable)
        {
            try
            {
                return (await simulator.ReadAsync([new SimulatorVariable(variable, LocalUnit)], cancellationToken).ConfigureAwait(false))[0];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                print("degrade", $"read of {variable} failed: {ex.Message}");
                return null;
            }
        }

        async Task WriteAsync(string id, bool apply)
        {
            DegradationWrite? write;
            string? refusal;
            var candidate = SynapticDegradationSession.Find(id);
            var current = candidate is null ? null : await ReadVariableNowAsync(candidate.Variable).ConfigureAwait(false);
            lock (gate)
            {
                (write, refusal) = candidate is null || current is null
                    ? (null, candidate is null ? $"unknown candidate '{id}'" : "the variable could not be read")
                    : apply ? session.PlanApply(id, current.Value, Conditions()) : session.PlanRestore(id, Conditions());
            }

            if (write is null)
            {
                print("degrade", $"REFUSED {(apply ? "apply" : "restore")} {id}: {refusal}");
                journal.Write("refused", new { id, action = apply ? "apply" : "restore", refusal });
                return;
            }

            print("degrade", string.Create(CultureInfo.InvariantCulture, $"about to write {write.Candidate.Variable} = {write.Value} ({write.Action.ToUpperInvariant()}, currently {current}). Type YES to confirm."));
            var confirm = await System.Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(confirm?.Trim(), "YES", StringComparison.Ordinal))
            {
                print("degrade", "cancelled, nothing written");
                journal.Write("cancelled", new { id, write.Action });
                return;
            }

            current = await ReadVariableNowAsync(write.Candidate.Variable).ConfigureAwait(false);
            lock (gate)
            {
                (write, refusal) = current is null ? (null, "the variable could not be read")
                    : apply ? session.PlanApply(id, current.Value, Conditions()) : session.PlanRestore(id, Conditions());
            }

            if (write is null)
            {
                print("degrade", $"REFUSED at confirmation: {refusal}");
                journal.Write("refused", new { id, action = apply ? "apply" : "restore", refusal, atConfirmation = true });
                return;
            }

            journal.Write(write.Action + "-before", new { id, write.Candidate.Variable, write.Value, current, snapshot = allColumns.ToDictionary(c => c.Id, c => Get(c.Id)) });
            IReadOnlyList<string> exceptions;
            try
            {
                exceptions = await simulator.ExperimentalWriteLocalAsync(write.Candidate.Variable, LocalUnit, write.Value, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (gate)
                {
                    session.RecordUncertain(write, $"{ex.GetType().Name}: {ex.Message}");
                }

                journal.Write("halt", new { id, reason = session.HaltReason });
                print("WARNING", $"write threw {ex.GetType().Name}: {ex.Message} — HALTED. Reload the aircraft.");
                return;
            }

            lock (gate)
            {
                session.RecordWritten(write);
            }

            await Task.Delay(ReadbackDelay, cancellationToken).ConfigureAwait(false);
            var readback = await ReadVariableNowAsync(write.Candidate.Variable).ConfigureAwait(false);
            string? halt = null;
            if (!apply)
            {
                lock (gate)
                {
                    halt = session.CheckRestoreReadback(write.Candidate, readback ?? double.NaN);
                }
            }

            var stuck = readback is { } r && Math.Abs(r - write.Value) < 0.5;
            journal.Write(write.Action, new { id, write.Candidate.Variable, written = write.Value, readback, stuck, exceptions, step = session.StepOf(write.Candidate.Id).ToString(), halt });
            print("degrade", string.Create(CultureInfo.InvariantCulture, $"{write.Action.ToUpperInvariant()} {write.Candidate.Variable} = {write.Value}: exceptions=[{string.Join("; ", exceptions)}] readback after {ReadbackDelay.TotalSeconds}s = {readback?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} ({(stuck ? "held" : "NOT HELD")}) -> {Phase()}"));
            if (halt is not null)
            {
                print("WARNING", $"HALTED: {halt}");
            }
        }

        async Task CommandLoopAsync()
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await System.Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                var parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                {
                    continue;
                }

                var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                var id = argument.ToUpperInvariant();
                switch (parts[0].ToLowerInvariant())
                {
                    case "list":
                        foreach (var c in SynapticDegradationSession.Candidates)
                        {
                            print("degrade", string.Create(CultureInfo.InvariantCulture, $"{c.Id} [{c.Family}] {c.DisplayName}: {c.Variable} normal={c.NormalValue} applied={c.DegradedValue} restore={c.RestoreValue} live={GetVariable(c.Variable)?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} step={session.StepOf(c.Id)} | expect: {c.ExpectedConsequence}"));
                        }

                        break;
                    case "baseline":
                    case "verified":
                    {
                        var candidate = SynapticDegradationSession.Find(id);
                        var current = candidate is null ? null : await ReadVariableNowAsync(candidate.Variable).ConfigureAwait(false);
                        string? refusal;
                        lock (gate)
                        {
                            refusal = current is null ? $"unknown candidate '{id}' or unreadable variable"
                                : parts[0].Equals("baseline", StringComparison.OrdinalIgnoreCase) ? session.BeginBaseline(id, current.Value) : session.Verify(id, current.Value);
                        }

                        var kind = parts[0].ToLowerInvariant();
                        print("degrade", refusal is null ? $"{kind} {id}: ok ({Phase()})" : $"REFUSED {kind} {id}: {refusal}");
                        journal.Write(refusal is null ? kind : "refused", new { id, current, refusal, snapshot = refusal is null ? allColumns.ToDictionary(c => c.Id, c => Get(c.Id)) : null });
                        break;
                    }

                    case "cancel":
                    {
                        string? refusal;
                        lock (gate)
                        {
                            refusal = session.CancelBaseline(id);
                        }

                        print("degrade", refusal ?? $"baseline {id} cancelled");
                        journal.Write(refusal is null ? "baseline-cancelled" : "refused", new { id, refusal });
                        break;
                    }

                    case "apply":
                        await WriteAsync(id, apply: true).ConfigureAwait(false);
                        break;
                    case "restore":
                        await WriteAsync(id, apply: false).ConfigureAwait(false);
                        break;
                    case "halt":
                        lock (gate)
                        {
                            session.Halt(string.IsNullOrWhiteSpace(argument) ? "halted by the operator" : argument);
                        }

                        print("WARNING", $"HALTED: {session.HaltReason}");
                        journal.Write("halt", new { reason = session.HaltReason });
                        break;
                    case "note":
                        print("note", $"[{Phase()}] {argument}");
                        journal.Write("note", new { phase = Phase(), text = argument });
                        break;
                    case "result":
                    {
                        var r = argument.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                        var refusal = r.Length < 2 ? "result <ID> <RESULT> [text]" : SynapticDegradationSession.ValidateResult(r[0].ToUpperInvariant(), r[1]);
                        print("degrade", refusal is null ? $"result {r[0].ToUpperInvariant()}: {r[1]}" : $"REFUSED result: {refusal}");
                        journal.Write(refusal is null ? "result" : "refused", new { id = r.Length > 0 ? r[0].ToUpperInvariant() : string.Empty, result = r.Length > 1 ? r[1] : string.Empty, text = r.Length > 2 ? r[2] : string.Empty, refusal });
                        break;
                    }

                    case "status":
                        print("degrade", $"{Phase()}; {string.Join(" ", session.Snapshot().Select(s => $"{s.Key}={s.Value}"))}{(session.Halted ? $"; HALTED: {session.HaltReason}" : string.Empty)}");
                        break;
                    case "quit" or "q":
                        if (session.ActiveId is { } active)
                        {
                            print("WARNING", $"quitting with {active} applied: restore it or reload the aircraft");
                        }

                        journal.Write("quit", new { phase = Phase(), steps = session.Snapshot() });
                        return;
                    default:
                        print("degrade", "unknown command");
                        break;
                }
            }
        }

        var background = new[] { SampleLoopAsync(), WatchConnectionAsync(), WatchAircraftAsync() };
        try
        {
            await CommandLoopAsync().ConfigureAwait(false);
        }
        finally
        {
            journal.Write("end", new { phase = Phase(), steps = session.Snapshot(), halt = session.HaltReason });
            print("degrade", $"end: {Phase()}; {string.Join(" ", session.Snapshot().Select(s => $"{s.Key}={s.Value}"))}");
        }

        _ = background;
    }

    private static bool IsQualified(AircraftDescriptor? d) =>
        d is not null && LiveryDiscoveryAnalysis.IsSynapticA220(d.Title ?? string.Empty) && d.Model == "A220-300" && d.Manufacturer == "223";

    private static string Csv(string? value) => value is null ? string.Empty : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
