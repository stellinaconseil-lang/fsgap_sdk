// FSGAP live validation sample.
//
// Scans the installed Fenix liveries, starts the FSGAP simulator transport, and prints connection status changes,
// pause/crash state, the raw aircraft descriptor reported by MSFS, what the Fenix provider makes of it (match,
// normalized identity, catalog match) and, every two seconds, a compact view of the normalized telemetry: the Fenix
// session's (generic telemetry with the Fenix policy applied) when a Fenix is loaded, the generic telemetry
// otherwise, followed for a Fenix by its systems (IRS, fuel pumps, fire panel, hydraulics). Read-only by default;
// the only write is the explicit --failure-roundtrip option (a trigger always followed by its clear).
//
// Usage: dotnet run --project samples/FSGAP.SimConnect.Console [-- --minutes N] [--failures] [--failure-roundtrip <key>] [--nearest-airport]
using System.Diagnostics;
using System.Globalization;
using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Geography;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix;
using FSGAP.SimConnect;
using FSGAP.SimConnect.Console;

// Options: --minutes N (auto-stop), --failures (read-only: EFB reachability and active failures, once per Fenix
// session), --failure-roundtrip <failure-key> (the only write: trigger, read back, clear, read back; see
// docs/fenix-failures.md, live validation).
static string? Arg(string[] args, string name) =>
    Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;

var runFor = double.TryParse(Arg(args, "--minutes"), CultureInfo.InvariantCulture, out var minutes)
    ? TimeSpan.FromMinutes(minutes)
    : Timeout.InfiniteTimeSpan;
var readFailures = args.Contains("--failures") || args.Contains("--failure-roundtrip");
var roundtripKey = Arg(args, "--failure-roundtrip") is { } keyText ? FailureKey.Parse(keyText) : null;
var airportAt = Arg(args, "--airport-at")?.Split(",") is [var atLat, var atLon]
    ? new GeoPosition(double.Parse(atLat, CultureInfo.InvariantCulture), double.Parse(atLon, CultureInfo.InvariantCulture))
    : (GeoPosition?)null;
var nearestAirport = args.Contains("--nearest-airport") || airportAt is not null;

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
if (runFor != Timeout.InfiniteTimeSpan)
{
    stop.CancelAfter(runFor);
}

var options = new FsgapOptions
{
    ApplicationName = "FSGAP live sample",
    DataDirectory = Path.Combine(Path.GetTempPath(), "fsgap-live-sample"),
};

var catalog = new FenixInstalledAircraftCatalog(options, logger: new ConsoleLogger<FenixInstalledAircraftCatalog>());
var scanClock = Stopwatch.StartNew();
var scan = await catalog.RefreshAsync(cancellationToken: stop.Token);
Print("catalog", $"{scan.AircraftCount} Fenix liveries in {scanClock.ElapsedMilliseconds} ms, {scan.Errors.Count} errors");
foreach (var error in scan.Errors)
{
    Print("catalog", $"  error: {error}");
}

await using var simulator = new SimConnectSimulator(options, new ConsoleLogger<SimConnectSimulator>());

// The Fenix provider composes on the transport: generic telemetry, Fenix variables read through the transport, and
// the transport's aircraft detector. One native connection for everything.
var provider = new FenixAircraftProvider(
    catalog,
    logger: new ConsoleLogger<FenixAircraftProvider>(),
    genericTelemetry: simulator.Telemetry,
    simulatorVariables: simulator,
    aircraftDetector: simulator.AircraftDetector,
    telemetryOptions: options.Telemetry,
    fenixOptions: readFailures ? new FenixOptions() : null);
IAircraftSession? session = null;
(string Source, ITelemetryProvider Provider) shown = ("generic", simulator.Telemetry);

Print("sample", $"starting (Ctrl+C to stop{(runFor == Timeout.InfiniteTimeSpan ? string.Empty : $", auto-stop after {runFor}")})");
await simulator.StartAsync();

var watchers = new[]
{
    Watch(simulator.WatchStatusAsync(stop.Token), s => Print("status", $"{s.State}{(s.Detail is null ? string.Empty : $" ({s.Detail})")}")),
    Watch(simulator.State.WatchAsync(stop.Token), s => Print("state", $"paused={s.Paused} crashes={s.CrashCount} lastCrash={s.LastCrashAt:O}")),
    Watch(simulator.AircraftDetector.WatchAsync(stop.Token), a => DescribeAsync(a).GetAwaiter().GetResult()),
    PrintTelemetryAsync(stop.Token),
    nearestAirport ? PrintNearestAirportAsync(stop.Token) : Task.CompletedTask,
};

await Task.WhenAll(watchers);
Print("sample", $"stopping after {simulator.SessionElapsed:hh\\:mm\\:ss}");
if (session is not null)
{
    await session.DisposeAsync();
}

await simulator.StopAsync();
Print("sample", $"final status: {simulator.Status.State}");

async Task DescribeAsync(AircraftDescriptor? aircraft)
{
    if (session is not null)
    {
        await session.DisposeAsync();
        session = null;
    }

    shown = ("generic", simulator.Telemetry);
    if (aircraft is null)
    {
        Print("aircraft", "none");
        return;
    }

    Print("msfs", $"Title='{aircraft.Title}' AtcId='{aircraft.Registration}' LiveryFolder='{aircraft.LiveryFolder}' Livery='{aircraft.Livery}'");
    var match = provider.Match(aircraft);
    if (!match.IsSupported)
    {
        Print("fenix", "Match: not a Fenix aircraft");
        return;
    }

    var installed = aircraft.LiveryFolder is null ? null : await catalog.FindByLiveryFolderAsync(aircraft.LiveryFolder);
    session = await provider.AttachAsync(aircraft);
    shown = ("fenix", session.Telemetry);
    var id = session.Identity;
    var declared = session.Capabilities.Telemetry;
    Print("fenix", $"Match={match.Specificity} Developer='{id.Developer}' Manufacturer='{id.Manufacturer}' Family='{id.Family}' Model='{id.Model}' IcaoType='{id.IcaoType}'");
    Print("fenix", $"Engine='{id.EngineVariant}' Wingtip='{id.WingtipConfiguration}' Registration='{id.Registration}' Operator='{id.OperatorIcao}' Livery='{id.Livery}'");
    Print("fenix", installed is null
        ? "Catalog: no installed livery matches this livery folder"
        : $"Catalog: {installed.PackageName}/{installed.LiveryFolder} (registration '{installed.Identity.Registration}', tags model={installed.Identity.Model} engine={installed.Identity.EngineVariant} wingtip={installed.Identity.WingtipConfiguration})");
    Print("fenix", $"Telemetry sections: flight={declared.FlightState} warnings={declared.Warnings} engines={declared.Engines} gear={declared.LandingGear} controls={declared.FlightControls} apu={declared.Apu} irs={declared.InertialReferences} fuelPumps={declared.FuelPumps} elec={declared.Electrical} hyd={declared.Hydraulics} fire={declared.Fire}");
    if (readFailures)
    {
        var failureSession = session;
        _ = Task.Run(() => CheckFailuresAsync(failureSession));
    }
}

async Task CheckFailuresAsync(IAircraftSession fenix)
{
    var capabilities = fenix.Capabilities.Failures;
    Print("failures", $"catalog={capabilities.Catalog.Count} keys, readActive={capabilities.CanReadActiveFailures}");
    var before = await ReadActiveAsync(fenix, "before");
    if (roundtripKey is null || before is null)
    {
        return;
    }

    if (!capabilities.Catalog.TryGet(roundtripKey, out var definition))
    {
        Print("failures", $"round trip refused: '{roundtripKey}' is not in the catalog");
        return;
    }

    if (before.Any(f => f.Key == roundtripKey))
    {
        Print("failures", $"round trip refused: '{roundtripKey}' is already active, it is left untouched");
        return;
    }

    var command = new FailureCommand(roundtripKey, definition.SupportedTargets[0]);
    var triggered = false;
    try
    {
        var trigger = await fenix.Failures.TriggerAsync(command);
        triggered = trigger.Status is not (FailureCommandStatus.NotSupported or FailureCommandStatus.Unavailable);
        Print("failures", $"TRIGGER {roundtripKey} ({definition.DisplayName}) -> {trigger.Status}{(trigger.Message is null ? string.Empty : $" ({trigger.Message})")}");
        await ReadActiveAsync(fenix, "after trigger");
        await Task.Delay(RoundtripHold); // a realistic pace: the failure stays active for a few seconds
    }
    finally
    {
        if (triggered)
        {
            var clear = await fenix.Failures.ClearAsync(command);
            Print("failures", $"CLEAR {roundtripKey} -> {clear.Status}{(clear.Message is null ? string.Empty : $" ({clear.Message})")}");
            await ReadActiveAsync(fenix, "right after clear");

            // The EFB list switches at once but Fenix applies the state asynchronously: a clear sent ~30 ms after its
            // trigger was seen overwritten by the late trigger. The final verdict is read after a settle delay.
            await Task.Delay(RoundtripSettle);
            var after = await ReadActiveAsync(fenix, $"{RoundtripSettle.TotalSeconds:0} s after clear");
            Print("failures", after is null
                ? "final state unknown (read failed)"
                : $"'{roundtripKey}' still active after the test: {after.Any(f => f.Key == roundtripKey)}; active failures now {after.Count} (before {before.Count})");
        }
    }
}

async Task<IReadOnlyCollection<AircraftFailure>?> ReadActiveAsync(IAircraftSession fenix, string when)
{
    try
    {
        var active = await fenix.Failures.GetActiveFailuresAsync();
        Print("failures", $"active {when}: {active.Count}{(active.Count == 0 ? string.Empty : " -> " + string.Join(", ", active.Select(f => f.Key?.Value ?? $"(unclassified: {f.Description})")))}");
        return active;
    }
    catch (FailuresUnavailableException ex)
    {
        Print("failures", $"active {when}: unavailable ({ex.Message})");
        return null;
    }
}

async Task PrintNearestAirportAsync(CancellationToken cancellationToken)
{
    // Read-only: the aircraft's position from the generic telemetry, then the airport service on the same connection.
    try
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            var flight = (await simulator.Telemetry.GetSnapshotAsync(cancellationToken)).Flight;
            var position = airportAt
                ?? (flight.LatitudeDegrees.TryGetValue(out var lat) && flight.LongitudeDegrees.TryGetValue(out var lon)
                    ? GeoPosition.TryFrom(lat, lon)
                    : null);
            if (position is not { } from)
            {
                Print("airport", "no known aircraft position yet");
                continue;
            }

            try
            {
                var clock = Stopwatch.StartNew();
                var nearby = await simulator.FindNearbyAirportsAsync(from, new AirportSearchOptions { MaxResults = 3 }, cancellationToken);
                Print("airport", nearby.Count == 0
                    ? $"no airport within {AirportSearchOptions.DefaultMaxDistanceNauticalMiles} NM of {from} ({clock.ElapsedMilliseconds} ms)"
                    : $"nearest to {from} ({clock.ElapsedMilliseconds} ms): " + string.Join(" | ", nearby.Select(a =>
                        string.Create(CultureInfo.InvariantCulture, $"{a.Icao} {a.DistanceNauticalMiles:F1} NM (region {a.Region ?? "-"}, elev {(a.ElevationFeet is { } ft ? $"{ft:F0} ft" : "n/a")}, {a.Position})"))));
            }
            catch (SimulatorServiceException ex)
            {
                Print("airport", $"unavailable: {ex.Error} ({ex.Message})");
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C or auto-stop.
    }
}

async Task PrintTelemetryAsync(CancellationToken cancellationToken)
{
    try
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            var (source, telemetry) = shown;
            var t = await telemetry.GetSnapshotAsync(cancellationToken);
            var generic = source == "generic" ? t : await simulator.Telemetry.GetSnapshotAsync(cancellationToken);
            var f = t.Flight;
            var w = t.Warnings;
            var g = t.LandingGear;
            var c = t.FlightControls;
            Print($"t.{source}", $"GND={B(f.OnGround)} LAT={D(f.LatitudeDegrees, "F5")} LON={D(f.LongitudeDegrees, "F5")} ALT={D(f.AltitudeFeet, "F0")} AGL={D(f.HeightAboveGroundFeet, "F0")} RA={D(f.RadioAltitudeFeet, "F0")} IAS={D(f.IndicatedAirspeedKnots, "F0")} GS={D(f.GroundSpeedKnots, "F0")} VS={D(f.VerticalSpeedFeetPerMinute, "F0")} TD={D(f.TouchdownVerticalSpeedFeetPerMinute, "F0")}");
            Print($"t.{source}", $"HDG={D(f.HeadingMagneticDegrees, "F0")} PITCH={D(f.PitchDegrees, "+0.0;-0.0;0.0")} BANK={D(f.BankDegrees, "+0.0;-0.0;0.0")} G={D(f.GLoad, "F2")} WARN ovs={B(w.Overspeed)} flap={B(w.FlapSpeedExceeded)} gear={B(w.GearSpeedExceeded)} stall={B(w.Stall)}");
            Print($"t.{source}", t.Engines.Count == 0 ? "ENG n/a" : string.Join(" | ", t.Engines.Select(e => $"ENG{e.Index} run={B(e.Running)} N1={D(e.N1Percent, "F1")} N2={D(e.N2Percent, "F1")} EGT={D(e.EgtCelsius, "F0")} FF={D(e.FuelFlowKilogramsPerHour, "F0")}kg/h fire={B(e.FireDetected)}")));
            Print($"t.{source}", $"GEAR handle={B(g.HandleDown)} {string.Join(' ', g.Units.Select(u => $"{u.Id}={D(u.ExtensionPercent, "F0")}"))} FLAPS handle={D(c.FlapsHandlePercent, "F0")} {string.Join(' ', c.FlapSurfaces.Select(s => $"{s.Id}={D(s.ExtensionPercent, "F0")}"))} SPDBRK={D(c.SpeedBrakeDeploymentPercent, "F0")} (generic {D(generic.FlightControls.SpeedBrakeDeploymentPercent, "F0")})");
            if (source == "fenix")
            {
                // Fenix systems: only what the session supports, in normalized terms (never variable names).
                Print("fenix.sys", $"IRS {Join(t.InertialReferences.Select(i => $"IR{i.Index}={Mode(i.Mode)}"))}   PUMPS {Join(t.FuelPumps.Select(p => $"{p.Id}={OnOff(p.IsOn)}"))}");
                Print("fenix.sys", $"FIRE {Join(t.Engines.Select(e => $"ENG{e.Index} handle={Handle(e.FireHandlePulled)} warning={OnOff(e.FireWarningLit)}"))}  APU handle={Handle(t.Apu.FireHandlePulled)}   HYD {Join(t.HydraulicSystems.Select(h => $"{h.Id}={(h.PressurePsi.IsKnown ? D(h.PressurePsi, "F0") + " psi" : D(h.PressurePsi, "F0"))}"))}");
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C or auto-stop.
    }
}

static string D(TelemetryValue<double> v, string format) =>
    v.IsKnown ? v.Value.ToString(format, CultureInfo.InvariantCulture) : v.State == ValueState.Unknown ? "unk" : "n/a";

static string Join(IEnumerable<string> parts) => parts.Any() ? string.Join(' ', parts) : "n/a";

static string OnOff(TelemetryValue<bool> v) =>
    v.IsKnown ? (v.Value ? "ON" : "off") : v.State == ValueState.Unknown ? "unk" : "n/a";

static string Handle(TelemetryValue<bool> v) =>
    v.IsKnown ? (v.Value ? "PULLED" : "stowed") : v.State == ValueState.Unknown ? "unk" : "n/a";

static string Mode(TelemetryValue<InertialReferenceMode> v) =>
    v.IsKnown ? v.Value switch { InertialReferenceMode.Navigation => "NAV", InertialReferenceMode.Attitude => "ATT", InertialReferenceMode.Off => "OFF", _ => v.Value.ToString() }
    : v.State == ValueState.Unknown ? "unk" : "n/a";

static string B(TelemetryValue<bool> v) =>
    v.IsKnown ? (v.Value ? "Y" : "N") : v.State == ValueState.Unknown ? "unk" : "n/a";

static async Task Watch<T>(IAsyncEnumerable<T> stream, Action<T> print)
{
    try
    {
        await foreach (var value in stream)
        {
            print(value);
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C or auto-stop.
    }
}

static void Print(string label, string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{label,-8}] {message}");

partial class Program
{
    /// <summary>How long the round-trip failure stays active before it is cleared.</summary>
    private static readonly TimeSpan RoundtripHold = TimeSpan.FromSeconds(5);

    /// <summary>Delay before the final read-back of a round trip.</summary>
    private static readonly TimeSpan RoundtripSettle = TimeSpan.FromSeconds(5);
}
