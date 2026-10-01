using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Observation;
using FSGAP.Core.Resolution;
using FSGAP.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FSGAP.Composition;

/// <summary>
/// Follows the loaded aircraft and keeps at most one open session: resolves each new aircraft through the registry,
/// disposes the previous session before attaching the next, and keeps the session when only metadata of the same loaded
/// aircraft changes (ATC ID churn, see <see cref="AircraftContinuity"/>). Changes are handled one at a time.
/// </summary>
internal sealed class AircraftSessionManager : IAsyncDisposable
{
    private readonly AircraftProviderRegistry _registry;
    private readonly IAircraftDetector _detector;
    private readonly ILogger _logger;
    private readonly ObservableState<FsgapSessionState> _state = new(FsgapSessionState.None);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private Task _loop = Task.CompletedTask;
    private int _opened;
    private int _disposedSessions;
    private int _disposed;

    internal AircraftSessionManager(AircraftProviderRegistry registry, IAircraftDetector detector, ILogger logger)
    {
        _registry = registry;
        _detector = detector;
        _logger = logger;
    }

    internal FsgapSessionState Current => _state.Current;

    internal int SessionsOpened => Volatile.Read(ref _opened);

    internal int SessionsDisposed => Volatile.Read(ref _disposedSessions);

    internal IAsyncEnumerable<FsgapSessionState> WatchAsync(CancellationToken cancellationToken) => _state.WatchAsync(cancellationToken);

    internal void Start()
    {
        if (_loop.IsCompleted)
        {
            _loop = Task.Run(() => FollowAsync(_stop.Token));
        }
    }

    /// <summary>Handles one detector emission (the loop calls it; tests may call it directly).</summary>
    internal async Task OnAircraftAsync(AircraftDescriptor? aircraft, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var current = _state.Current;
            if (aircraft is not null && current.Aircraft is not null && AircraftContinuity.IsSameLoadedAircraft(current.Aircraft, aircraft))
            {
                // Same loaded aircraft, new metadata: keep the session (its identity follows), never re-attach.
                _state.Set(current with { Aircraft = aircraft });
                return;
            }

            await CloseAsync(current.Session).ConfigureAwait(false);
            if (aircraft is null)
            {
                _state.Set(FsgapSessionState.None);
                return;
            }

            var resolution = _registry.Resolve(aircraft);
            switch (resolution.Status)
            {
                case ProviderResolutionStatus.NotSupported:
                    _state.Set(new FsgapSessionState(FsgapSessionStatus.NotSupported, aircraft, null, "No built-in provider supports this aircraft."));
                    return;
                case ProviderResolutionStatus.Ambiguous:
                    var claimants = string.Join(", ", resolution.Candidates.Select(c => c.Provider.ProviderId));
                    _logger.LogWarning("Several providers claim '{Title}' equally ({Providers}); no session is opened.", aircraft.Title, claimants);
                    _state.Set(new FsgapSessionState(FsgapSessionStatus.Ambiguous, aircraft, null, $"Several providers claim this aircraft equally: {claimants}."));
                    return;
            }

            try
            {
                var session = await resolution.Selected!.Provider.AttachAsync(aircraft, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _opened);
                _logger.LogInformation("Session opened for '{Title}' by provider {Provider}.", aircraft.Title, session.ProviderId);
                _state.Set(new FsgapSessionState(FsgapSessionStatus.Attached, aircraft, session));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Opening a session for '{Title}' failed.", aircraft.Title);
                _state.Set(new FsgapSessionState(FsgapSessionStatus.AttachFailed, aircraft, null, ex.Message));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

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
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseAsync(_state.Current.Session).ConfigureAwait(false);
            _state.Set(FsgapSessionState.None);
            _state.Complete();
        }
        finally
        {
            _gate.Release();
        }

        _stop.Dispose();
    }

    private async Task FollowAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var aircraft in _detector.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                await OnAircraftAsync(aircraft, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The aircraft follow loop stopped.");
        }
    }

    private async Task CloseAsync(IAircraftSession? session)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Disposing the {Provider} session failed.", session.ProviderId);
        }

        Interlocked.Increment(ref _disposedSessions);
    }
}
