// BLOCK 10A read-only discovery harness for the Synaptic Simulations A220-300 (docs/audits/synaptic-a220-discovery.md).
//
// This is NOT an aircraft provider and NOT a telemetry overlay. It lives in the sample only, is never packaged, and
// exists so that a future live session with the A220 loaded can capture the evidence BLOCK 10A could not (the aircraft
// was installed but not loaded during the audit). It:
//   - prints the verdict of every candidate detection rule for whatever aircraft MSFS reports (negatives included);
//   - when a candidate rule fires, reads a small subset of the OFFICIALLY DOCUMENTED Synaptic variables
//     (docs.synapticsim.com/pilots/simvars) and a few stock SimVars FSGAP does not read yet, every 5 s, through the
//     transport's single connection (ISimulatorVariableReader). Reads only. No LVAR write, no H-Event, no K-Event;
//   - optionally writes one sanitized JSON fixture (descriptor, generic AircraftTelemetry, variable values) for BLOCK 10B.
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

    private static readonly JsonSerializerOptions FixtureJson = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new TelemetryValueConverterFactory() },
    };

    internal sealed record CandidateRule(string Id, string Grade, string Description, Func<AircraftDescriptor, bool> Test);

    internal sealed record RuleVerdict(string Id, string Grade, string Description, bool Fires);

    /// <summary>
    /// Candidate detection rules under evaluation (grades are the audit's, see the discovery document). None of them
    /// is a production rule: the real TITLE of the Synaptic A220 has not been observed live yet.
    /// </summary>
    internal static IReadOnlyList<CandidateRule> CandidateRules { get; } =
    [
        new("R1", "PLAUSIBLE", "TITLE contains 'Synaptic'", a => Has(a.Title, "Synaptic")),
        new("R2", "PLAUSIBLE", "TITLE contains 'A220' or 'A223', and none of 'Fenix', 'Asobo', 'FSLTL'", a =>
            (Has(a.Title, "A220") || Has(a.Title, "A223")) && !Has(a.Title, "Fenix") && !Has(a.Title, "Asobo") && !Has(a.Title, "FSLTL")),
        new("R3", "WEAK", "TITLE or LIVERY FOLDER contains 'A22X'", a => Has(a.Title, "A22X") || Has(a.LiveryFolder, "A22X")),
        new("R4", "WEAK", "TITLE contains 'iniBuilds' and 'A220'", a => Has(a.Title, "iniBuilds") && Has(a.Title, "A220")),
        new("R5", "REJECT", "TITLE contains 'Airbus' (control: fires on other Airbus add-ons too)", a => Has(a.Title, "Airbus")),
    ];

    /// <summary>
    /// Officially documented Synaptic variables read by the probe (unit "number": enums and booleans come back as their
    /// raw value). Selected for the systems BLOCK 10A audits: fuel boost pumps, APU, bleeds, packs, crossbleed,
    /// hydraulic selectors, engine fire pushbuttons, anti-ice, master caution/warning, generators, flap lever, brakes.
    /// </summary>
    internal static IReadOnlyList<SimulatorVariable> DocumentedSubset { get; } =
    [
        Local("L:A22X L Boost Pump"), Local("L:A22X R Boost Pump"),
        Local("L:A22X APU Switch"), Local("L:A22X APU Gen Off"), Local("L:A22X APU Gen Fail Lamp"),
        Local("L:A22X APU Bleed Off"), Local("L:A22X APU Bleed Fail Lamp"),
        Local("L:A22X L Bleed Off"), Local("L:A22X R Bleed Off"), Local("L:A22X L Bleed Fail Lamp"), Local("L:A22X R Bleed Fail Lamp"),
        Local("L:A22X Crossbleed"),
        Local("L:A22X L Pack Off"), Local("L:A22X R Pack Off"), Local("L:A22X L Pack Fail Lamp"), Local("L:A22X R Pack Fail Lamp"),
        Local("L:A22X PTU"), Local("L:A22X ACMP 2B"), Local("L:A22X ACMP 3A"), Local("L:A22X ACMP 3B"),
        Local("L:A22X Hyd 1 SOV"), Local("L:A22X Hyd 2 SOV"),
        Local("L:A22X L Eng Fire"), Local("L:A22X R Eng Fire"),
        Local("L:A22X L Cowl Anti Ice"), Local("L:A22X R Cowl Anti Ice"), Local("L:A22X Wing Anti Ice"),
        Local("L:A22X Caution PBA"), Local("L:A22X Warning PBA"),
        Local("L:A22X L Gen Off"), Local("L:A22X R Gen Off"), Local("L:A22X L Gen Fail Lamp"), Local("L:A22X R Gen Fail Lamp"),
        Local("L:A22X RAT Gen Lamp"), Local("L:A22X Bus Isolation Mode"),
        Local("L:A22X Flap Lever"), Local("L:A22X Parking Brake"), Local("L:A22X Autobrake"),
        Local("L:A22X Lamp Test"), Local("L:A22X Flight Stage"),
    ];

    /// <summary>
    /// Stock MSFS SimVars FSGAP does not read yet, whose A220 behaviour is unknown. Small independent groups, because one
    /// unsupported name fails its whole request (see TelemetryGroups.cs).
    /// </summary>
    internal static IReadOnlyList<(string Name, IReadOnlyList<SimulatorVariable> Variables)> StockGroups { get; } =
    [
        ("stock.config", [new("FLAPS HANDLE INDEX", "Number"), new("BRAKE PARKING POSITION", "Bool"), new("SPOILERS HANDLE POSITION", "Percent"), new("LEADING EDGE FLAPS LEFT PERCENT", "Percent")]),
        ("stock.apu", [new("APU PCT RPM", "Percent"), new("APU SWITCH", "Bool"), new("APU GENERATOR SWITCH:1", "Bool"), new("APU GENERATOR ACTIVE:1", "Bool")]),
        ("stock.hyd-elec", [new("HYDRAULIC PRESSURE:1", "Psi"), new("HYDRAULIC PRESSURE:2", "Psi"), new("HYDRAULIC PRESSURE:3", "Psi"), new("ELECTRICAL BATTERY VOLTAGE:1", "Volts"), new("ELECTRICAL BATTERY VOLTAGE:2", "Volts"), new("ELECTRICAL MAIN BUS VOLTAGE:1", "Volts")]),
        ("stock.bleed-ice", [new("BLEED AIR ENGINE:1", "Bool"), new("BLEED AIR ENGINE:2", "Bool"), new("ENG ANTI ICE:1", "Bool"), new("ENG ANTI ICE:2", "Bool"), new("STRUCTURAL DEICE SWITCH", "Bool"), new("PITOT HEAT", "Bool")]),
    ];

    internal static IReadOnlyList<RuleVerdict> EvaluateCandidateRules(AircraftDescriptor aircraft) =>
        CandidateRules.Select(r => new RuleVerdict(r.Id, r.Grade, r.Description, r.Test(aircraft))).ToArray();

    /// <summary>Whether any non-rejected candidate rule fires: the gate for reading a Synaptic variable at all.</summary>
    internal static bool LooksLikeA220(AircraftDescriptor aircraft) =>
        CandidateRules.Any(r => r.Grade != "REJECT" && r.Test(aircraft));

    /// <summary>
    /// Probe loop: every 5 s, read the documented subset and the stock groups, print them, and write the fixture once
    /// (first complete read) when <paramref name="fixtureDirectory"/> is given. Stops on cancellation.
    /// </summary>
    internal static async Task RunAsync(
        SimConnectSimulator simulator,
        AircraftDescriptor aircraft,
        string? fixtureDirectory,
        Action<string, string> print,
        CancellationToken cancellationToken)
    {
        ISimulatorVariableReader reader = simulator;
        var fixtureWritten = fixtureDirectory is null;
        var failing = new HashSet<string>();
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

                if (!fixtureWritten && documented is not null)
                {
                    var telemetry = await simulator.Telemetry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    var path = WriteFixture(fixtureDirectory!, aircraft, telemetry, documented, stock);
                    print("synaptic", $"fixture written: {Path.GetFileName(path)}");
                    fixtureWritten = true;
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
    /// Sanitized fixture: aircraft descriptor as MSFS reports it, the generic FSGAP snapshot with every value's state,
    /// and the raw variable values. No file path, no machine name, no user data.
    /// </summary>
    private static string WriteFixture(
        string directory,
        AircraftDescriptor aircraft,
        AircraftTelemetry telemetry,
        IReadOnlyDictionary<string, double> documented,
        IReadOnlyDictionary<string, double> stock)
    {
        Directory.CreateDirectory(directory);
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(directory, $"synaptic-a220-discovery-{now:yyyyMMdd-HHmmss}.json");
        var fixture = new
        {
            _comment = "BLOCK 10A discovery capture (read-only). descriptor = TITLE / ATC ID / LIVERY FOLDER / LIVERY NAME as MSFS reports them; genericTelemetry = FSGAP 0.9.0 generic snapshot (state + value); synapticVariables = documented L:A22X variables read as 'number'; stockSimVars = stock SimVars FSGAP does not read yet. Values are raw and unvalidated.",
            capturedAtUtc = now,
            descriptor = new { aircraft.Title, aircraft.Registration, aircraft.LiveryFolder, aircraft.Livery },
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
