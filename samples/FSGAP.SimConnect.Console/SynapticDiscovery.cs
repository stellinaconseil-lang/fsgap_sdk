// BLOCK 10A read-only discovery harness for the Synaptic Simulations A220-300 (docs/audits/synaptic-a220-discovery.md).
//
// This is NOT an aircraft provider and NOT a telemetry overlay. It lives in the sample only, is never packaged, and
// exists to capture live evidence. It:
//   - prints the verdict of every candidate detection rule for whatever aircraft MSFS reports (negatives included);
//   - when a candidate rule fires, reads a small subset of the OFFICIALLY DOCUMENTED Synaptic variables
//     (docs.synapticsim.com/pilots/simvars) and a few stock SimVars FSGAP does not read yet, every 5 s, through the
//     transport's single connection (ISimulatorVariableReader). Reads only. No LVAR write, no H-Event, no K-Event;
//   - with --synaptic-fixture, writes one sanitized JSON fixture (descriptor, generic AircraftTelemetry, variable values)
//     per stable aircraft state: a new file whenever the documented values, on-ground flag or running-engine count have
//     changed and held for two consecutive reads (BLOCK 10A-LIVE: one capture per state, no manual trigger needed).
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.SimConnect.Console;

internal static class SynapticDiscovery
{
    /// <summary>Slow on purpose: selector and lamp states hold for seconds, and this is a probe, not a stream.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound on fixtures per run, so a long session cannot fill a disk.</summary>
    private const int MaxFixtures = 60;

    private static readonly JsonSerializerOptions FixtureJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new TelemetryValueConverterFactory() },
    };

    internal sealed record CandidateRule(string Id, string Grade, string Description, Func<AircraftDescriptor, bool> Test);

    internal sealed record RuleVerdict(string Id, string Grade, string Description, bool Fires);

    /// <summary>
    /// Candidate detection rules under evaluation (grades are the audit's, see the discovery document). None of them
    /// is a production rule until the live capture has graded it.
    /// </summary>
    internal static IReadOnlyList<CandidateRule> CandidateRules { get; } =
    [
        new("R1", "PLAUSIBLE", "TITLE contains 'Synaptic'", a => Has(a.Title, "Synaptic")),
        new("R2", "PLAUSIBLE", "TITLE contains 'A220' or 'A223', and none of 'Fenix', 'Asobo', 'FSLTL'", a =>
            (Has(a.Title, "A220") || Has(a.Title, "A223")) && !Has(a.Title, "Fenix") && !Has(a.Title, "Asobo") && !Has(a.Title, "FSLTL")),
        new("R3", "WEAK", "TITLE or LIVERY FOLDER contains 'A22X'", a => Has(a.Title, "A22X") || Has(a.LiveryFolder, "A22X")),
        new("R4", "WEAK", "TITLE contains 'iniBuilds' and 'A220'", a => Has(a.Title, "iniBuilds") && Has(a.Title, "A220")),
        new("R5", "REJECT", "TITLE contains 'Airbus' (control: fires on other Airbus add-ons too)", a => Has(a.Title, "Airbus")),
        new("R6", "CANDIDATE", "ATC MODEL contains 'A223' or 'A220' (stock aircraft.cfg atc_model)", a => Has(a.Model, "A223") || Has(a.Model, "A220")),
        new("R7", "CANDIDATE", "ATC TYPE, TITLE or LIVERY FOLDER contains 'Synaptic'", a => Has(a.Manufacturer, "Synaptic") || Has(a.Title, "Synaptic") || Has(a.LiveryFolder, "Synaptic")),
    ];

    /// <summary>
    /// Officially documented Synaptic variables read by the probe (unit "number": enums and booleans come back as their
    /// raw value). Selected for the systems the audit covers: fuel boost pumps, APU, bleeds, packs, crossbleed,
    /// hydraulic selectors, engine fire pushbuttons, anti-ice, master caution/warning, generators, flap lever, brakes.
    /// </summary>
    internal static IReadOnlyList<SimulatorVariable> DocumentedSubset { get; } =
    [
        Local("L:A22X L Boost Pump"), Local("L:A22X R Boost Pump"),
        Local("L:A22X APU Switch"), Local("L:A22X APU Gen Off"), Local("L:A22X APU Gen Fail Lamp"), Local("L:A22X APU Gen Off Lamp"),
        Local("L:A22X APU Bleed Off"), Local("L:A22X APU Bleed Fail Lamp"), Local("L:A22X APU Bleed Off Lamp"),
        Local("L:A22X L Bleed Off"), Local("L:A22X R Bleed Off"), Local("L:A22X L Bleed Fail Lamp"), Local("L:A22X R Bleed Fail Lamp"),
        Local("L:A22X L Bleed Off Lamp"), Local("L:A22X R Bleed Off Lamp"),
        Local("L:A22X Crossbleed"),
        Local("L:A22X L Pack Off"), Local("L:A22X R Pack Off"), Local("L:A22X L Pack Fail Lamp"), Local("L:A22X R Pack Fail Lamp"),
        Local("L:A22X L Pack Off Lamp"), Local("L:A22X R Pack Off Lamp"),
        Local("L:A22X PTU"), Local("L:A22X ACMP 2B"), Local("L:A22X ACMP 3A"), Local("L:A22X ACMP 3B"),
        Local("L:A22X Hyd 1 SOV"), Local("L:A22X Hyd 2 SOV"), Local("L:A22X Hyd 1 SOV Lamp"), Local("L:A22X Hyd 2 SOV Lamp"),
        Local("L:A22X L Eng Fire"), Local("L:A22X R Eng Fire"),
        Local("L:A22X L Cowl Anti Ice"), Local("L:A22X R Cowl Anti Ice"), Local("L:A22X Wing Anti Ice"), Local("L:A22X Probe Heat"),
        Local("L:A22X Caution PBA"), Local("L:A22X Warning PBA"),
        Local("L:A22X L Gen Off"), Local("L:A22X R Gen Off"), Local("L:A22X L Gen Fail Lamp"), Local("L:A22X R Gen Fail Lamp"),
        Local("L:A22X L Gen Off Lamp"), Local("L:A22X R Gen Off Lamp"),
        Local("L:A22X RAT Gen"), Local("L:A22X RAT Gen Lamp"), Local("L:A22X Bus Isolation Mode"), Local("L:A22X Cabin Power Off"),
        Local("L:A22X Flap Lever"), Local("L:A22X Parking Brake"), Local("L:A22X Autobrake"), Local("L:A22X Alternate Brake"),
        Local("L:A22X Nose Steer Off"), Local("L:A22X L Tiller"),
        Local("L:A22X Lamp Test"), Local("L:A22X Flight Stage"), Local("L:INI_GPU_AVAIL"),
    ];

    /// <summary>
    /// Stock MSFS SimVars FSGAP does not read yet, whose A220 behaviour is unknown. Small independent groups, because one
    /// unsupported name fails its whole request (see TelemetryGroups.cs).
    /// </summary>
    internal static IReadOnlyList<(string Name, IReadOnlyList<SimulatorVariable> Variables)> StockGroups { get; } =
    [
        ("stock.config", [new("FLAPS HANDLE INDEX", "Number"), new("FLAPS NUM HANDLE POSITIONS", "Number"), new("BRAKE PARKING POSITION", "Bool"), new("SPOILERS HANDLE POSITION", "Percent"), new("SPOILERS ARMED", "Bool"), new("LEADING EDGE FLAPS LEFT PERCENT", "Percent"), new("LEADING EDGE FLAPS RIGHT PERCENT", "Percent")]),
        ("stock.apu", [new("APU PCT RPM", "Percent"), new("APU SWITCH", "Bool"), new("APU GENERATOR SWITCH:1", "Bool"), new("APU GENERATOR ACTIVE:1", "Bool"), new("APU BLEED PRESSURE RECEIVED BY ENGINE:1", "Psi")]),
        ("stock.hyd", [new("HYDRAULIC PRESSURE:1", "Psi"), new("HYDRAULIC PRESSURE:2", "Psi"), new("HYDRAULIC PRESSURE:3", "Psi"), new("HYDRAULIC RESERVOIR PERCENT:1", "Percent"), new("HYDRAULIC RESERVOIR PERCENT:2", "Percent"), new("HYDRAULIC RESERVOIR PERCENT:3", "Percent")]),
        ("stock.elec", [new("ELECTRICAL BATTERY VOLTAGE:1", "Volts"), new("ELECTRICAL BATTERY VOLTAGE:2", "Volts"), new("ELECTRICAL MAIN BUS VOLTAGE:1", "Volts"), new("ELECTRICAL MAIN BUS VOLTAGE:2", "Volts"), new("ELECTRICAL MASTER BATTERY:1", "Bool"), new("GENERAL ENG GENERATOR ACTIVE:1", "Bool"), new("GENERAL ENG GENERATOR ACTIVE:2", "Bool"), new("EXTERNAL POWER ON:1", "Bool")]),
        ("stock.bleed-ice", [new("BLEED AIR ENGINE:1", "Bool"), new("BLEED AIR ENGINE:2", "Bool"), new("ENG ANTI ICE:1", "Bool"), new("ENG ANTI ICE:2", "Bool"), new("STRUCTURAL DEICE SWITCH", "Bool"), new("PITOT HEAT", "Bool")]),
        ("stock.ra", [new("RADIO HEIGHT", "Feet")]),
    ];

    internal static IReadOnlyList<RuleVerdict> EvaluateCandidateRules(AircraftDescriptor aircraft) =>
        CandidateRules.Select(r => new RuleVerdict(r.Id, r.Grade, r.Description, r.Test(aircraft))).ToArray();

    /// <summary>Whether any non-rejected candidate rule fires: the gate for reading a Synaptic variable at all.</summary>
    internal static bool LooksLikeA220(AircraftDescriptor aircraft) =>
        CandidateRules.Any(r => r.Grade != "REJECT" && r.Test(aircraft));

    /// <summary>
    /// Probe loop: every 5 s, read the documented subset and the stock groups, print them, and (with a fixture
    /// directory) write one fixture per stable aircraft state. Stops on cancellation.
    /// </summary>
    internal static async Task RunAsync(
        SimConnectSimulator simulator,
        AircraftDescriptor aircraft,
        string? fixtureDirectory,
        Action<string, string> print,
        CancellationToken cancellationToken)
    {
        ISimulatorVariableReader reader = simulator;
        var failing = new HashSet<string>();
        string? lastWrittenKey = null;
        string? pendingKey = null;
        var written = 0;
        print("synaptic", $"probe started: {DocumentedSubset.Count} documented variables + {StockGroups.Sum(g => g.Variables.Count)} stock SimVars every {Interval.TotalSeconds:0} s, read-only");
        try
        {
            while (true)
            {
                var documented = await ReadGroupAsync(reader, "documented", DocumentedSubset, failing, print, cancellationToken).ConfigureAwait(false);
                var stock = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var (name, variables) in StockGroups)
                {
                    var values = await ReadGroupAsync(reader, name, variables, failing, print, cancellationToken).ConfigureAwait(false);
                    if (values is not null)
                    {
                        foreach (var pair in values)
                        {
                            stock[pair.Key] = pair.Value;
                        }
                    }
                }

                if (fixtureDirectory is not null && documented is not null && written < MaxFixtures)
                {
                    var telemetry = await simulator.Telemetry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    var (key, label) = StateOf(documented, telemetry);
                    if (key != lastWrittenKey)
                    {
                        if (key == pendingKey)
                        {
                            var path = WriteFixture(fixtureDirectory, label, aircraft, telemetry, documented, stock);
                            written++;
                            lastWrittenKey = key;
                            pendingKey = null;
                            print("synaptic", $"fixture {written} written ({label}): {Path.GetFileName(path)}");
                        }
                        else
                        {
                            pendingKey = key;
                        }
                    }
                }

                await Task.Delay(Interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Aircraft changed, or the sample is stopping.
        }
    }

    private static async Task<Dictionary<string, double>?> ReadGroupAsync(
        ISimulatorVariableReader reader,
        string group,
        IReadOnlyList<SimulatorVariable> variables,
        HashSet<string> failing,
        Action<string, string> print,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await reader.ReadAsync(variables, cancellationToken).ConfigureAwait(false);
            if (failing.Remove(group))
            {
                print("synaptic", $"{group}: reads recovered");
            }

            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < variables.Count; i++)
            {
                values[variables[i].Name] = raw[i];
            }

            print("synaptic", $"{group}: " + string.Join(" ", values.Select(v => $"{Short(v.Key)}={v.Value.ToString("0.###", CultureInfo.InvariantCulture)}")));
            return values;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (failing.Add(group))
            {
                print("synaptic", $"{group}: read failed ({ex.GetType().Name}: {ex.Message}); retrying every {Interval.TotalSeconds:0} s");
            }

            return null;
        }
    }

    /// <summary>
    /// A state key (every documented value, on-ground, engines running) and a short label for the fixture name.
    /// </summary>
    private static (string Key, string Label) StateOf(IReadOnlyDictionary<string, double> documented, AircraftTelemetry telemetry)
    {
        var onGround = telemetry.Flight.OnGround.IsKnown ? (telemetry.Flight.OnGround.Value ? "gnd" : "air") : "unk";
        var running = telemetry.Engines.Count(e => e.Running.GetValueOrDefault(false));
        var apu = documented.TryGetValue("L:A22X APU Switch", out var apuSwitch) ? apuSwitch.ToString("0", CultureInfo.InvariantCulture) : "x";
        var key = string.Join(";", documented.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value.ToString("0.###", CultureInfo.InvariantCulture)}"))
            + $";gnd={onGround};eng={running}";
        return (key, $"{onGround}-eng{running}-apu{apu}");
    }

    /// <summary>
    /// Sanitized fixture: aircraft descriptor as MSFS reports it, the generic FSGAP snapshot with every value's state,
    /// and the raw variable values. No file path, no machine name, no user data.
    /// </summary>
    private static string WriteFixture(
        string directory,
        string label,
        AircraftDescriptor aircraft,
        AircraftTelemetry telemetry,
        IReadOnlyDictionary<string, double> documented,
        IReadOnlyDictionary<string, double> stock)
    {
        Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(directory, $"synaptic-a220-discovery-{now:yyyyMMdd-HHmmss}-{label}.json");
        var fixture = new
        {
            _comment = "BLOCK 10A-LIVE discovery capture (read-only). descriptor = TITLE / ATC ID / LIVERY FOLDER / LIVERY NAME / ATC MODEL / ATC TYPE as MSFS reports them; genericTelemetry = FSGAP 0.9.0 generic snapshot (state + value); synapticVariables = documented L:A22X variables read as 'number'; stockSimVars = stock SimVars FSGAP does not read yet. Values are raw and unvalidated.",
            capturedAtUtc = now,
            state = label,
            descriptor = new { aircraft.Title, aircraft.Registration, aircraft.LiveryFolder, aircraft.Livery, AtcModel = aircraft.Model, AtcType = aircraft.Manufacturer },
            genericTelemetry = telemetry,
            synapticVariables = documented.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
            stockSimVars = stock.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(fixture, FixtureJson));
        return path;
    }

    private static bool Has(string? text, string token) => text is not null && text.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static SimulatorVariable Local(string name) => new(name, "number");

    private static string Short(string name) => name.StartsWith("L:A22X ", StringComparison.Ordinal) ? name[7..] : name;

    /// <summary>Writes a <see cref="TelemetryValue{T}"/> as {state, value?} so an unknown value never throws.</summary>
    private sealed class TelemetryValueConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(TelemetryValue<>);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;

        private sealed class Converter<T> : JsonConverter<TelemetryValue<T>>
            where T : struct
        {
            public override TelemetryValue<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                throw new NotSupportedException("Fixtures are written, never read back here.");

            public override void Write(Utf8JsonWriter writer, TelemetryValue<T> value, JsonSerializerOptions options)
            {
                writer.WriteStartObject();
                writer.WriteString("state", value.State.ToString());
                if (value.IsKnown)
                {
                    writer.WritePropertyName("value");
                    JsonSerializer.Serialize(writer, value.Value, options);
                    writer.WriteString("observedAt", value.ObservedAt!.Value);
                }

                writer.WriteEndObject();
            }
        }
    }
}
