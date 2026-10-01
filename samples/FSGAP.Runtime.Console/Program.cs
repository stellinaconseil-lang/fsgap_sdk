// FSGAP application sample: one runtime, the loaded aircraft's normalized session, capabilities-driven features.
// No provider type, no vendor branch, no registration code: the runtime composes the built-in aircraft providers.
//
// Usage: dotnet run --project samples/FSGAP.Runtime.Console [-- --minutes N]
using System.Globalization;
using FSGAP;
using FSGAP.Abstractions;
using FSGAP.Abstractions.Configuration;

var minutes = args.Length > 1 && args[0] == "--minutes" && double.TryParse(args[1], CultureInfo.InvariantCulture, out var m) ? m : 0;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
if (minutes > 0)
{
    stop.CancelAfter(TimeSpan.FromMinutes(minutes));
}

var options = new FsgapRuntimeOptions
{
    Sdk = new FsgapOptions
    {
        ApplicationName = "FSGAP runtime sample",
        DataDirectory = Path.Combine(Path.GetTempPath(), "fsgap-runtime-sample"),
    },
};

await using var runtime = new FsgapRuntime(options);
Print($"built-in providers: {string.Join(", ", runtime.ProviderIds)}");
await runtime.StartAsync(stop.Token);

var connection = Task.Run(async () =>
{
    await foreach (var status in runtime.WatchConnectionAsync(stop.Token))
    {
        Print($"simulator: {status.State}{(status.Detail is null ? string.Empty : $" ({status.Detail})")}");
    }
});

try
{
    await foreach (var state in runtime.WatchSessionAsync(stop.Token))
    {
        Print($"aircraft: {state.Aircraft?.Title ?? "none"} -> {state.Status}{(state.Detail is null ? string.Empty : $" ({state.Detail})")}");
        if (state.Session is { } session)
        {
            await DescribeAsync(session);
        }
    }
}
catch (OperationCanceledException)
{
    // Stopping.
}

await connection.ContinueWith(_ => { }, TaskScheduler.Default);
Print("stopped");

static async Task DescribeAsync(IAircraftSession session)
{
    var id = session.Identity;
    Print($"  identity: {id.Manufacturer} {id.Model} ({id.IcaoType}) registration={id.Registration ?? "unknown"} livery={id.Livery}");

    var failures = session.Capabilities.Failures;
    var executable = failures.Catalog.Count(d => failures.CanTrigger(d.Key));
    Print($"  failures: {failures.Catalog.Count} keys, {executable} executable, read active={failures.CanReadActiveFailures}");

    var degradations = session.Capabilities.Degradations;
    Print($"  controlled degradations: {degradations.Catalog.Count} (max active {degradations.MaxActive})");

    var telemetry = await session.Telemetry.GetSnapshotAsync();
    var ias = telemetry.Flight.IndicatedAirspeedKnots.TryGetValue(out var knots) ? $"{knots:0} kt" : "unavailable";
    Print($"  telemetry: IAS {ias}, {telemetry.Engines.Count} engines");
}

static void Print(string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}");
