using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Observation;
using FSGAP.SimConnect.Native;
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
public sealed class SimConnectSimulator : ISimulatorConnection
{
    /// <summary>Identity poll interval while connecting or while the aircraft is changing.</summary>
    internal static readonly TimeSpan IdentityFastInterval = TimeSpan.FromSeconds(2);

    /// <summary>Identity poll interval once the aircraft is stable.</summary>
    internal static readonly TimeSpan IdentityStableInterval = TimeSpan.FromSeconds(5);

    /// <summary>Consecutive identical identity reads after which the aircraft is considered stable.</summary>
    internal const int IdentityStableReads = 3;

    internal const string SimulatorNotRunning = "Simulator not running.";
    internal const string ConnectionLost = "Connection to the simulator was lost.";

    private readonly FsgapOptions _options;
    private readonly ISimConnectSessionFactory _factory;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly ObservableState<SimulatorConnectionStatus> _status = new(SimulatorConnectionStatus.Initial);
    private readonly object _statusGate = new();
    private readonly SimulatorStateSource _state = new();
    private readonly AircraftDetectorSource _aircraft = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly object _clockGate = new();

    private long? _sessionStartedAt;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private bool _disposed;

    /// <summary>Creates the connection. Nothing happens until <see cref="StartAsync"/>.</summary>
    /// <param name="options">Host options; <see cref="FsgapOptions.ApplicationName"/> is the SimConnect client name.</param>
    /// <param name="logger">Optional logger; lifecycle transitions are logged, individual polls are not.</param>
    /// <param name="timeProvider">Clock for delays and timestamps; <see cref="TimeProvider.System"/> by default.</param>
    /// <exception cref="ArgumentException"><paramref name="options"/> is invalid.</exception>
    public SimConnectSimulator(FsgapOptions options, ILogger<SimConnectSimulator>? logger = null, TimeProvider? timeProvider = null)
        : this(options, new SimConnectNetSessionFactory(), logger, timeProvider)
    {
    }

    internal SimConnectSimulator(FsgapOptions options, ISimConnectSessionFactory factory, ILogger? logger, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(factory);
        options.Validate();
        _options = options;
        _factory = factory;
        _logger = logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public SimulatorConnectionStatus Status => _status.Current;

    /// <summary>Simulation state: pause and crashes.</summary>
    public ISimulatorStateProvider State => _state;

    /// <summary>The aircraft currently loaded, as reported by the simulator.</summary>
    public IAircraftDetector AircraftDetector => _aircraft;

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
        try
        {
            await TrySubscribeAsync(session, SimulatorSystemEvent.Crashed, sessionCts.Token).ConfigureAwait(false);
            var pauseSubscribed = await TrySubscribeAsync(session, SimulatorSystemEvent.Pause, sessionCts.Token).ConfigureAwait(false);
            _state.MarkConnected(pauseSubscribed);
            SetStatus(SimulatorConnectionState.Connected, null);

            identityTask = PollIdentityAsync(session, lost, sessionCts.Token);
            var finished = await Task.WhenAny(lost.Task, identityTask).ConfigureAwait(false);
            await finished.ConfigureAwait(false);
        }
        finally
        {
            // Callbacks the native layer may still deliver for this connection are ignored from here on.
            active.Deactivate();
            session.Disconnected -= OnDisconnected;
            session.Crashed -= OnCrashed;
            session.PauseChanged -= OnPauseChanged;
            await sessionCts.CancelAsync().ConfigureAwait(false);
            await AwaitQuietlyAsync(identityTask).ConfigureAwait(false);
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
            _logger.LogWarning(ex, "Aircraft identity polling ended with an error");
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

    /// <summary>Marks the callbacks of one native connection as current; deactivated when that connection ends.</summary>
    private sealed class SessionGuard
    {
        private volatile bool _active = true;

        public bool IsActive => _active;

        public void Deactivate() => _active = false;
    }
}
