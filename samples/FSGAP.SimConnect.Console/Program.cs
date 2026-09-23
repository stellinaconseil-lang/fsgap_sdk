// FSGAP.SimConnect live validation sample.
//
// Starts the FSGAP simulator transport and prints connection status changes, pause/crash state and the
// loaded-aircraft descriptor until Ctrl+C. No telemetry, no aircraft-specific logic, no failure injection.
//
// Usage: dotnet run --project samples/FSGAP.SimConnect.Console [-- --minutes N]
using FSGAP.Abstractions.Configuration;
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

await using var simulator = new SimConnectSimulator(options, new ConsoleLogger<SimConnectSimulator>());
Print("sample", $"starting (Ctrl+C to stop{(runFor == Timeout.InfiniteTimeSpan ? string.Empty : $", auto-stop after {runFor}")})");
await simulator.StartAsync();

var watchers = new[]
{
    Watch(simulator.WatchStatusAsync(stop.Token), s => $"{s.State}{(s.Detail is null ? string.Empty : $" ({s.Detail})")}", "status"),
    Watch(simulator.State.WatchAsync(stop.Token), s => $"paused={s.Paused} crashes={s.CrashCount} lastCrash={s.LastCrashAt:O}", "state"),
    Watch(simulator.AircraftDetector.WatchAsync(stop.Token),
        a => a is null ? "none" : $"title='{a.Title}' atcId='{a.Registration}' liveryFolder='{a.LiveryFolder}' livery='{a.Livery}'", "aircraft"),
};

await Task.WhenAll(watchers);
Print("sample", $"stopping after {simulator.SessionElapsed:hh\\:mm\\:ss}");
await simulator.StopAsync();
Print("sample", $"final status: {simulator.Status.State}");

static async Task Watch<T>(IAsyncEnumerable<T> stream, Func<T, string> format, string label)
{
    try
    {
        await foreach (var value in stream)
        {
            Print(label, format(value));
        }
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C or auto-stop.
    }
}

static void Print(string label, string message) => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{label,-8}] {message}");
