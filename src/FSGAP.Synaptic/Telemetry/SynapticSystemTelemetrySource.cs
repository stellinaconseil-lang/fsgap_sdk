using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using Microsoft.Extensions.Logging;

namespace FSGAP.Synaptic.Telemetry;

/// <summary>
/// Polls the Synaptic overlay group for one session and keeps the latest <see cref="SynapticSystemState"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime = the session.</b> Created and started by <c>SynapticAircraftProvider.AttachAsync</c>, which only happens
/// for an aircraft the recognizer accepted, and stopped when the session is disposed. Without a Synaptic session there
/// is no instance and no Synaptic read.
/// </para>
/// <para>
/// <b>No connection of its own.</b> Every read goes through <see cref="ISimulatorVariableReader"/> on the simulator's
/// single native connection. While the simulator is away reads fail or are skipped, and resume by themselves.
/// </para>
/// <para>
/// <b>Which aircraft.</b> With a detector: the attached aircraft is read; nothing loaded means no read (held values age
/// into Unknown); another aircraft means no read, and the state is discarded under a new generation so a read in
/// flight for the previous aircraft can never land.
/// </para>
/// </remarks>
internal sealed class SynapticSystemTelemetrySource : IAsyncDisposable
{
    private readonly ISimulatorVariableReader _reader;
    private readonly IAircraftDetector? _detector;
    private readonly AircraftDescriptor _attached;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();

    private SynapticSystemState _state = SynapticSystemState.Empty;
    private int _generation;
    private Task _loop = Task.CompletedTask;
    private int _disposed;

    internal SynapticSystemTelemetrySource(
        ISimulatorVariableReader reader,
        IAircraftDetector? detector,
        AircraftDescriptor attached,
        TimeProvider time,
        ILogger logger)
    {
        _reader = reader;
        _detector = detector;
        _attached = attached;
        _time = time;
        _logger = logger;
    }

    /// <summary>The latest overlay state (immutable).</summary>
    internal SynapticSystemState Current
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>Whether the simulator now reports a different aircraft from the one this session was attached to.</summary>
    internal bool AircraftReplaced => _detector?.Current is { } loaded && !loaded.Equals(_attached);

    /// <summary>Starts the polling loop. Called once.</summary>
    internal void Start() => _loop = Task.Run(() => PollAsync(_stop.Token));

    /// <summary>Stops the loop and waits for it; an in-flight read is cancelled.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The Synaptic telemetry loop ended with an error");
        }

        _stop.Dispose();
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var failing = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AircraftReplaced)
            {
                Discard();
            }
            else if (_detector is null || _detector.Current is not null)
            {
                failing = await ReadOnceAsync(failing, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(SynapticVariables.SystemsInterval, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ReadOnceAsync(bool failing, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _generation);
        try
        {
            var raw = await _reader.ReadAsync(SynapticVariables.Systems, cancellationToken).ConfigureAwait(false);
            var observedAt = _time.GetUtcNow();
            lock (_gate)
            {
                if (generation == _generation && !AircraftReplaced)
                {
                    _state = SynapticSystemMapper.Apply(raw, observedAt);
                }
            }

            if (failing)
            {
                _logger.LogInformation("Synaptic A220 readings recovered");
            }

            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (!failing)
            {
                _logger.LogWarning(ex, "Could not read the Synaptic A220 variables; their values will expire");
            }

            return true;
        }
    }

    private void Discard()
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_state, SynapticSystemState.Empty))
            {
                _generation++;
                _state = SynapticSystemState.Empty;
            }
        }
    }
}
