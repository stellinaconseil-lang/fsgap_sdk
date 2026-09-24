// FSGAP live validation sample.
//
// Scans the installed Fenix liveries, starts the FSGAP simulator transport, and prints connection status changes,
// pause/crash state, the raw aircraft descriptor reported by MSFS, and what the Fenix provider makes of it
// (match, normalized identity, catalog match). No telemetry, no failure injection.
//
// Usage: dotnet run --project samples/FSGAP.SimConnect.Console [-- --minutes N]
using System.Diagnostics;
using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Fenix;
using FSGAP.SimConnect;
using FSGAP.SimConnect.Console;

var runFor = args.Length == 2 && args[0] == "--minutes" && double.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out var minutes)
    ? TimeSpan.FromMinutes(minutes)
    : Timeout.InfiniteTimeSpan;

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

var provider = new FenixAircraftProvider(catalog, logger: new ConsoleLogger<FenixAircraftProvider>());

await using var simulator = new SimConnectSimulator(options, new ConsoleLogger<SimConnectSimulator>());
Print("sample", $"starting (Ctrl+C to stop{(runFor == Timeout.InfiniteTimeSpan ? string.Empty : $", auto-stop after {runFor}")})");
await simulator.StartAsync();

var watchers = new[]
{
    Watch(simulator.WatchStatusAsync(stop.Token), s => Print("status", $"{s.State}{(s.Detail is null ? string.Empty : $" ({s.Detail})")}")),
    Watch(simulator.State.WatchAsync(stop.Token), s => Print("state", $"paused={s.Paused} crashes={s.CrashCount} lastCrash={s.LastCrashAt:O}")),
    Watch(simulator.AircraftDetector.WatchAsync(stop.Token), a => DescribeAsync(a).GetAwaiter().GetResult()),
};

await Task.WhenAll(watchers);
Print("sample", $"stopping after {simulator.SessionElapsed:hh\\:mm\\:ss}");
await simulator.StopAsync();
Print("sample", $"final status: {simulator.Status.State}");

async Task DescribeAsync(AircraftDescriptor? aircraft)
{
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
    await using var session = await provider.AttachAsync(aircraft);
    var id = session.Identity;
    Print("fenix", $"Match={match.Specificity} Developer='{id.Developer}' Manufacturer='{id.Manufacturer}' Family='{id.Family}' Model='{id.Model}' IcaoType='{id.IcaoType}'");
    Print("fenix", $"Engine='{id.EngineVariant}' Wingtip='{id.WingtipConfiguration}' Registration='{id.Registration}' Operator='{id.OperatorIcao}' Livery='{id.Livery}'");
    Print("fenix", installed is null
        ? "Catalog: no installed livery matches this livery folder"
        : $"Catalog: {installed.PackageName}/{installed.LiveryFolder} (registration '{installed.Identity.Registration}', tags model={installed.Identity.Model} engine={installed.Identity.EngineVariant} wingtip={installed.Identity.WingtipConfiguration})");
}

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
