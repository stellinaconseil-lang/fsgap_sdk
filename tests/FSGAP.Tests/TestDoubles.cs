using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Geography;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Composition;

namespace FSGAP.Tests;

internal static class Aircraft
{
    public static readonly AircraftDescriptor FenixA320 = new() { Title = "FenixA320 IAE WF", LiveryFolder = "BAW-G-EUYA-0002", Livery = "British Airways" };
    public static readonly AircraftDescriptor FenixA321 = new() { Title = "FenixA321 CFM WF SC", Registration = "F-GMZC", LiveryFolder = "AFR-F-GMZC-8761", Livery = "Air France" };
    public static readonly AircraftDescriptor SynapticA220 = new() { Title = "A220-300", Model = "A220-300", Manufacturer = "223", LiveryFolder = "AIR FRANCE F-HZUF", Livery = "Air France A220-300" };
    public static readonly AircraftDescriptor Unsupported = new() { Title = "A380-800 EA Basic", Model = "A380-800", Manufacturer = "Airbus", LiveryFolder = "INIBUILDS" };
}

/// <summary>One fake simulator standing for the single connection: every service is this one object.</summary>
internal sealed class FakeSimulator : ISimulatorConnection, ISimulatorStateProvider, IAircraftDetector, ITelemetryProvider,
    ISimulatorVariableReader, ISimulatorVariableWriter, IInstalledLiveryService, IAirportService
{
    private readonly object _gate = new();
    private readonly List<Channel<AircraftDescriptor?>> _watchers = [];
    private AircraftDescriptor? _current;
    private int _starts;

    public ConcurrentDictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<(string Name, double Value)> Writes { get; } = new();

    public int Starts => Volatile.Read(ref _starts);

    public int DisposeCount { get; private set; }

    public FsgapSimulatorServices Services => new(this, this, this, this, this, this, this, this);

    // ISimulatorConnection
    public SimulatorConnectionStatus Status { get; private set; } = SimulatorConnectionStatus.Initial;

    public TimeSpan SessionElapsed => TimeSpan.Zero;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _starts);
        Status = SimulatorConnectionStatus.Initial with { State = SimulatorConnectionState.Connected };
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async IAsyncEnumerable<SimulatorConnectionStatus> WatchStatusAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return Status;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return ValueTask.CompletedTask;
    }

    // ISimulatorStateProvider
    SimulatorState ISimulatorStateProvider.Current => new();

    IAsyncEnumerable<SimulatorState> ISimulatorStateProvider.WatchAsync(CancellationToken cancellationToken) => Once(new SimulatorState(), cancellationToken);

    // IAircraftDetector (streams every change, like the transport)
    public AircraftDescriptor? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public int Watchers
    {
        get
        {
            lock (_gate)
            {
                return _watchers.Count;
            }
        }
    }

    public void Load(AircraftDescriptor? aircraft)
    {
        lock (_gate)
        {
            _current = aircraft;
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryWrite(aircraft);
            }
        }
    }

    public async IAsyncEnumerable<AircraftDescriptor?> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<AircraftDescriptor?>();
        lock (_gate)
        {
            _watchers.Add(channel);
            channel.Writer.TryWrite(_current);
        }

        try
        {
            await foreach (var aircraft in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return aircraft;
            }
        }
        finally
        {
            lock (_gate)
            {
                _watchers.Remove(channel);
            }
        }
    }

    // ITelemetryProvider (generic)
    public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AircraftTelemetry.Unavailable(DateTimeOffset.UtcNow));

    public async IAsyncEnumerable<AircraftTelemetry> StreamAsync(TelemetryStreamOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await GetSnapshotAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    // Variables
    public Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<double>>(variables.Select(v => Values.TryGetValue(v.Name, out var value) ? value : 0.0).ToArray());

    public Task WriteAsync(SimulatorVariable variable, double value, CancellationToken cancellationToken = default)
    {
        Writes.Enqueue((variable.Name, value));
        Values[variable.Name] = value;
        return Task.CompletedTask;
    }

    // Services
    public Task<IReadOnlyList<InstalledLivery>> GetInstalledAircraftLiveriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstalledLivery>>([]);

    public Task<IReadOnlyList<AirportInfo>> FindNearbyAirportsAsync(GeoPosition position, AirportSearchOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AirportInfo>>([]);

    public Task<AirportInfo?> FindNearestAirportAsync(GeoPosition position, AirportSearchOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<AirportInfo?>(null);

    private static async IAsyncEnumerable<T> Once<T>(T value, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        yield return value;
    }
}

/// <summary>A runtime on a fake simulator with a temporary data directory and no MSFS installation scan.</summary>
internal sealed class RuntimeRig : IAsyncDisposable
{
    private readonly string _data = Path.Combine(Path.GetTempPath(), "fsgap-runtime-tests", Guid.NewGuid().ToString("N"));

    public RuntimeRig(bool commands = true)
    {
        Directory.CreateDirectory(_data);
        Options = new FsgapRuntimeOptions
        {
            Sdk = new FsgapOptions { ApplicationName = "FsgapRuntimeTests", DataDirectory = _data },
            EnableAircraftCommands = commands,
        };
        Runtime = new FsgapRuntime(Options, Simulator.Services, null, TimeProvider.System, ownsSimulator: false, fenixPackageRoots: [], fenixEfbHttpClient: new HttpClient(new NoEfb()), synapticPackageRoots: []);
    }

    public FakeSimulator Simulator { get; } = new();

    public FsgapRuntimeOptions Options { get; }

    public FsgapRuntime Runtime { get; }

    /// <summary>Loads an aircraft and waits until the runtime has handled it.</summary>
    public async Task<FsgapSessionState> LoadAsync(AircraftDescriptor? aircraft)
    {
        Simulator.Load(aircraft);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!Equals(Runtime.SessionState.Aircraft, aircraft) || (aircraft is not null && Runtime.SessionState.Status == FsgapSessionStatus.NoAircraft))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"The runtime did not handle '{aircraft?.Title}'.");
            }

            await Task.Delay(5);
        }

        return Runtime.SessionState;
    }

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        try
        {
            Directory.Delete(_data, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class NoEfb : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
    }
}
