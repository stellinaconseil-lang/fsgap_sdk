using System.Collections.Concurrent;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
using FSGAP.Synaptic.CircuitBreakers;
using FSGAP.Synaptic.Systems;
using Microsoft.Extensions.Logging;

namespace FSGAP.Synaptic.Failures;

/// <summary>
/// Normalized failures on one attached Synaptic A220: the Fenix key set (<see cref="SynapticFailureCatalog"/>), each
/// executable key realized by forcing documented A22X controls on the shared <see cref="SynapticControlBoard"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Trigger</b> reads the recipe's controls, refuses an unexpected value or a control held by another failure or
/// degradation (conflict), leaves controls already degraded outside FSGAP alone, writes the degraded values, and succeeds
/// once they are read back. <b>Clear</b> writes back what this session's trigger changed (the qualified normal value, or
/// the value found before the trigger) and succeeds once read back. Recipe provenance never gates the historical recipes.
/// </para>
/// <para>
/// <b>Circuit breakers</b> (0.13.0-preview.4) are single-cell recipes on the same board: trigger reads the breaker cell,
/// writes nothing if it is already out, otherwise writes 1 and succeeds once it reads 1; clear writes it back to 0 and
/// succeeds once it reads 0. All 55 are listed; only those the session's <see cref="SynapticCircuitBreakerActivation"/>
/// enables declare operations, the others answer <c>NotSupported</c> without reading or writing anything. The Synaptic
/// version is never a condition. Opening a session reads no breaker cell (an unknown L: can be created by a read, so a
/// cell is read only when its breaker is used). A breaker whose cell cannot be read while the aircraft is attached, holds a
/// value that is neither 0 nor 1, or does not take a write is disabled for the rest of the session, alone: the other
/// breakers and every other recipe keep working, and an ambiguous write is never repeated automatically.
/// </para>
/// <para>
/// <b>Active failures</b> are the recipes this session triggered whose controls still hold their degraded values. A
/// failure whose controls were set back by the pilot is no longer active (its ownership is dropped, nothing is rewritten).
/// Several failures may be active at once when they touch different controls. A tripped breaker whose cell can no longer
/// be read is still reported active (its reset was never confirmed), so one unreadable cell never hides the others.
/// </para>
/// <para>
/// <b>Disposal</b> clears, best effort and bounded, what this session triggered, only while the attached aircraft is still
/// loaded: ownership lives in memory, so a later session could not clear it.
/// </para>
/// </remarks>
internal sealed class SynapticFailureProvider : IFailureProvider, IAsyncDisposable
{
    private const string OwnerPrefix = "failure:";

    private readonly SynapticControlBoard _board;
    private readonly bool _ownsBoard;
    private readonly SynapticCircuitBreakerActivation _circuitBreakers;
    private readonly ConcurrentDictionary<string, BreakerState> _breakers = new(StringComparer.Ordinal);
    private int _disposed;

    internal SynapticFailureProvider(SynapticControlBoard board, bool ownsBoard, SynapticCircuitBreakerActivation? circuitBreakers = null)
    {
        _board = board;
        _ownsBoard = ownsBoard;
        _circuitBreakers = circuitBreakers ?? SynapticCircuitBreakerActivation.Default;
        Capabilities = ReferenceEquals(_circuitBreakers, SynapticCircuitBreakerActivation.Default)
            ? DefaultCapabilities
            : new() { CanReadActiveFailures = true, Catalog = SynapticFailureCatalog.CatalogFor(_circuitBreakers) };
        DiscoverCircuitBreakers();
    }

    /// <summary>What a session declares without breaker options: the manifest-qualified breakers are executable.</summary>
    internal static FailureCapabilities DefaultCapabilities { get; } =
        new() { CanReadActiveFailures = true, Catalog = SynapticFailureCatalog.CatalogFor(SynapticCircuitBreakerActivation.Default) };

    /// <summary>What this session declares: the catalog, with the activated circuit breakers executable.</summary>
    internal FailureCapabilities Capabilities { get; }

    /// <summary>The failures this session triggered and still owns (diagnostics, tests).</summary>
    internal IReadOnlyList<FailureKey> OwnedKeys =>
        _board.Owners().Where(o => o.StartsWith(OwnerPrefix, StringComparison.Ordinal)).Select(o => FailureKey.Parse(o[OwnerPrefix.Length..])).ToArray();

    /// <summary>The 55 breakers as this session sees them (diagnostics, tests).</summary>
    internal IReadOnlyList<SynapticCircuitBreakerStatus> CircuitBreakerStatuses() =>
        SynapticFailureCatalog.CircuitBreakerRecipes.Select(r => StatusOf(r.CircuitBreaker!, r.Key)).ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<AircraftFailure>> GetActiveFailuresAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using (await _board.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_board.AircraftGone())
            {
                throw new FailuresUnavailableException("The aircraft this session was attached to is no longer loaded or the simulator is not connected.");
            }

            var active = new List<AircraftFailure>();
            foreach (var owner in _board.Owners().Where(o => o.StartsWith(OwnerPrefix, StringComparison.Ordinal)))
            {
                var recipe = SynapticFailureCatalog.Find(FailureKey.Parse(owner[OwnerPrefix.Length..]))!;
                var applied = await _board.StillAppliedCoreAsync(owner, cancellationToken).ConfigureAwait(false);
                if (applied is null && recipe.CircuitBreaker is { } breaker && CellProblem())
                {
                    // One unreadable breaker cell: its trip was never reset, so it stays reported, and it never hides the others.
                    Disable(breaker, SynapticBreakerInterface.Unavailable, "its cell could not be read");
                    applied = true;
                }

                switch (applied)
                {
                    case null:
                        throw new FailuresUnavailableException($"The controls of '{recipe.Key}' cannot be read.");
                    case true:
                        active.Add(new AircraftFailure
                        {
                            Key = recipe.Key,
                            Target = recipe.Definition.SupportedTargets[0],
                            Category = recipe.Definition.Category,
                            Description = recipe.Definition.DisplayName,
                        });
                        break;
                }
            }

            return active;
        }
    }

    /// <inheritdoc />
    public async Task<FailureCommandResult> TriggerAsync(FailureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (SynapticFailureCatalog.Find(command.Key) is not { } recipe || !Capabilities.CanTrigger(command))
        {
            return FailureCommandResult.NotSupported(NotExecutable(command, "triggered"));
        }

        if (recipe.CircuitBreaker is { } disabled && State(disabled).DisabledReason is { } why)
        {
            return FailureCommandResult.NotSupported($"'{recipe.Key}' ({disabled.Id}) is disabled for this session: {why}. Nothing was read or written.");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return FailureCommandResult.Unavailable("The session is disposed.");
        }

        using (await _board.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var outcome = await _board.ApplyCoreAsync(Owner(recipe), recipe.Controls, acceptPartiallyPreExisting: true, cancellationToken).ConfigureAwait(false);
            if (recipe.CircuitBreaker is { } breaker)
            {
                return TripResult(recipe, breaker, outcome);
            }

            switch (outcome.Outcome)
            {
                case BoardOutcome.Applied:
                    _board.Logger.LogInformation("Failure {Key} triggered ({Mechanism}, {Qualification}) and confirmed.", recipe.Key, recipe.Mechanism, recipe.Qualification);
                    return FailureCommandResult.Succeeded;
                case BoardOutcome.AlreadyApplied:
                    return FailureCommandResult.Succeeded;
                case BoardOutcome.Unavailable:
                    return FailureCommandResult.Unavailable(outcome.Message);
                case BoardOutcome.Unconfirmed:
                    return FailureCommandResult.Unconfirmed(outcome.Message);
                case BoardOutcome.Conflict:
                    return FailureCommandResult.Rejected($"'{recipe.Key}' needs a control already held by '{outcome.ConflictOwner}'; clear it first. Nothing was written.");
                default:
                    return FailureCommandResult.Rejected($"'{recipe.Key}' cannot be triggered in the current configuration: {outcome.Message}");
            }
        }
    }

    /// <inheritdoc />
    public async Task<FailureCommandResult> ClearAsync(FailureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (SynapticFailureCatalog.Find(command.Key) is not { } recipe || !Capabilities.CanClear(command))
        {
            return FailureCommandResult.NotSupported(NotExecutable(command, "cleared"));
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return FailureCommandResult.Unavailable("The session is disposed.");
        }

        using (await _board.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (_board.AircraftGone())
            {
                return FailureCommandResult.Unavailable("The attached aircraft is no longer loaded or the simulator is not connected; nothing was written.");
            }

            if (_board.OwnedBy(Owner(recipe)).Count == 0)
            {
                var values = await _board.ReadCoreAsync(recipe.Controls, cancellationToken).ConfigureAwait(false);
                if (values.Any(v => v is null))
                {
                    if (recipe.CircuitBreaker is { } unreadable && CellProblem())
                    {
                        Disable(unreadable, SynapticBreakerInterface.Unavailable, "its cell could not be read");
                        return FailureCommandResult.Rejected($"The cell of {unreadable.Id} could not be read while the aircraft is attached; the breaker is disabled for this session. Nothing was written.");
                    }

                    return FailureCommandResult.Unavailable("The controls could not be read; nothing was written.");
                }

                return recipe.Controls.Select((c, i) => c.IsDegraded(values[i]!.Value)).Any(d => d)
                    ? FailureCommandResult.Rejected($"'{recipe.Key}' was not triggered by this session (its configuration was set outside FSGAP); FSGAP clears only what it triggered. Nothing was written.")
                    : FailureCommandResult.Succeeded;
            }

            var outcome = await _board.RestoreCoreAsync(Owner(recipe), cancellationToken).ConfigureAwait(false);
            switch (outcome.Outcome)
            {
                case BoardOutcome.Restored or BoardOutcome.NotOwned:
                    _board.Logger.LogInformation("Failure {Key} cleared and confirmed.", recipe.Key);
                    if (recipe.CircuitBreaker is { } reset)
                    {
                        Record(reset, SynapticBreakerInterface.Accessible, SynapticBreakerExecution.Succeeded, "reset: cell read back 0");
                    }

                    return FailureCommandResult.Succeeded;
                case BoardOutcome.Unavailable:
                    return FailureCommandResult.Unavailable(outcome.Message);
                default:
                    if (recipe.CircuitBreaker is { } ambiguous)
                    {
                        // Still owned (the board keeps the claim): an explicit clear can be retried; no new trip of this breaker.
                        Disable(ambiguous, SynapticBreakerInterface.WriteIneffective, "its reset (cell = 0) was not confirmed", SynapticBreakerExecution.Failed);
                    }

                    return FailureCommandResult.Unconfirmed(outcome.Message);
            }
        }
    }

    /// <summary>Clears what this session triggered if the attached aircraft is still loaded (best effort, bounded), then stops.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        using var limit = new CancellationTokenSource(_board.Times.DisposeRestoreTimeout);
        try
        {
            using (await _board.LockAsync(limit.Token).ConfigureAwait(false))
            {
                foreach (var owner in _board.Owners().Where(o => o.StartsWith(OwnerPrefix, StringComparison.Ordinal)))
                {
                    if (_board.AircraftGone())
                    {
                        _board.Logger.LogWarning("Failure {Owner} not cleared on dispose: the attached aircraft is no longer loaded.", owner);
                    }
                    else
                    {
                        var outcome = await _board.RestoreCoreAsync(owner, limit.Token).ConfigureAwait(false);
                        if (outcome.Outcome != BoardOutcome.Restored)
                        {
                            _board.Logger.LogWarning("Failure {Owner} could not be cleared on dispose ({Outcome}): {Message}", owner, outcome.Outcome, outcome.Message);
                        }
                    }

                    _board.ReleaseCore(owner, "session disposed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _board.Logger.LogWarning("Failure clear on dispose timed out after {Timeout}.", _board.Times.DisposeRestoreTimeout);
        }

        if (_ownsBoard)
        {
            await _board.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string Owner(SynapticFailureRecipe recipe) => OwnerPrefix + recipe.Key.Value;

    /// <summary>
    /// Session start: no simulator access. The provider is already identified (only an aircraft the Synaptic recognizer
    /// accepted gets this provider); the breaker manifest and the session catalog must agree; every cell starts
    /// <see cref="SynapticBreakerInterface.Unverified"/> and is read only when its breaker is used.
    /// </summary>
    private void DiscoverCircuitBreakers()
    {
        foreach (var recipe in SynapticFailureCatalog.CircuitBreakerRecipes)
        {
            var breaker = recipe.CircuitBreaker!;
            var operations = _circuitBreakers.IsEnabled(breaker) ? FailureOperations.Trigger | FailureOperations.Clear : FailureOperations.None;
            if (!Capabilities.Catalog.TryGet(recipe.Key, out var definition) || definition is null || definition.Operations != operations
                || !ReferenceEquals(breaker.Control, recipe.Controls.Single()))
            {
                throw new InvalidOperationException($"The circuit-breaker catalog is inconsistent for {breaker.Id}.");
            }
        }

        _board.Logger.LogInformation(
            "Synaptic circuit breakers: {Listed} listed, {Executable} executable (direct trip qualified), cells verified on first use; manifest qualified on Synaptic A220 {Qualified}, reported version {Reported} (diagnostic only, never a condition).",
            SynapticFailureCatalog.CircuitBreakerRecipes.Count,
            _circuitBreakers.Enabled.Count,
            _circuitBreakers.QualifiedOnVersion,
            _circuitBreakers.ReportedVersion ?? "unknown");
    }

    /// <summary>Maps a board outcome of a breaker trip, recording the breaker's interface and execution.</summary>
    private FailureCommandResult TripResult(SynapticFailureRecipe recipe, SynapticCircuitBreaker breaker, BoardResult outcome)
    {
        switch (outcome.Outcome)
        {
            case BoardOutcome.Applied:
                Record(breaker, SynapticBreakerInterface.Accessible, SynapticBreakerExecution.Succeeded, "trip: cell read back 1");
                _board.Logger.LogInformation("Circuit breaker {Id} tripped ({Cell} = 1 confirmed).", breaker.Id, breaker.Control.Variable.Name);
                return FailureCommandResult.Succeeded;
            case BoardOutcome.AlreadyApplied:
                Record(breaker, SynapticBreakerInterface.Accessible, SynapticBreakerExecution.Succeeded, "trip: already out by this session, nothing written");
                return FailureCommandResult.Succeeded;
            case BoardOutcome.PreExisting:
                Record(breaker, SynapticBreakerInterface.Accessible, null, null);
                return FailureCommandResult.Rejected($"{breaker.Id} is already out (pulled outside FSGAP): nothing was written, and FSGAP will not reset it.");
            case BoardOutcome.UnexpectedValue:
                Disable(breaker, SynapticBreakerInterface.IncoherentValue, "its cell holds a value that is neither 0 nor 1");
                return FailureCommandResult.Rejected($"The cell of {breaker.Id} holds a value that is neither 0 nor 1; nothing was written and the breaker is disabled for this session.");
            case BoardOutcome.Unconfirmed:
                Disable(breaker, SynapticBreakerInterface.WriteIneffective, "its trip (cell = 1) was not confirmed", SynapticBreakerExecution.Failed);
                return FailureCommandResult.Unconfirmed($"{outcome.Message} {breaker.Id} is disabled for this session and will not be written again automatically.");
            case BoardOutcome.Unavailable when outcome.Values.Any(v => v is null) && CellProblem():
                Disable(breaker, SynapticBreakerInterface.Unavailable, "its cell could not be read", SynapticBreakerExecution.Failed);
                return FailureCommandResult.Rejected($"The cell of {breaker.Id} could not be read while the aircraft is attached; nothing was written and the breaker is disabled for this session.");
            case BoardOutcome.Unavailable:
                return FailureCommandResult.Unavailable(outcome.Message);
            default:
                return FailureCommandResult.Rejected($"'{recipe.Key}' cannot be triggered in the current configuration: {outcome.Message}");
        }
    }

    /// <summary>
    /// After an unreadable value inside the lock: true when the simulator answered and the aircraft is still attached, so the
    /// problem is this cell, not the connection (a whole-read failure or a gone aircraft never disables a breaker).
    /// </summary>
    private bool CellProblem() => !_board.LastReadFaulted && !_board.AircraftGone() && !_board.Disposed;

    private BreakerState State(SynapticCircuitBreaker breaker) => _breakers.GetOrAdd(breaker.Id, _ => new BreakerState());

    private void Record(SynapticCircuitBreaker breaker, SynapticBreakerInterface state, SynapticBreakerExecution? execution, string? message)
    {
        var current = State(breaker);
        lock (current)
        {
            current.Interface = state;
            if (execution is { } done)
            {
                current.Execution = done;
                current.Message = message;
            }
        }
    }

    private void Disable(SynapticCircuitBreaker breaker, SynapticBreakerInterface state, string why, SynapticBreakerExecution? execution = null)
    {
        var current = State(breaker);
        lock (current)
        {
            current.Interface = state;
            current.DisabledReason ??= why;
            if (execution is { } done)
            {
                current.Execution = done;
                current.Message = why;
            }
        }

        _board.Logger.LogWarning("Circuit breaker {Id} disabled for this session: {Why}. The other breakers and recipes are not affected.", breaker.Id, why);
    }

    private SynapticCircuitBreakerStatus StatusOf(SynapticCircuitBreaker breaker, FailureKey key)
    {
        var current = State(breaker);
        lock (current)
        {
            var enabled = _circuitBreakers.IsEnabled(breaker);
            return new SynapticCircuitBreakerStatus(
                breaker.Id,
                key,
                Listed: Capabilities.Catalog.TryGet(key, out _),
                current.Interface,
                breaker.Trip,
                Executable: enabled && current.DisabledReason is null,
                NotExecutableReason: !enabled ? _circuitBreakers.ReasonFor(breaker) : current.DisabledReason,
                current.Execution,
                current.Message,
                breaker.MasterCaution ? SynapticBreakerFunctionalEffect.MasterCautionInLab : SynapticBreakerFunctionalEffect.NotObserved);
        }
    }

    private string NotExecutable(FailureCommand command, string operation) =>
        SynapticFailureCatalog.Find(command.Key) is { CircuitBreaker: { } breaker } && !_circuitBreakers.IsEnabled(breaker)
            ? $"'{command.Key}' ({breaker.Id}) is listed but cannot be {operation} in this session: {_circuitBreakers.ReasonFor(breaker)}. Nothing was read or written."
            : $"'{command.Key}' cannot be {operation} on this aircraft (no documented Synaptic control, or unsupported target).";

    /// <summary>Per-session state of one breaker; mutated under its own lock.</summary>
    private sealed class BreakerState
    {
        public SynapticBreakerInterface Interface { get; set; } = SynapticBreakerInterface.Unverified;

        public SynapticBreakerExecution Execution { get; set; } = SynapticBreakerExecution.NotAttempted;

        public string? Message { get; set; }

        public string? DisabledReason { get; set; }
    }
}
