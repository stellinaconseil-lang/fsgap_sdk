using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Composition;
using FSGAP.SimConnect;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP;

/// <summary>
/// The FSGAP_SDK entry point: one runtime per application. It owns the single simulator connection, follows the loaded
/// aircraft and keeps a normalized <see cref="IAircraftSession"/> for it, opened by the right built-in aircraft provider.
/// </summary>
/// <remarks>
/// <para>
/// Applications never compose providers: the built-in providers (Fenix A319/A320/A321, Synaptic A220-300, and future
/// aircraft families) are registered inside the runtime, and the provider for each loaded aircraft is resolved internally.
/// A session exposes the same members whatever the aircraft (<see cref="IAircraftSession.Identity"/>,
/// <see cref="IAircraftSession.Telemetry"/>, <see cref="IAircraftSession.Failures"/>,
/// <see cref="IAircraftSession.Degradations"/>); what an aircraft supports is read from
/// <see cref="IAircraftSession.Capabilities"/>, never inferred from its vendor.
/// </para>
/// <para>
/// Lifecycle: <see cref="StartAsync"/> connects (and keeps reconnecting) to the simulator and starts following the
/// aircraft; <see cref="DisposeAsync"/> closes the current session (which restores what it applied when it can) and the
/// connection. Exactly one native simulator connection exists per runtime.
/// </para>
/// </remarks>
public sealed class FsgapRuntime : IAsyncDisposable
{
    private readonly FsgapSimulatorServices _services;
    private readonly FsgapComposition _composition;
    private readonly AircraftSessionManager _sessions;
    private readonly IAsyncDisposable? _ownedSimulator;
    private int _started;
    private int _disposed;

    /// <summary>Creates a runtime with its own simulator connection (not connected until <see cref="StartAsync"/>).</summary>
    /// <param name="options">Runtime options.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <param name="timeProvider">Clock; <see cref="TimeProvider.System"/> by default.</param>
    /// <exception cref="ArgumentException">An option is invalid.</exception>
    public FsgapRuntime(FsgapRuntimeOptions options, ILoggerFactory? loggerFactory = null, TimeProvider? timeProvider = null)
        : this(options, CreateSimulator(options, loggerFactory, timeProvider), loggerFactory, timeProvider ?? TimeProvider.System, ownsSimulator: true)
    {
    }

    /// <summary>Composes the runtime on given simulator services (production: one <c>SimConnectSimulator</c>; tests: doubles).</summary>
    internal FsgapRuntime(
        FsgapRuntimeOptions options,
        FsgapSimulatorServices services,
        ILoggerFactory? loggerFactory,
        TimeProvider timeProvider,
        bool ownsSimulator,
        IReadOnlyList<string>? fenixPackageRoots = null,
        HttpClient? fenixEfbHttpClient = null,
        IReadOnlyList<string>? synapticPackageRoots = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _services = services;
        _ownedSimulator = ownsSimulator ? services.Connection : null;
        _composition = FsgapComposition.CreateDefault(options, services, loggerFactory, timeProvider, fenixPackageRoots, fenixEfbHttpClient, synapticPackageRoots);
        _sessions = new AircraftSessionManager(
            _composition.Registry,
            services.Detector,
            (ILogger?)loggerFactory?.CreateLogger<FsgapRuntime>() ?? NullLogger.Instance);
    }

    /// <summary>Ids of the built-in aircraft providers, for diagnostics (applications never need to branch on them).</summary>
    public IReadOnlyList<string> ProviderIds => _composition.Registry.Providers.Select(p => p.ProviderId).ToArray();

    /// <summary>The simulator connection status.</summary>
    public SimulatorConnectionStatus ConnectionStatus => _services.Connection.Status;

    /// <summary>Simulator state (pause, crashes).</summary>
    public ISimulatorStateProvider SimulatorState => _services.State;

    /// <summary>The loaded aircraft as reported by the simulator, whether or not a provider supports it.</summary>
    public IAircraftDetector LoadedAircraft => _services.Detector;

    /// <summary>Generic simulator telemetry, available for any aircraft (a session's telemetry adds the aircraft policy).</summary>
    public ITelemetryProvider GenericTelemetry => _services.GenericTelemetry;

    /// <summary>Airports of the simulator's reality bubble.</summary>
    public IAirportService Airports => _services.Airports;

    /// <summary>Installed aircraft of every supported family (one catalog per family, same normalized contract).</summary>
    public IReadOnlyList<IInstalledAircraftCatalog> InstalledAircraft => _composition.InstalledAircraft;

    /// <summary>The loaded aircraft's session state.</summary>
    public FsgapSessionState SessionState => _sessions.Current;

    /// <summary>The open session, or <see langword="null"/> when no supported aircraft is loaded.</summary>
    public IAircraftSession? CurrentSession => _sessions.Current.Session;

    /// <summary>Internal: the shared simulator services and the composition (composition tests only).</summary>
    internal FsgapSimulatorServices Services => _services;

    /// <summary>Internal: the session manager (composition tests only).</summary>
    internal AircraftSessionManager Sessions => _sessions;

    /// <summary>Yields the current connection status, then each change, until cancelled.</summary>
    public IAsyncEnumerable<SimulatorConnectionStatus> WatchConnectionAsync(CancellationToken cancellationToken = default) =>
        _services.Connection.WatchStatusAsync(cancellationToken);

    /// <summary>
    /// Yields the current session state, then each change (new aircraft, session opened or closed), until cancelled. A
    /// session yielded here is owned by the runtime: do not dispose it.
    /// </summary>
    public IAsyncEnumerable<FsgapSessionState> WatchSessionAsync(CancellationToken cancellationToken = default) => _sessions.WatchAsync(cancellationToken);

    /// <summary>Connects to the simulator (retrying while it is not running) and starts following the loaded aircraft.</summary>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        await _services.Connection.StartAsync(cancellationToken).ConfigureAwait(false);
        _sessions.Start();
    }

    /// <summary>Closes the current session (restoring what it applied when it can) and the simulator connection.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _sessions.DisposeAsync().ConfigureAwait(false);
        if (_ownedSimulator is not null)
        {
            await _ownedSimulator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static FsgapSimulatorServices CreateSimulator(FsgapRuntimeOptions options, ILoggerFactory? loggerFactory, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        // The one native connection: every provider reads, writes and detects through this instance.
        var simulator = new SimConnectSimulator(options.Sdk, loggerFactory?.CreateLogger<SimConnectSimulator>(), timeProvider);
        return new FsgapSimulatorServices(simulator, simulator.State, simulator.AircraftDetector, simulator.Telemetry, simulator, simulator, simulator, simulator);
    }
}
