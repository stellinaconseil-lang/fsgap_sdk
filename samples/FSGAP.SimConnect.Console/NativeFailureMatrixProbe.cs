// BLOCK 10C.2 — RESEARCH ONLY: live qualification of nine documented MSFS 2024 failure key events on the Synaptic A220
// (successor of the BLOCK 10C.1 brake probe). Everything goes through the sample's one SimConnectSimulator (one native
// connection). The pilot flies; this harness (1) samples flight / engine / brake / system / Synaptic-overlay telemetry at
// 4 Hz and read-only Wear & Tear (BRAKE 36, TIRE 37, TIRE_PRESSURE 38) every 2 s, and (2) on an explicit, confirmed command
// transmits exactly ONE allowed toggle. It never writes a SimVar, an L:var or Wear & Tear. The safety model (two sends per
// event, one active failure, verified recovery, clean reload before air data, halt on any uncertainty) is
// NativeFailureMatrix, unit-tested in FSGAP.SimConnect.Tests. Not a failure provider; FailureCapabilities stay None.
//
// Commands (stdin): inject <KEY>, restore <KEY> (each then YES), verified <KEY>, reloaded, halt <reason>, note <text>,
// verdict <KEY> <VERDICT> <CONFIDENCE> [text], wt, status, quit. Keys: LB RB TB E1 E2 HYD ELEC PITOT STATIC.
using System.Globalization;
using System.Text;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Sessions;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Console;

internal static class NativeFailureMatrixProbe
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan WearInterval = TimeSpan.FromSeconds(2);

    /// <summary>A telemetry group read as one request; a group whose read fails is split to drop only the bad variable.</summary>
    private sealed record Column(string Name, string Unit, string Id);

    private sealed class Group(string name, Column[] columns)
    {
        public string Name { get; } = name;

        public Column[] Columns { get; } = columns;

        public HashSet<string> Excluded { get; } = [];

        public double?[] Last { get; set; } = new double?[columns.Length];
    }

    private static Column C(string name, string unit, string id) => new(name, unit, id);

    private static Group[] Groups() =>
    [
        new("flight",
        [
            C("SIM ON GROUND", "Bool", "on_ground"), C("GROUND VELOCITY", "Knots", "gs_kt"), C("AIRSPEED INDICATED", "Knots", "ias_kt"),
            C("AIRSPEED TRUE", "Knots", "tas_kt"), C("INDICATED ALTITUDE", "Feet", "ind_alt_ft"), C("PRESSURE ALTITUDE", "Feet", "press_alt_ft"),
            C("PLANE ALTITUDE", "Feet", "geo_alt_ft"), C("PLANE ALT ABOVE GROUND", "Feet", "agl_ft"), C("VERTICAL SPEED", "Feet per minute", "vs_fpm"),
            C("PLANE HEADING DEGREES TRUE", "Degrees", "hdg_true"), C("PLANE PITCH DEGREES", "Degrees", "pitch_deg"), C("PLANE BANK DEGREES", "Degrees", "bank_deg"),
            C("ROTATION VELOCITY BODY Y", "Degrees per second", "yaw_rate_dps"), C("ACCELERATION BODY Z", "Feet per second squared", "accel_z_fps2"),
            C("PLANE LATITUDE", "Degrees", "lat"), C("PLANE LONGITUDE", "Degrees", "lon"),
        ]),
        new("engines",
        [
            C("TURB ENG N1:1", "Percent", "n1_1"), C("TURB ENG N1:2", "Percent", "n1_2"), C("TURB ENG N2:1", "Percent", "n2_1"), C("TURB ENG N2:2", "Percent", "n2_2"),
            C("GENERAL ENG EXHAUST GAS TEMPERATURE:1", "Celsius", "egt_1"), C("GENERAL ENG EXHAUST GAS TEMPERATURE:2", "Celsius", "egt_2"),
            C("GENERAL ENG THROTTLE LEVER POSITION:1", "Percent", "tla_1"), C("GENERAL ENG THROTTLE LEVER POSITION:2", "Percent", "tla_2"),
            C("GENERAL ENG REVERSE THRUST ENGAGED:1", "Bool", "rev_1_diag"), C("GENERAL ENG REVERSE THRUST ENGAGED:2", "Bool", "rev_2_diag"),
            C("GENERAL ENG COMBUSTION:1", "Bool", "gen_comb_1_diag"), C("GENERAL ENG COMBUSTION:2", "Bool", "gen_comb_2_diag"),
            C("ENG COMBUSTION:1", "Bool", "eng_comb_1_diag"), C("ENG COMBUSTION:2", "Bool", "eng_comb_2_diag"),
            C("ENG FAILED:1", "Bool", "eng_failed_1"), C("ENG FAILED:2", "Bool", "eng_failed_2"),
            C("GENERAL ENG FAILED:1", "Bool", "gen_eng_failed_1"), C("GENERAL ENG FAILED:2", "Bool", "gen_eng_failed_2"),
            C("ENG ON FIRE:1", "Bool", "eng_fire_1"), C("ENG ON FIRE:2", "Bool", "eng_fire_2"),
            C("ENG FUEL FLOW PPH:1", "Pounds per hour", "ff_1_diag"), C("ENG FUEL FLOW PPH:2", "Pounds per hour", "ff_2_diag"),
        ]),
        new("brakes",
        [
            C("BRAKE LEFT POSITION", "Position", "brake_l"), C("BRAKE RIGHT POSITION", "Position", "brake_r"), C("BRAKE INDICATOR", "Position", "brake_ind"),
            C("BRAKE PARKING POSITION", "Bool", "park_pos"), C("BRAKE DEPENDENT HYDRAULIC PRESSURE", "psi", "brake_hyd_psi"),
            C("BRAKE LEFT TEMPERATURE", "Celsius", "brake_l_temp_c"), C("BRAKE RIGHT TEMPERATURE", "Celsius", "brake_r_temp_c"),
            C("LEFT WHEEL RPM", "RPM", "wheel_l_rpm"), C("RIGHT WHEEL RPM", "RPM", "wheel_r_rpm"), C("CENTER WHEEL RPM", "RPM", "wheel_c_rpm"),
        ]),
        new("systems",
        [
            C("HYDRAULIC PRESSURE", "psi", "hyd_psi"), C("ENG HYDRAULIC PRESSURE:1", "psi", "eng_hyd_1_psi"), C("ENG HYDRAULIC PRESSURE:2", "psi", "eng_hyd_2_psi"),
            C("CIRCUIT HYDRAULIC PUMP ON", "Bool", "circ_hyd_pump"), C("ELECTRICAL MAIN BUS VOLTAGE", "Volts", "main_bus_v_diag"),
            C("ELECTRICAL BATTERY VOLTAGE:1", "Volts", "batt_1_v_diag"), C("ELECTRICAL TOTAL LOAD AMPS", "Amperes", "total_load_a_diag"),
            C("PITOT HEAT", "Bool", "pitot_heat"), C("CIRCUIT PITOT HEAT ON", "Bool", "circ_pitot_heat"), C("PITOT ICE PCT", "Percent Over 100", "pitot_ice"),
            C("ALTERNATE STATIC SOURCE OPEN", "Bool", "alt_static"),
            C("PARTIAL PANEL ELECTRICAL", "Enum", "pp_electrical"), C("PARTIAL PANEL PITOT", "Enum", "pp_pitot"), C("PARTIAL PANEL AIRSPEED", "Enum", "pp_airspeed"),
            C("PARTIAL PANEL ALTIMETER", "Enum", "pp_altimeter"), C("PARTIAL PANEL VERTICAL VELOCITY", "Enum", "pp_vsi"), C("PARTIAL PANEL ENGINE", "Enum", "pp_engine"),
            C("PARTIAL PANEL AVIONICS", "Enum", "pp_avionics"),
        ]),
        new("synaptic",
        [
            C("L:A22X Caution PBA", "number", "a22x_caution"), C("L:A22X Warning PBA", "number", "a22x_warning"), C("L:A22X Parking Brake", "number", "a22x_park"),
            C("L:A22X Alternate Brake", "number", "a22x_alt_brake"), C("L:A22X L Gen Fail Lamp", "number", "a22x_l_gen_fail"), C("L:A22X R Gen Fail Lamp", "number", "a22x_r_gen_fail"),
            C("L:A22X L Gen Off Lamp", "number", "a22x_l_gen_off"), C("L:A22X R Gen Off Lamp", "number", "a22x_r_gen_off"), C("L:A22X APU Gen Fail Lamp", "number", "a22x_apu_gen_fail"),
            C("L:A22X RAT Gen Lamp", "number", "a22x_rat_gen"), C("L:A22X Hyd 1 SOV Lamp", "number", "a22x_hyd1_sov_lamp"), C("L:A22X Hyd 2 SOV Lamp", "number", "a22x_hyd2_sov_lamp"),
            C("L:A22X L Eng Fire", "number", "a22x_l_eng_fire"), C("L:A22X R Eng Fire", "number", "a22x_r_eng_fire"), C("L:A22X Probe Heat Lamp", "number", "a22x_probe_heat_lamp"),
            C("L:A22X L Boost Pump", "number", "a22x_l_boost"), C("L:A22X R Boost Pump", "number", "a22x_r_boost"), C("L:A22X APU Switch", "number", "a22x_apu_switch"),
            C("L:A22X Throttle 1 TLA", "number", "a22x_tla_1"), C("L:A22X Throttle 2 TLA", "number", "a22x_tla_2"),
        ]),
    ];

    private static readonly (int Component, string Name)[] WearComponents = [(36, "BRAKE"), (37, "TIRE"), (38, "TIRE_PRESSURE")];

    private static readonly (string Name, string Unit)[] WearStates =
    [
        ("WEAR AND TEAR LEVEL", "Percent Over 100"),
        ("WEAR AND TEAR IS WEAK", "Bool"),
        ("WEAR AND TEAR IS FAILED", "Bool"),
        ("WEAR AND TEAR WEAK VALUE", "Percent Over 100"),
    ];

    internal static async Task RunAsync(SimConnectSimulator simulator, string rootDirectory, Action<string, string> print, CancellationToken cancellationToken)
    {
        var campaign = Path.Combine(rootDirectory, "campaign-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(campaign);
        await using var samplesFile = new StreamWriter(Path.Combine(campaign, "samples.csv"), append: false, new UTF8Encoding(false)) { AutoFlush = true };
        await using var journalFile = new StreamWriter(Path.Combine(campaign, "journal.jsonl"), append: false, new UTF8Encoding(false));
        var journal = new NativeFailureJournal(journalFile);
        var matrix = new NativeFailureMatrix();
        var gate = new object();
        var groups = Groups();
        var allColumns = groups.SelectMany(g => g.Columns).ToArray();
        await samplesFile.WriteLineAsync("time,phase,title,atc_model,atc_type,atc_id," + string.Join(",", allColumns.Select(c => c.Id))).ConfigureAwait(false);
        string? lastWear = null;
        var forceWear = false;

        string Phase()
        {
            lock (gate)
            {
                return matrix.Halted ? "HALTED" : matrix.ActiveKey is { } a ? a + "=INJECTED" : "NORMAL";
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

        string F(string id, string format = "0.0") => Get(id) is { } v ? v.ToString(format, CultureInfo.InvariantCulture) : "n/a";

        print("matrix", "BLOCK 10C.2 native failure matrix. Commands: inject|restore <KEY> (+YES), verified <KEY>, reloaded, halt <reason>, note <text>, verdict <KEY> <VERDICT> <CONFIDENCE> [text], wt, status, quit");
        print("matrix", $"keys: {string.Join(" ", NativeFailureMatrix.Definitions.Select(d => $"{d.Key}={d.EventName}"))}");
        print("matrix", $"campaign folder {Path.GetFullPath(campaign)}");
        while (!cancellationToken.IsCancellationRequested && (simulator.Status.State != SimulatorConnectionState.Connected || simulator.AircraftDetector.Current is null))
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        var descriptor = simulator.AircraftDetector.Current;
        journal.Write("environment", new { descriptor?.Title, AtcModel = descriptor?.Model, AtcType = descriptor?.Manufacturer, AtcId = descriptor?.Registration, descriptor?.LiveryFolder, connection = "one SimConnectSimulator, one native connection" });
        print("matrix", $"aircraft: title='{descriptor?.Title}' atcModel='{descriptor?.Model}' atcType='{descriptor?.Manufacturer}' atcId='{descriptor?.Registration}' qualified={IsQualified(descriptor)}");

        async Task WatchConnectionAsync()
        {
            var opened = 0;
            await foreach (var status in simulator.WatchStatusAsync(cancellationToken).ConfigureAwait(false))
            {
                if (status.State == SimulatorConnectionState.Connected)
                {
                    opened++;
                    journal.Write("connection", new { state = status.State.ToString(), opened });
                    if (opened > 1)
                    {
                        print("WARNING", $"native connection re-opened (#{opened})");
                    }
                }
                else if (opened > 0)
                {
                    lock (gate)
                    {
                        matrix.ConnectionLost();
                    }

                    journal.Write("connection", new { state = status.State.ToString(), status.Detail, phase = Phase(), halt = matrix.HaltReason });
                    print("WARNING", $"connection {status.State}; phase {Phase()}{(matrix.Halted ? $" — MATRIX HALTED: {matrix.HaltReason}" : string.Empty)}");
                }
            }
        }

        async Task WatchAircraftAsync()
        {
            AircraftDescriptor? previous = null;
            await foreach (var current in simulator.AircraftDetector.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                var same = previous is not null && current is not null && AircraftContinuity.IsSameLoadedAircraft(previous, current);
                journal.Write("descriptor", new { current?.Title, AtcModel = current?.Model, AtcType = current?.Manufacturer, AtcId = current?.Registration, current?.LiveryFolder, sameLoadedAircraft = same });
                if (previous is not null && !same)
                {
                    lock (gate)
                    {
                        matrix.AircraftReplaced($"'{previous.Title}' -> '{current?.Title}'");
                    }

                    print("WARNING", $"aircraft changed '{previous.Title}' -> '{current?.Title}'; phase {Phase()}{(matrix.Halted ? $" — MATRIX HALTED: {matrix.HaltReason}" : string.Empty)}");
                }

                previous = current ?? previous;
            }
        }

        async Task<double[]?> ReadGroupAsync(Group g)
        {
            var active = g.Columns.Where(c => !g.Excluded.Contains(c.Id)).ToArray();
            if (active.Length == 0)
            {
                return null;
            }

            try
            {
                return (await simulator.ReadAsync(active.Select(c => new SimulatorVariable(c.Name, c.Unit)).ToArray(), cancellationToken).ConfigureAwait(false)).ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException && simulator.Status.State == SimulatorConnectionState.Connected)
            {
                // Find the bad variable(s) once, one by one, and drop only those.
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
                        dropped.Add($"{c.Name} ({one.GetType().Name}: {one.Message})");
                    }
                }

                journal.Write("var-excluded", new { group = g.Name, error = ex.Message, dropped });
                print("matrix", $"group {g.Name} read failed ({ex.GetType().Name}); excluded: {(dropped.Count == 0 ? "none (transient)" : string.Join("; ", dropped))}");
                return null;
            }
        }

        async Task SampleLoopAsync()
        {
            var wearVars = WearComponents.SelectMany(c => WearStates.Select(s => new SimulatorVariable($"{s.Name}:{c.Component}", s.Unit))).ToArray();
            var nextPrint = DateTime.UtcNow;
            var nextWear = DateTime.UtcNow;
            var wearErrorReported = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                if (simulator.Status.State == SimulatorConnectionState.Connected)
                {
                    foreach (var g in groups)
                    {
                        var values = await ReadGroupAsync(g).ConfigureAwait(false);
                        if (values is null)
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
                    var cells = allColumns.Select(c => Get(c.Id) is { } v ? v.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty);
                    await samplesFile.WriteLineAsync($"{DateTimeOffset.Now:O},{Phase()},{Csv(d?.Title)},{Csv(d?.Model)},{Csv(d?.Manufacturer)},{Csv(d?.Registration)}," + string.Join(",", cells)).ConfigureAwait(false);

                    if (DateTime.UtcNow >= nextPrint)
                    {
                        nextPrint = DateTime.UtcNow.AddSeconds(1);
                        print("t", $"[{Phase()}] gnd={F("on_ground", "0")} gs={F("gs_kt")} ias={F("ias_kt")} alt={F("ind_alt_ft", "0")}/{F("press_alt_ft", "0")}/{F("geo_alt_ft", "0")} vs={F("vs_fpm", "0")} " +
                            $"N1={F("n1_1")}/{F("n1_2")} N2={F("n2_1")}/{F("n2_2")} EGT={F("egt_1", "0")}/{F("egt_2", "0")} engFail={F("eng_failed_1", "0")}/{F("eng_failed_2", "0")} " +
                            $"brk={F("brake_l", "0.00")}/{F("brake_r", "0.00")} park={F("park_pos", "0")} T={F("brake_l_temp_c")}/{F("brake_r_temp_c")} hyd={F("hyd_psi", "0")} bus={F("main_bus_v_diag")} " +
                            $"pp(el/pit/asi/alt)={F("pp_electrical", "0")}/{F("pp_pitot", "0")}/{F("pp_airspeed", "0")}/{F("pp_altimeter", "0")} caut={F("a22x_caution", "0")} warn={F("a22x_warning", "0")}");
                    }

                    if (DateTime.UtcNow >= nextWear || forceWear)
                    {
                        forceWear = false;
                        nextWear = DateTime.UtcNow + WearInterval;
                        try
                        {
                            var wear = await simulator.ReadAsync(wearVars, cancellationToken).ConfigureAwait(false);
                            var text = string.Join(" | ", WearComponents.Select((c, ci) => string.Create(CultureInfo.InvariantCulture, $"{c.Name}({c.Component}) {wear[ci * 4]:0.###}/{wear[(ci * 4) + 1]:0}/{wear[(ci * 4) + 2]:0}/{wear[(ci * 4) + 3]:0.###}")));
                            if (text != lastWear)
                            {
                                lastWear = text;
                                print("wear", $"[{Phase()}] level/weak/failed/weakValue: {text}");
                                journal.Write("wear", new { phase = Phase(), values = wearVars.Select((v, i) => new { v.Name, Value = wear[i] }) });
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            if (!wearErrorReported)
                            {
                                wearErrorReported = true;
                                journal.Write("wear-error", new { ex.Message });
                                print("wear", $"Wear & Tear read failed (reported once): {ex.Message}");
                            }
                        }
                    }
                }

                await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        NativeFailureConditions Conditions() => new(
            IsQualified(simulator.AircraftDetector.Current),
            Get("on_ground") is >= 0.5,
            Get("gs_kt") ?? double.MaxValue,
            Get("park_pos") is >= 0.5,
            Get("agl_ft") ?? 0,
            Get("n2_1") ?? 0,
            Get("n2_2") ?? 0);

        async Task SendAsync(string key, NativeFailureAction action)
        {
            NativeFailurePlan? plan;
            string? refusal;
            var conditions = Conditions();
            lock (gate)
            {
                (plan, refusal) = action == NativeFailureAction.Inject ? matrix.PlanInject(key, conditions) : matrix.PlanRestore(key, conditions);
            }

            if (plan is null)
            {
                print("matrix", $"REFUSED {action} {key}: {refusal}");
                journal.Write("refused", new { key, action = action.ToString(), refusal, conditions = conditions.ToString() });
                return;
            }

            print("matrix", $"about to transmit {plan.Definition.EventName} exactly once — {action.ToString().ToUpperInvariant()}. Type YES to confirm.");
            var confirm = await System.Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(confirm?.Trim(), "YES", StringComparison.Ordinal))
            {
                print("matrix", "cancelled, nothing transmitted");
                journal.Write("cancelled", new { key, action = action.ToString() });
                return;
            }

            // Re-check after the confirmation delay: the aircraft may have moved.
            conditions = Conditions();
            lock (gate)
            {
                (plan, refusal) = action == NativeFailureAction.Inject ? matrix.PlanInject(key, conditions) : matrix.PlanRestore(key, conditions);
            }

            if (plan is null)
            {
                print("matrix", $"REFUSED {action} {key} at confirmation: {refusal}");
                journal.Write("refused", new { key, action = action.ToString(), refusal, conditions = conditions.ToString(), atConfirmation = true });
                return;
            }

            journal.Write(action == NativeFailureAction.Inject ? "inject-before" : "restore-before", new { key, plan.Definition.EventName, conditions = conditions.ToString(), snapshot = allColumns.ToDictionary(c => c.Id, c => Get(c.Id)) });
            NativeFailureEventResult result;
            try
            {
                result = await simulator.ExperimentalTransmitNativeFailureEventAsync(plan.Definition.EventName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lock (gate)
                {
                    matrix.RecordUncertain(plan, $"{ex.GetType().Name}: {ex.Message}");
                }

                journal.Write("halt", new { key, reason = matrix.HaltReason });
                print("WARNING", $"transmission threw {ex.GetType().Name}: {ex.Message} — MATRIX HALTED (toggle count uncertain). Reload the aircraft.");
                return;
            }

            if (!result.Accepted)
            {
                lock (gate)
                {
                    matrix.RecordUncertain(plan, $"map=0x{result.MapHResult ?? 0:X8} transmit=0x{result.TransmitHResult:X8} exceptions=[{string.Join("; ", result.Exceptions)}]");
                }
            }
            else
            {
                lock (gate)
                {
                    matrix.RecordSent(plan);
                }
            }

            journal.Write(action == NativeFailureAction.Inject ? "inject" : "restore", new
            {
                key,
                result.EventName,
                result.ClientEventId,
                result.MapHResult,
                result.TransmitHResult,
                result.Exceptions,
                result.SentAt,
                result.Accepted,
                state = matrix.StateOf(plan.Definition.Key).ToString(),
                halt = matrix.HaltReason,
            });
            print("matrix", $"{result.EventName} {action.ToString().ToUpperInvariant()}: map=0x{result.MapHResult ?? 0:X8}{(result.MapHResult is null ? " (already mapped)" : string.Empty)} transmit=0x{result.TransmitHResult:X8} exceptions=[{string.Join("; ", result.Exceptions)}] accepted={result.Accepted} -> {plan.Definition.Key}={matrix.StateOf(plan.Definition.Key)}; phase {Phase()}");
            if (matrix.Halted)
            {
                print("WARNING", $"MATRIX HALTED: {matrix.HaltReason}");
            }

            forceWear = true;
            lastWear = null;
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
                switch (parts[0].ToLowerInvariant())
                {
                    case "inject":
                        await SendAsync(argument.ToUpperInvariant(), NativeFailureAction.Inject).ConfigureAwait(false);
                        break;
                    case "restore":
                        await SendAsync(argument.ToUpperInvariant(), NativeFailureAction.Restore).ConfigureAwait(false);
                        break;
                    case "verified":
                    {
                        string? refusal;
                        lock (gate)
                        {
                            refusal = matrix.Verify(argument.ToUpperInvariant());
                        }

                        print("matrix", refusal is null ? $"recovery of {argument.ToUpperInvariant()} verified" : $"REFUSED verified: {refusal}");
                        journal.Write(refusal is null ? "verified" : "refused", new { key = argument.ToUpperInvariant(), refusal });
                        break;
                    }

                    case "reloaded":
                    {
                        string? refusal;
                        lock (gate)
                        {
                            refusal = matrix.RecordCleanReload();
                        }

                        print("matrix", refusal is null ? $"clean aircraft reload recorded (air-data tests unlocked: {matrix.CleanReloadAfterGroundTests})" : $"REFUSED reloaded: {refusal}");
                        journal.Write(refusal is null ? "clean-reload" : "refused", new { refusal, matrix.CleanReloadAfterGroundTests });
                        break;
                    }

                    case "halt":
                        lock (gate)
                        {
                            matrix.Halt(string.IsNullOrWhiteSpace(argument) ? "halted by the operator" : argument);
                        }

                        print("WARNING", $"MATRIX HALTED: {matrix.HaltReason}");
                        journal.Write("halt", new { reason = matrix.HaltReason, phase = Phase() });
                        break;
                    case "note":
                        print("note", $"[{Phase()}] {argument}");
                        journal.Write("note", new { phase = Phase(), text = argument });
                        break;
                    case "verdict":
                    {
                        var v = argument.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
                        var refusal = v.Length < 3 ? "verdict <KEY> <VERDICT> <CONFIDENCE> [text]" : NativeFailureMatrix.ValidateVerdict(v[0].ToUpperInvariant(), v[1], v[2]);
                        print("matrix", refusal is null ? $"verdict {v[0].ToUpperInvariant()}: {v[1]} ({v[2]})" : $"REFUSED verdict: {refusal}");
                        journal.Write(refusal is null ? "verdict" : "refused", refusal is null
                            ? new { key = v[0].ToUpperInvariant(), verdict = v[1], confidence = v[2], text = v.Length > 3 ? v[3] : string.Empty }
                            : new { key = string.Empty, verdict = string.Empty, confidence = string.Empty, text = refusal });
                        break;
                    }

                    case "wt":
                        lastWear = null;
                        forceWear = true;
                        break;
                    case "status":
                        lock (gate)
                        {
                            print("matrix", $"phase {Phase()}; {string.Join(" ", matrix.Snapshot().Select(s => $"{s.Key}={s.Value}"))}; cleanReloadAfterGround={matrix.CleanReloadAfterGroundTests}{(matrix.Halted ? $"; HALTED: {matrix.HaltReason}" : string.Empty)}");
                        }

                        break;
                    case "quit" or "q":
                        if (matrix.ActiveKey is { } active)
                        {
                            print("WARNING", $"quitting with {active} injected: restore it or reload the aircraft");
                        }

                        journal.Write("quit", new { phase = Phase(), states = matrix.Snapshot() });
                        return;
                    default:
                        print("matrix", "unknown command");
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
            journal.Write("end", new { phase = Phase(), states = matrix.Snapshot(), halt = matrix.HaltReason, journalLines = journal.Count + 1 });
            print("matrix", $"end: phase {Phase()}; {string.Join(" ", matrix.Snapshot().Select(s => $"{s.Key}={s.Value}"))}");
        }

        _ = background;
    }

    private static bool IsQualified(AircraftDescriptor? d) =>
        d is not null && LiveryDiscoveryAnalysis.IsSynapticA220(d.Title ?? string.Empty) && d.Model == "A220-300" && d.Manufacturer == "223";

    private static string Csv(string? value) => value is null ? string.Empty : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
