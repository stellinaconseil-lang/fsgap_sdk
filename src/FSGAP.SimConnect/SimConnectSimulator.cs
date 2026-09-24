using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Geography;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Observation;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Services;
using FSGAP.SimConnect.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.SimConnect;

/// <summary>
/// Connection to Microsoft Flight Simulator through SimConnect: lifecycle with automatic retry and reconnection,
/// simulation state (<see cref="State"/>) and loaded-aircraft detection (<see cref="AircraftDetector"/>).
/// Vendor-neutral: it reports what the simulator says and never interprets the aircraft.
/// </summary>
/// <remarks>
/// <para>
/// One background loop owns the native connection. It opens it, subscribes the system events, polls the aircraft
/// identity, and disposes the connection before any replacement is opened. Native events only update immutable
/// state and return. Consumers are notified through latest-value streams whose continuations never run on the
/// native thread, so a slow or failing consumer cannot block or break the transport.
/// </para>
/// <para>
/// Idempotence rules:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="StartAsync"/> while already started (including in <see cref="SimulatorConnectionState.Faulted"/>)
/// does nothing.
/// </description></item>
/// <item><description><see cref="StopAsync"/> while stopped does nothing.</description></item>
/// <item><description>Start after Stop begins a new session.</description></item>
/// <item><description>After disposal, Start throws <see cref="ObjectDisposedException"/>.</description></item>
/// </list>
/// </remarks>
public sealed class SimConnectSimulator : ISimulatorConnection, ISimulatorVariableReader, IAirportService
{
    /// <summary>Identity poll interval while connecting or while the aircraft is changing.</summary>
    internal static readonly TimeSpan IdentityFastInterval = TimeSpan.FromSeconds(2);

    /// <summary>Identity poll interval once the aircraft is stable.</summary>
    internal static readonly TimeSpan IdentityStableInterval = TimeSpan.FromSeconds(5);

    /// <summary>Consecutive identical identity reads after which the aircraft is considered stable.</summary>
    internal const int IdentityStableReads = 3;

    /// <summary>ADR 0005: position at 1 Hz. The audited applications used ten seconds, a network concession
    /// rather than a property of the data.</summary>
    internal static readonly TimeSpan FastGroupInterval = TimeSpan.FromSeconds(1);

    /// <summary>Gear, flaps and speed brake. Faster than the audited five seconds, because configuration at
    /// touchdown is what a landing analysis needs and five seconds can straddle a transition.</summary>
    internal static readonly TimeSpan NormalGroupInterval = TimeSpan.FromSeconds(2);

    /// <summary>Engines. Thermal and rotational quantities; the audited five seconds proved sufficient.</summary>
    internal static readonly TimeSpan SlowGroupInterval = TimeSpan.FromSeconds(5);

    /// <summary>Weather around the aircraft. It changes over minutes; the audited ten seconds is kept.</summary>
    internal static readonly TimeSpan EnvironmentGroupInterval = TimeSpan.FromSeconds(10);

    internal const string SimulatorNotRunning = "Simulator not running.";
    internal const string ConnectionLost = "Connection to the simulator was lost.";
    internal const string SimulatorNotConnected = "The simulator is not connected.";

    /// <summary>How long an airport list is reused. The bubble moves with the aircraft, so this stays short.</summary>
    internal static readonly TimeSpan AirportListCacheLifetime = TimeSpan.FromSeconds(10);

    private readonly FsgapOptions _options;
    private readonly ISimConnectSessionFactory _factory;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ObservableState<SimulatorConnectionStatus> _status = new(SimulatorConnectionStatus.Initial);
    private readonly object _statusGate = new();
    private readonly SimulatorStateSource _state = new();
    private readonly AircraftDetectorSource _aircraft = new();
    private readonly TelemetrySource _telemetry;
    private readonly bool _pollTelemetry;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _clockGate = new();
    private readonly object _airportGate = new();

    private long? _sessionStartedAt;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private bool _disposed;
    private volatile LiveSession? _connectedSession;
    private AirportListCache? _airportCache;

    /// <summary>Creates the connection. Nothing happens until <see cref="StartAsync"/>.</summary>
    /// <param name="options">Host options; <see cref="FsgapOptions.ApplicationName"/> is the SimConnect client name.</param>
    /// <param name="logger">Optional logger; lifecycle transitions are logged, individual polls are not.</param>
    /// <param name="timeProvider">Clock for delays and timestamps; <see cref="TimeProvider.System"/> by default.</param>
    /// <exception cref="ArgumentException"><paramref name="options"/> is invalid.</exception>
    public SimConnectSimulator(FsgapOptions options, ILogger<SimConnectSimulator>? logger = null, TimeProvider? timeProvider = null)
        : this(options, new SimConnectNetSessionFactory(), logger, timeProvider)
    {
    }

    /// <summary>Test seam: a transport over a given native layer.</summary>
    /// <param name="options">Host options.</param>
    /// <param name="factory">Opens native connections.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Clock for delays and timestamps.</param>
    /// <param name="pollTelemetry">
    /// <see langword="false"/> leaves <see cref="Telemetry"/> permanently Unavailable and creates no telemetry
    /// timers. Only the lifecycle tests use it, because they count the timers the transport creates.
    /// </param>
    internal SimConnectSimulator(
        FsgapOptions options,
        ISimConnectSessionFactory factory,
        ILogger? logger,
        TimeProvider? timeProvider,
        bool pollTelemetry = true)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        options.Validate();
        _options = options;
        _factory = factory;
        _logger = logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _telemetry = new TelemetrySource(_time, options.Telemetry.StaleAfter);
        _pollTelemetry = pollTelemetry;
    }

    /// <inheritdoc />
    public SimulatorConnectionStatus Status => _status.Current;

    /// <summary>Simulation state: pause and crashes.</summary>
    public ISimulatorStateProvider State => _state;

    /// <summary>The aircraft currently loaded, as reported by the simulator.</summary>
    public IAircraftDetector AircraftDetector => _aircraft;

    /// <summary>Generic MSFS telemetry for the attached aircraft.</summary>
    /// <remarks>
    /// Bound to this connection, not a global: a snapshot always describes the aircraft this transport is
    /// attached to, and an aircraft change clears it rather than letting the previous aircraft's readings pass
    /// for the new one.
    /// </remarks>
    public ITelemetryProvider Telemetry => _telemetry;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Carried out on this transport's native connection (never a second one) as a single request: the list becomes
    /// one data definition, registered by the library on first use per connection.
    /// </para>
    /// <para>
    /// The transport knows nothing about the variables: aircraft providers own the names and their meaning. There is
    /// no retry here; a read that fails while the connection is being lost or replaced simply throws, and the caller
    /// polls again. Continuations never run on the native thread.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default)
    {
        VariableSetStructs.Validate(variables);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var session = _connectedSession?.Session;
        if (session is null || !session.IsConnected)
        {
            throw new InvalidOperationException(SimulatorNotConnected);
        }

        var snapshot = variables.ToArray();
        return await session.ReadVariablesAsync(snapshot, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The airports come from the simulator's facility list for its <b>reality bubble</b>: the area loaded around the
    /// user aircraft. A position far from the aircraft only finds the bubble's airports, so the nearest one returned can
    /// be further away than the real nearest airport, or none may be within range.
    /// </para>
    /// <para>
    /// The list is requested on this transport's connection (one native request, through the library's dispatcher, see
    /// <c>FacilityInterop</c>) and kept for <see cref="AirportListCacheLifetime"/>. Concurrent searches share one
    /// request; a new connection always starts without a cache.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<AirportInfo>> FindNearbyAirportsAsync(
        GeoPosition position,
        AirportSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= AirportSearchOptions.Default;
        options.Validate();
        var airports = await GetAirportListAsync(cancellationToken).ConfigureAwait(false);
        return AirportSelection.Select(airports, position, options);
    }

    /// <inheritdoc />
    /// <remarks>Same source and limits as <see cref="FindNearbyAirportsAsync"/>.</remarks>
    public async Task<AirportInfo?> FindNearestAirportAsync(
        GeoPosition position,
        AirportSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var nearby = await FindNearbyAirportsAsync(position, (options ?? AirportSearchOptions.Default) with { MaxResults = 1 }, cancellationToken)
            .ConfigureAwait(false);
        return nearby.Count == 0 ? null : nearby[0];
    }

    /// <inheritdoc />
    public TimeSpan SessionElapsed
    {
        get
        {
            lock (_clockGate)
            {
                return _sessionStartedAt is { } startedAt ? _time.GetElapsedTime(startedAt) : TimeSpan.Zero;
            }
        }
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">The connection has been disposed.</exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_runTask is not null)
            {
                return;
            }

            lock (_clockGate)
            {
                _sessionStartedAt = _time.GetTimestamp();
            }

            _state.Reset();
            _aircraft.Publish(null);
            SetStatus(SimulatorConnectionState.Connecting, null);
            _runCts = new CancellationTokenSource();
            var token = _runCts.Token;
            _runTask = Task.Run(() => RunAsync(token), CancellationToken.None);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SimulatorConnectionStatus> WatchStatusAsync(CancellationToken cancellationToken = default) =>
        _status.WatchAsync(cancellationToken);

    /// <summary>Stops the connection and completes every watch stream.</summary>
    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            await StopCoreAsync().ConfigureAwait(false);
            _disposed = true;
            _status.Complete();
            _state.Complete();
            _aircraft.Complete();
            _telemetry.Complete();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>Errors after which retrying cannot help: the native SimConnect library cannot be loaded.</summary>
    internal static bool IsTerminal(Exception exception) =>
        exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException;

    private async Task StopCoreAsync()
    {
        if (_runTask is null)
        {
            return;
        }

        await _runCts!.CancelAsync().ConfigureAwait(false);
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected: the loop observed the stop request.
        }

        _runCts.Dispose();
        _runCts = null;
        _runTask = null;
        lock (_clockGate)
        {
            _sessionStartedAt = null;
        }

        _state.Reset();
        _aircraft.Publish(null);
        _telemetry.Reset();
        SetStatus(SimulatorConnectionState.Disconnected, null);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var hasConnected = false;
        var attempt = 0;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;
                ISimConnectSession session;
                try
                {
                    _logger.Log(attempt == 1 ? LogLevel.Information : LogLevel.Debug,
                        "Connecting to the simulator as '{ApplicationName}' (attempt {Attempt})", _options.ApplicationName, attempt);
                    session = await _factory.ConnectAsync(_options.ApplicationName, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsTerminal(ex))
                {
                    _logger.LogError(ex, "The SimConnect native library cannot be used; the connection will not be retried");
                    SetStatus(SimulatorConnectionState.Faulted, ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    var detail = ex is SimulatorUnavailableException ? SimulatorNotRunning : ex.Message;
                    if (ex is not SimulatorUnavailableException && Status.Detail != detail)
                    {
                        _logger.LogWarning(ex, "Unexpected error while connecting to the simulator; retrying");
                    }

                    await WaitBeforeRetryAsync(hasConnected ? SimulatorConnectionState.Reconnecting : SimulatorConnectionState.WaitingForSimulator, detail, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                hasConnected = true;
                attempt = 0;
                await RunSessionAsync(session, cancellationToken).ConfigureAwait(false);

                // The session ended without a stop request: the connection was lost.
                _state.MarkDisconnected();
                _aircraft.Publish(null);
                if (_pollTelemetry)
                {
                    _ = ExpireTelemetryLaterAsync(cancellationToken);
                }

                await WaitBeforeRetryAsync(SimulatorConnectionState.Reconnecting, ConnectionLost, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop requested.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "The simulator transport stopped on an unexpected error");
            SetStatus(SimulatorConnectionState.Faulted, ex.Message);
        }
    }

    private async Task WaitBeforeRetryAsync(SimulatorConnectionState state, string detail, CancellationToken cancellationToken)
    {
        // The delay is scheduled before the status is published, so an observer of the status knows the retry
        // timer already exists (this makes the lifecycle deterministic to test with a fake clock).
        var delay = Task.Delay(_options.Connection.RetryDelay, _time, cancellationToken);
        SetStatus(state, detail);
        await delay.ConfigureAwait(false);
    }

    private async Task RunSessionAsync(ISimConnectSession session, CancellationToken cancellationToken)
    {
        var active = new SessionGuard();
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDisconnected() => lost.TrySetResult();
        void OnCrashed()
        {
            if (active.IsActive)
            {
                var count = _state.RecordCrash(_time.GetUtcNow());
                _logger.LogInformation("The simulator reported a crash (#{CrashCount} this session)", count);
            }
        }

        void OnPauseChanged(bool paused)
        {
            if (active.IsActive)
            {
                _state.RecordPause(paused, _time.GetUtcNow());
            }
        }

        session.Disconnected += OnDisconnected;
        session.Crashed += OnCrashed;
        session.PauseChanged += OnPauseChanged;
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? identityTask = null;
        Task[] groupTasks = [];
        try
        {
            await TrySubscribeAsync(session, SimulatorSystemEvent.Crashed, sessionCts.Token).ConfigureAwait(false);
            var pauseSubscribed = await TrySubscribeAsync(session, SimulatorSystemEvent.Pause, sessionCts.Token).ConfigureAwait(false);
            _state.MarkConnected(pauseSubscribed);
            _connectedSession = new LiveSession(session, sessionCts.Token);
            SetStatus(SimulatorConnectionState.Connected, null);

            identityTask = PollIdentityAsync(session, lost, sessionCts.Token);
            groupTasks = !_pollTelemetry ? [] :
            [
                PollGroupAsync<FastGroupVars>(session, TelemetryGroup.Fast, FastGroupInterval, _telemetry.ApplyFast, sessionCts.Token),
                PollGroupAsync<NormalGroupVars>(session, TelemetryGroup.Normal, NormalGroupInterval, _telemetry.ApplyNormal, sessionCts.Token),
                PollGroupAsync<SlowGroupVars>(session, TelemetryGroup.Slow, SlowGroupInterval, _telemetry.ApplySlow, sessionCts.Token),
                PollGroupAsync<EnvironmentGroupVars>(session, TelemetryGroup.Environment, EnvironmentGroupInterval, _telemetry.ApplyEnvironment, sessionCts.Token),
            ];

            var finished = await Task.WhenAny([lost.Task, identityTask, .. groupTasks]).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
        }
        finally
        {
            // Callbacks the native layer may still deliver for this connection are ignored from here on.
            _connectedSession = null;
            active.Deactivate();
            session.Disconnected -= OnDisconnected;
            session.Crashed -= OnCrashed;
            session.PauseChanged -= OnPauseChanged;
            await sessionCts.CancelAsync().ConfigureAwait(false);
            await AwaitQuietlyAsync(identityTask).ConfigureAwait(false);
            foreach (var groupTask in groupTasks)
            {
                await AwaitQuietlyAsync(groupTask).ConfigureAwait(false);
            }

            // Nothing will arrive until the simulator returns, so publish the snapshot aged to now: a stream
            // must not keep showing the last live values as though the aircraft were still flying. Values read
            // less than StaleAfter ago are still fresh at this instant; ExpireTelemetryLaterAsync finishes the job.
            _telemetry.ExpireNow();
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing the simulator connection");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task<bool> TrySubscribeAsync(ISimConnectSession session, SimulatorSystemEvent systemEvent, CancellationToken cancellationToken)
    {
        try
        {
            await session.SubscribeAsync(systemEvent, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not subscribe to the simulator {SystemEvent} event; that information stays unavailable", systemEvent);
            return false;
        }
    }

    private async Task PollIdentityAsync(ISimConnectSession session, TaskCompletionSource lost, CancellationToken cancellationToken)
    {
        var identicalReads = 0;
        var failing = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // ForceYielding: never continue on the native message thread, whatever the library does.
                var raw = await session.ReadAircraftIdentityAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                if (failing)
                {
                    _logger.LogInformation("Aircraft identity reads recovered");
                    failing = false;
                }

                var aircraft = AircraftIdentityMapper.ToDescriptor(raw);
                if (_aircraft.Publish(aircraft))
                {
                    // Not stale data about the new aircraft — accurate data about a different one. Ageing would
                    // leave it looking merely old, so it is dropped outright.
                    _telemetry.Reset();
                    identicalReads = 1;
                    _logger.LogInformation("Loaded aircraft: {Title} (livery folder {LiveryFolder})", aircraft?.Title ?? "none", aircraft?.LiveryFolder ?? "none");
                }
                else
                {
                    identicalReads++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (!session.IsConnected)
                {
                    lost.TrySetResult();
                    return;
                }

                if (!failing)
                {
                    _logger.LogWarning(ex, "Could not read the aircraft identity; retrying");
                    failing = true;
                }

                identicalReads = 0;
            }

            var interval = identicalReads >= IdentityStableReads ? IdentityStableInterval : IdentityFastInterval;
            await Task.Delay(interval, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads one telemetry group on its own cadence until the session ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One loop per group, and each one survives its own failures. A group that starts failing — an unsupported
    /// variable, a transient native error — stops updating its own sections and nothing else: the other groups
    /// keep publishing, and the failing group's values age into Unknown through the normal freshness path rather
    /// than freezing or taking the connection down with them.
    /// </para>
    /// <para>
    /// Logging is on transition only. At 1 Hz a per-read log would be 3,600 lines an hour saying the same thing,
    /// which is how a log stops being read at all. One line when a group starts failing, one when it recovers.
    /// </para>
    /// </remarks>
    private async Task PollGroupAsync<TGroup>(
        ISimConnectSession session,
        TelemetryGroup group,
        TimeSpan interval,
        ApplyGroup<TGroup> apply,
        CancellationToken cancellationToken)
        where TGroup : struct
    {
        var failing = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // No aircraft (main menu, or before the first identity read): there is nothing to describe, and a
                // snapshot must never exist without the aircraft it belongs to.
                if (_aircraft.Current is not null)
                {
                    // Captured before the read: if the aircraft changes while the read is in flight, the result
                    // belongs to the previous aircraft and TelemetrySource drops it.
                    var generation = _telemetry.Generation;

                    // ForceYielding: the native message thread copies and returns; normalization and publication
                    // happen off it, as BLOCK 3 established.
                    var vars = await session.ReadTelemetryGroupAsync<TGroup>(cancellationToken)
                        .ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
                    apply(vars, _time.GetUtcNow(), generation);
                }

                if (failing)
                {
                    _logger.LogInformation("Telemetry group {Group} recovered", group);
                    failing = false;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (!session.IsConnected)
                {
                    // The connection itself is gone; RunSessionAsync owns that, so this loop simply stops.
                    return;
                }

                if (!failing)
                {
                    _logger.LogWarning(ex, "Could not read telemetry group {Group}; its values will expire", group);
                    failing = true;
                }

                // If every group is failing, nothing else publishes: age the snapshot here so subscribers see it.
                _telemetry.ExpireNow();
            }

            await Task.Delay(interval, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// After a connection loss, publishes the snapshot once more when everything read before the loss has passed
    /// the telemetry StaleAfter, so a stream shows Unknown without having to be polled.
    /// </summary>
    private async Task ExpireTelemetryLaterAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_options.Telemetry.StaleAfter + TimeSpan.FromMilliseconds(1), _time, cancellationToken).ConfigureAwait(false);
            _telemetry.ExpireNow();
        }
        catch (OperationCanceledException)
        {
            // Stopped; StopCoreAsync resets the telemetry anyway.
        }
    }

    private async Task AwaitQuietlyAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when the session is torn down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A polling loop ended with an error");
        }
    }

    private void SetStatus(SimulatorConnectionState state, string? detail)
    {
        lock (_statusGate)
        {
            var current = _status.Current;
            if (current.State == state && current.Detail == detail)
            {
                return;
            }

            _status.Set(new SimulatorConnectionStatus { State = state, Since = _time.GetUtcNow(), Detail = detail });
            if (current.State == state)
            {
                return;
            }

            switch (state)
            {
                case SimulatorConnectionState.Connected:
                    _logger.LogInformation("Connected to the simulator");
                    break;
                case SimulatorConnectionState.WaitingForSimulator:
                    _logger.LogInformation("Simulator not available; retrying every {RetryDelay}", _options.Connection.RetryDelay);
                    break;
                case SimulatorConnectionState.Reconnecting:
                    _logger.LogWarning("Connection to the simulator lost; reconnecting every {RetryDelay}", _options.Connection.RetryDelay);
                    break;
                case SimulatorConnectionState.Disconnected:
                    _logger.LogInformation("Simulator connection stopped");
                    break;
                case SimulatorConnectionState.Faulted:
                    _logger.LogError("Simulator connection faulted: {Detail}", detail);
                    break;
                case SimulatorConnectionState.Connecting:
                    break;
            }
        }
    }

    /// <summary>Hands a freshly read group to the aggregator. Separate delegate type because a group is passed
    /// by <c>in</c> reference and <see cref="Action{T1, T2, T3}"/> cannot express that.</summary>
    private delegate void ApplyGroup<TGroup>(in TGroup vars, DateTimeOffset observedAt, int generation)
        where TGroup : struct;

    /// <summary>
    /// The airport list of the current connection: from the cache while it is fresh and belongs to this connection,
    /// otherwise from one new native request shared by every concurrent caller. A caller's cancellation only stops its
    /// own wait; the shared request ends with the connection.
    /// </summary>
    /// <exception cref="SimulatorServiceException">Not connected, or the query failed.</exception>
    private async Task<IReadOnlyList<RawAirport>> GetAirportListAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var live = _connectedSession;
        if (live is null || !live.Session.IsConnected)
        {
            throw new SimulatorServiceException(SimulatorServiceError.SimulatorUnavailable, SimulatorNotConnected);
        }

        Task<IReadOnlyList<RawAirport>> request;
        lock (_airportGate)
        {
            var cached = _airportCache;
            if (cached is not null
                && ReferenceEquals(cached.Session, live.Session)
                && !cached.Request.IsFaulted
                && !cached.Request.IsCanceled
                && _time.GetElapsedTime(cached.RequestedAt) <= AirportListCacheLifetime)
            {
                request = cached.Request;
            }
            else
            {
                request = RequestAirportListAsync(live);
                _airportCache = new AirportListCache(live.Session, _time.GetTimestamp(), request);
            }
        }

        try
        {
            return await request.WaitAsync(cancellationToken).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SimulatorServiceException(SimulatorServiceError.SimulatorUnavailable, ConnectionLost);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or SimulatorServiceException))
        {
            if (!live.Session.IsConnected || live.Lifetime.IsCancellationRequested)
            {
                throw new SimulatorServiceException(SimulatorServiceError.SimulatorUnavailable, ConnectionLost, ex);
            }

            _logger.LogWarning(ex, "The airport list query failed");
            throw new SimulatorServiceException(SimulatorServiceError.QueryFailed, $"The airport list query failed: {ex.Message}", ex);
        }
    }

    private async Task<IReadOnlyList<RawAirport>> RequestAirportListAsync(LiveSession live)
    {
        // ForceYielding: never continue on the native message thread.
        await Task.Yield();
        return await live.Session.RequestAirportsAsync(live.Lifetime).ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
    }

    /// <summary>The connected native session and the token that ends with it.</summary>
    private sealed record LiveSession(ISimConnectSession Session, CancellationToken Lifetime);

    /// <summary>One airport-list request of one connection, and when it was sent.</summary>
    private sealed record AirportListCache(ISimConnectSession Session, long RequestedAt, Task<IReadOnlyList<RawAirport>> Request);

    /// <summary>Marks the callbacks of one native connection as current; deactivated when that connection ends.</summary>
    private sealed class SessionGuard
    {
        private volatile bool _active = true;

        public bool IsActive => _active;

        public void Deactivate() => _active = false;
    }
}
