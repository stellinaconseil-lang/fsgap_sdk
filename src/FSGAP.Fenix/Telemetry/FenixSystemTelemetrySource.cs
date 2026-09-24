using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Fenix.Variables;
using Microsoft.Extensions.Logging;

namespace FSGAP.Fenix.Telemetry;

/// <summary>
/// Polls the Fenix-specific variables for one Fenix session and keeps the latest <see cref="FenixSystemState"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime = the session.</b> Created and started by <c>FenixAircraftProvider.AttachAsync</c>, which only
/// happens for an aircraft the Fenix recognizer accepted, and stopped when the session is disposed. With no Fenix
/// session there is no instance and therefore no Fenix read at all.
/// </para>
/// <para>
/// <b>No connection of its own.</b> Every read goes through <see cref="ISimulatorVariableReader"/>, which the
/// simulator transport carries out on its single native connection. There is no client, no reconnect loop and no
/// lifecycle here: while the simulator is away the reads fail or are skipped, and they resume by themselves.
/// </para>
/// <para>
/// <b>Which aircraft.</b> With an <see cref="IAircraftDetector"/>, each read is gated on what is loaded:
/// </para>
/// <list type="bullet">
/// <item><description>the attached aircraft: read;</description></item>
/// <item><description>
/// nothing (menu, connection lost): no read; the values already held age into Unknown like every other value;
/// </description></item>
/// <item><description>
/// another aircraft: no read, and everything held is discarded under a new generation, so a read that was in flight
/// for the previous aircraft can never land. <see cref="AircraftReplaced"/> tells the composer to publish nothing.
/// </description></item>
/// </list>
/// <para>
/// <b>Threading.</b> One loop per group on the thread pool. A read's result is merged under a lock into a new
/// immutable state; readers of <see cref="Current"/> never see a partial update. Failures are logged on transition
/// only (one line when a group starts failing, one when it recovers).
/// </para>
/// </remarks>
internal sealed class FenixSystemTelemetrySource : IAsyncDisposable
{
    private readonly ISimulatorVariableReader _reader;
    private readonly IAircraftDetector? _detector;
    private readonly AircraftDescriptor _attached;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<Group> _groups;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stop = new();

    private FenixSystemState _state = FenixSystemState.Empty;
    private int _generation;
    private Task[] _loops = [];
    private int _disposed;

    internal FenixSystemTelemetrySource(
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
        _groups =
        [
            new Group("cockpit", FenixVariables.Cockpit, FenixVariables.CockpitInterval, FenixSystemMapper.ApplyCockpit),
            new Group("systems", FenixVariables.Systems, FenixVariables.SystemsInterval, FenixSystemMapper.ApplySystems),
        ];
    }

    /// <summary>The latest Fenix state (immutable).</summary>
    internal FenixSystemState Current
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// Whether the simulator now reports a <i>different</i> aircraft from the one this session was attached to.
    /// Nothing loaded (null) is not a replacement: it is a gap, handled by freshness.
    /// </summary>
    internal bool AircraftReplaced => _detector?.Current is { } loaded && !loaded.Equals(_attached);

    /// <summary>Starts one polling loop per group. Called once.</summary>
    internal void Start()
    {
        _loops = _groups.Select(g => Task.Run(() => PollAsync(g, _stop.Token))).ToArray();
    }

    /// <summary>Stops the loops and waits for them; an in-flight read is cancelled.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        foreach (var loop in _loops)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A Fenix telemetry loop ended with an error");
            }
        }

        _stop.Dispose();
    }

    private async Task PollAsync(Group group, CancellationToken cancellationToken)
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
                failing = await ReadOnceAsync(group, failing, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(group.Interval, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ReadOnceAsync(Group group, bool failing, CancellationToken cancellationToken)
    {
        var generation = Volatile.Read(ref _generation);
        try
        {
            var raw = await _reader.ReadAsync(group.Variables, cancellationToken).ConfigureAwait(false);
            var observedAt = _time.GetUtcNow();
            lock (_gate)
            {
                if (generation == _generation && !AircraftReplaced)
                {
                    _state = group.Apply(_state, raw, observedAt);
                }
            }

            if (failing)
            {
                _logger.LogInformation("Fenix {Group} readings recovered", group.Name);
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
                _logger.LogWarning(ex, "Could not read the Fenix {Group} variables; their values will expire", group.Name);
            }

            return true;
        }
    }

    private void Discard()
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_state, FenixSystemState.Empty))
            {
                _generation++;
                _state = FenixSystemState.Empty;
            }
        }
    }

    private sealed record Group(
        string Name,
        IReadOnlyList<SimulatorVariable> Variables,
        TimeSpan Interval,
        Func<FenixSystemState, IReadOnlyList<double>, DateTimeOffset, FenixSystemState> Apply);
}
