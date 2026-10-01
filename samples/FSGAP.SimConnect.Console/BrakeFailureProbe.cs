// BLOCK 10C.1 — RESEARCH ONLY: live probe of the documented MSFS 2024 brake-failure key events on the Synaptic A220.
//
// Everything goes through the sample's one SimConnectSimulator (one native connection). The pilot drives the aircraft;
// this harness only (1) samples brake / wheel / ground telemetry at 4 Hz and the read-only Wear & Tear states of TIRE (37)
// and TIRE_PRESSURE (38) (plus BRAKE (36) as a control) every 2 s, and (2) on an explicit, confirmed command transmits
// exactly ONE toggle. It never writes a SimVar, an L:var or Wear & Tear. Not a failure provider.
//
// Commands (stdin): fail L|R|T, verified L|R, note <text>, wt, status, quit.
// Gates before an inject: Synaptic A220 title, SIM ON GROUND, ground speed < 1 kt, no other toggle active, at most two
// transmissions per event per run (inject + restore), T only after L and R were both restored AND verified by the pilot,
// and only with the parking brake set. A restore (the same toggle again) is only gated on the aircraft title.
using System.Globalization;
using System.Text;
using System.Text.Json;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Console;

internal static class BrakeFailureProbe
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan WearInterval = TimeSpan.FromSeconds(2);
    private const double MaxTransmitGroundSpeedKnots = 1.0;
    private const double ActiveFailureSpeedWarningKnots = 8.0;
    private const int MaxTransmissionsPerEvent = 2;

    private static readonly (string Name, string Unit, string Column)[] Telemetry =
    [
        ("SIM ON GROUND", "Bool", "on_ground"),
        ("GROUND VELOCITY", "Knots", "gs_kt"),
        ("BRAKE LEFT POSITION", "Position", "brake_l"),
        ("BRAKE RIGHT POSITION", "Position", "brake_r"),
        ("BRAKE INDICATOR", "Position", "brake_ind"),
        ("BRAKE PARKING POSITION", "Bool", "park_pos"),
        ("BRAKE PARKING INDICATOR", "Bool", "park_ind"),
        ("BRAKE DEPENDENT HYDRAULIC PRESSURE", "psi", "brake_hyd_psi"),
        ("BRAKE LEFT TEMPERATURE", "Celsius", "brake_l_temp_c"),
        ("BRAKE RIGHT TEMPERATURE", "Celsius", "brake_r_temp_c"),
        ("LEFT WHEEL RPM", "RPM", "wheel_l_rpm"),
        ("RIGHT WHEEL RPM", "RPM", "wheel_r_rpm"),
        ("CENTER WHEEL RPM", "RPM", "wheel_c_rpm"),
        ("ROTATION VELOCITY BODY Y", "Degrees per second", "yaw_rate_dps"),
        ("ACCELERATION BODY Z", "Feet per second squared", "accel_z_fps2"),
        ("PLANE HEADING DEGREES TRUE", "Degrees", "hdg_true"),
        ("PLANE LATITUDE", "Degrees", "lat"),
        ("PLANE LONGITUDE", "Degrees", "lon"),
        ("L:A22X Parking Brake", "number", "a22x_park"),
        ("L:A22X Alternate Brake", "number", "a22x_alt_brake"),
        ("L:A22X Caution PBA", "number", "a22x_caution"),
        ("L:A22X Warning PBA", "number", "a22x_warning"),
    ];

    private static readonly (int Component, string ComponentName)[] WearComponents = [(37, "TIRE"), (38, "TIRE_PRESSURE"), (36, "BRAKE")];

    private static readonly (string Name, string Unit)[] WearStates =
    [
        ("WEAR AND TEAR LEVEL", "Percent Over 100"),
        ("WEAR AND TEAR IS WEAK", "Bool"),
        ("WEAR AND TEAR IS FAILED", "Bool"),
        ("WEAR AND TEAR WEAK VALUE", "Percent Over 100"),
    ];

    private static readonly Dictionary<char, string> Events = new()
    {
        ['L'] = NativeFailureProbeEvents.ToggleLeftBrakeFailure,
        ['R'] = NativeFailureProbeEvents.ToggleRightBrakeFailure,
        ['T'] = NativeFailureProbeEvents.ToggleTotalBrakeFailure,
    };

    internal static async Task RunAsync(SimConnectSimulator simulator, string outputDirectory, Action<string, string> print, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        await using var samples = new StreamWriter(Path.Combine(outputDirectory, $"brake-probe-{stamp}-samples.csv"), append: false, Encoding.UTF8);
        await using var journal = new StreamWriter(Path.Combine(outputDirectory, $"brake-probe-{stamp}-journal.jsonl"), append: false, Encoding.UTF8);
        samples.AutoFlush = true;
        journal.AutoFlush = true;
        await samples.WriteLineAsync("time,phase," + string.Join(",", Telemetry.Select(t => t.Column))).ConfigureAwait(false);

        var gate = new object();
        var transmissions = Events.Values.ToDictionary(e => e, _ => 0);
        var verified = new HashSet<char>();
        double[]? last = null;
        string? lastWear = null;
        var lastNote = string.Empty;
        var busy = 0;

        void Journal(string kind, object payload)
        {
            var line = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, kind, payload });
            lock (journal)
            {
                journal.WriteLine(line);
            }
        }

        string Phase()
        {
            lock (gate)
            {
                var active = transmissions.Where(t => t.Value % 2 == 1).Select(t => t.Key).ToArray();
                return active.Length == 0 ? "NORMAL" : string.Join("+", active) + "=ACTIVE";
            }
        }

        print("brakes", "BLOCK 10C.1 brake-failure probe. Commands: fail L|R|T, verified L|R, note <text>, wt, status, quit");
        print("brakes", $"logging to {Path.GetFullPath(outputDirectory)}");
        while (!cancellationToken.IsCancellationRequested && simulator.Status.State != SimulatorConnectionState.Connected)
        {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        var aircraft = simulator.AircraftDetector.Current;
        print("brakes", $"aircraft: title='{aircraft?.Title}' atcModel='{aircraft?.Model}' synapticA220={LiveryDiscoveryAnalysis.IsSynapticA220(aircraft?.Title ?? string.Empty)}");
        Journal("environment", new { aircraft?.Title, aircraft?.Model, aircraft?.Manufacturer, connections = "1 SimConnectSimulator" });

        var telemetryVars = Telemetry.Select(t => new SimulatorVariable(t.Name, t.Unit)).ToArray();
        var wearVars = WearComponents.SelectMany(c => WearStates.Select(s => new SimulatorVariable($"{s.Name}:{c.Component}", s.Unit))).ToArray();

        async Task SampleLoopAsync()
        {
            var nextPrint = DateTime.UtcNow;
            var nextWear = DateTime.UtcNow;
            var wearErrorReported = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var values = (await simulator.ReadAsync(telemetryVars, cancellationToken).ConfigureAwait(false)).ToArray();
                    lock (gate)
                    {
                        last = values;
                    }

                    var phase = Phase();
                    await samples.WriteLineAsync($"{DateTimeOffset.Now:O},{phase}," + string.Join(",", values.Select(v => v.ToString("0.####", CultureInfo.InvariantCulture)))).ConfigureAwait(false);
                    if (DateTime.UtcNow >= nextPrint)
                    {
                        nextPrint = DateTime.UtcNow.AddSeconds(1);
                        print("brakes", Describe(values, phase));
                        if (phase != "NORMAL" && Value(values, "gs_kt") > ActiveFailureSpeedWarningKnots)
                        {
                            print("WARNING", $"brake failure ACTIVE and ground speed {Value(values, "gs_kt"):0.0} kt > {ActiveFailureSpeedWarningKnots} kt: slow down");
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    print("brakes", $"telemetry read failed: {ex.GetType().Name}: {ex.Message}");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }

                if (DateTime.UtcNow >= nextWear)
                {
                    nextWear = DateTime.UtcNow + WearInterval;
                    try
                    {
                        var wear = await simulator.ReadAsync(wearVars, cancellationToken).ConfigureAwait(false);
                        var text = DescribeWear(wear);
                        if (text != lastWear)
                        {
                            print("wear", $"[{Phase()}] {text}");
                            Journal("wear", new { phase = Phase(), values = wearVars.Select((v, i) => new { v.Name, v.Unit, Value = wear[i] }) });
                            lastWear = text;
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (!wearErrorReported)
                        {
                            wearErrorReported = true;
                            print("wear", $"Wear & Tear read failed (reported once): {ex.GetType().Name}: {ex.Message}");
                            Journal("wear-error", new { ex.Message });
                        }
                    }
                }

                await Task.Delay(SampleInterval, cancellationToken).ConfigureAwait(false);
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

                var command = parts[0].ToLowerInvariant();
                var argument = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                switch (command)
                {
                    case "quit" or "q":
                        if (Phase() != "NORMAL")
                        {
                            print("WARNING", $"quitting with {Phase()}: restore it (fail <same>) or reload the aircraft");
                        }

                        Journal("quit", new { phase = Phase() });
                        return;
                    case "note" or "n":
                        lastNote = argument;
                        print("note", $"[{Phase()}] {argument}");
                        Journal("note", new { phase = Phase(), text = argument });
                        break;
                    case "wt":
                        lastWear = null;
                        break;
                    case "status":
                        lock (gate)
                        {
                            print("brakes", $"phase {Phase()}; transmissions {string.Join(", ", transmissions.Select(t => $"{t.Key}={t.Value}"))}; verified [{string.Join(",", verified)}]; last note '{lastNote}'");
                        }

                        break;
                    case "verified":
                        Verify(argument);
                        break;
                    case "fail":
                        if (Interlocked.Exchange(ref busy, 1) == 0)
                        {
                            try
                            {
                                await FailAsync(argument).ConfigureAwait(false);
                            }
                            finally
                            {
                                Volatile.Write(ref busy, 0);
                            }
                        }

                        break;
                    default:
                        print("brakes", "unknown command (fail L|R|T, verified L|R, note <text>, wt, status, quit)");
                        break;
                }
            }
        }

        void Verify(string argument)
        {
            var key = argument.Length == 1 ? char.ToUpperInvariant(argument[0]) : '?';
            if (key is not ('L' or 'R'))
            {
                print("brakes", "verified L|R");
                return;
            }

            lock (gate)
            {
                var count = transmissions[Events[key]];
                if (count < 2 || count % 2 != 0)
                {
                    print("brakes", $"refused: {Events[key]} was transmitted {count} time(s); restoration needs inject + restore");
                    return;
                }

                verified.Add(key);
            }

            print("brakes", $"pilot verified normal braking restored after {Events[key]}");
            Journal("verified", new { eventName = Events[key] });
        }

        async Task FailAsync(string argument)
        {
            var key = argument.Length == 1 ? char.ToUpperInvariant(argument[0]) : '?';
            if (!Events.TryGetValue(key, out var eventName))
            {
                print("brakes", "fail L|R|T");
                return;
            }

            string? refusal;
            double[]? values;
            int count;
            lock (gate)
            {
                values = last;
                count = transmissions[eventName];
                var otherActive = transmissions.Any(t => t.Key != eventName && t.Value % 2 == 1);
                var title = simulator.AircraftDetector.Current?.Title ?? string.Empty;
                // A restore (odd count: this failure is active) is a safety action: never gated on ground state or speed.
                var restoring = count % 2 == 1;
                refusal =
                    !LiveryDiscoveryAnalysis.IsSynapticA220(title) ? $"loaded aircraft '{title}' is not the Synaptic A220"
                    : values is null ? "no telemetry yet"
                    : restoring ? null
                    : Value(values, "on_ground") < 0.5 ? "SIM ON GROUND is false"
                    : Value(values, "gs_kt") >= MaxTransmitGroundSpeedKnots ? $"ground speed {Value(values, "gs_kt"):0.0} kt: stop the aircraft first"
                    : otherActive ? "another brake failure is still active: restore it first"
                    : count >= MaxTransmissionsPerEvent ? $"{eventName} already transmitted {count} times this run (inject + restore); reload the aircraft for another cycle"
                    : key == 'T' && !(verified.Contains('L') && verified.Contains('R')) ? "total brake test only after L and R were restored and verified"
                    : key == 'T' && Value(values, "park_pos") < 0.5 ? "set the parking brake before the total brake test"
                    : null;
            }

            if (refusal is not null)
            {
                print("brakes", $"REFUSED {eventName}: {refusal}");
                Journal("refused", new { eventName, refusal });
                return;
            }

            var intent = count % 2 == 0 ? "INJECT (expected: failure becomes active)" : "RESTORE (expected: failure cleared)";
            print("brakes", $"about to transmit {eventName} exactly once — {intent}. Type YES to confirm.");
            var confirm = await System.Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(confirm?.Trim(), "YES", StringComparison.Ordinal))
            {
                print("brakes", "cancelled, nothing transmitted");
                return;
            }

            print("brakes", $"before: {Describe(values!, Phase())}");
            Journal("before", new { eventName, intent, telemetry = Snapshot(values!) });
            NativeFailureEventResult result;
            try
            {
                result = await simulator.ExperimentalTransmitNativeFailureEventAsync(eventName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                print("brakes", $"transmission threw {ex.GetType().Name}: {ex.Message} (counted as NOT sent)");
                Journal("transmit-error", new { eventName, ex.Message });
                return;
            }

            if (result.TransmitHResult >= 0)
            {
                lock (gate)
                {
                    transmissions[eventName]++;
                    if (key is 'L' or 'R' && transmissions[eventName] % 2 == 1)
                    {
                        verified.Remove(key);
                    }
                }
            }

            print("brakes", $"{eventName}: map=0x{result.MapHResult ?? 0:X8}{(result.MapHResult is null ? " (already mapped)" : string.Empty)} transmit=0x{result.TransmitHResult:X8} exceptions=[{string.Join("; ", result.Exceptions)}] accepted={result.Accepted} -> phase {Phase()}");
            Journal("transmit", new { result.EventName, result.ClientEventId, result.MapHResult, result.TransmitHResult, result.Exceptions, result.SentAt, result.Accepted, phase = Phase() });
            lastWear = null;
        }

        var sampling = SampleLoopAsync();
        try
        {
            await CommandLoopAsync().ConfigureAwait(false);
        }
        finally
        {
            Journal("end", new { phase = Phase(), transmissions });
            print("brakes", $"end: phase {Phase()}; transmissions {string.Join(", ", transmissions.Select(t => $"{t.Key}={t.Value}"))}");
        }

        _ = sampling;
    }

    private static double Value(double[] values, string column) => values[Array.FindIndex(Telemetry, t => t.Column == column)];

    private static Dictionary<string, double> Snapshot(double[] values) =>
        Telemetry.Select((t, i) => (t.Column, values[i])).ToDictionary(p => p.Column, p => p.Item2);

    private static string Describe(double[] v, string phase) => string.Create(
        CultureInfo.InvariantCulture,
        $"[{phase}] gnd={Value(v, "on_ground"):0} gs={Value(v, "gs_kt"):0.0}kt brkL={Value(v, "brake_l"):0.00} brkR={Value(v, "brake_r"):0.00} ind={Value(v, "brake_ind"):0.00} park={Value(v, "park_pos"):0}/{Value(v, "a22x_park"):0} " +
        $"rpmL={Value(v, "wheel_l_rpm"):0.0} rpmR={Value(v, "wheel_r_rpm"):0.0} rpmC={Value(v, "wheel_c_rpm"):0.0} yaw={Value(v, "yaw_rate_dps"):0.00}°/s ax={Value(v, "accel_z_fps2"):0.00} " +
        $"T={Value(v, "brake_l_temp_c"):0}/{Value(v, "brake_r_temp_c"):0}°C hyd={Value(v, "brake_hyd_psi"):0} caut={Value(v, "a22x_caution"):0} warn={Value(v, "a22x_warning"):0} alt={Value(v, "a22x_alt_brake"):0}");

    private static string DescribeWear(IReadOnlyList<double> wear)
    {
        var parts = new List<string>();
        for (var c = 0; c < WearComponents.Length; c++)
        {
            var o = c * WearStates.Length;
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{WearComponents[c].ComponentName}({WearComponents[c].Component}) level={wear[o]:0.###} weak={wear[o + 1]:0} failed={wear[o + 2]:0} weakValue={wear[o + 3]:0.###}"));
        }

        return string.Join(" | ", parts);
    }
}
