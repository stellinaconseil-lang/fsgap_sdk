using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
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
/// the value found before the trigger) and succeeds once read back. Recipe provenance never gates execution.
/// </para>
/// <para>
/// <b>Active failures</b> are the recipes this session triggered whose controls still hold their degraded values. A
/// failure whose controls were set back by the pilot is no longer active (its ownership is dropped, nothing is rewritten).
/// Several failures may be active at once when they touch different controls.
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
    private int _disposed;

    internal SynapticFailureProvider(SynapticControlBoard board, bool ownsBoard)
    {
        _board = board;
        _ownsBoard = ownsBoard;
    }

    /// <summary>What sessions with this provider declare.</summary>
    internal static FailureCapabilities Capabilities { get; } = new() { CanReadActiveFailures = true, Catalog = SynapticFailureCatalog.Catalog };

    /// <summary>The failures this session triggered and still owns (diagnostics, tests).</summary>
    internal IReadOnlyList<FailureKey> OwnedKeys =>
        _board.Owners().Where(o => o.StartsWith(OwnerPrefix, StringComparison.Ordinal)).Select(o => FailureKey.Parse(o[OwnerPrefix.Length..])).ToArray();

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
                switch (await _board.StillAppliedCoreAsync(owner, cancellationToken).ConfigureAwait(false))
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
            return FailureCommandResult.NotSupported($"'{command.Key}' cannot be triggered on this aircraft (no documented Synaptic control, or unsupported target).");
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            return FailureCommandResult.Unavailable("The session is disposed.");
        }

        using (await _board.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            var outcome = await _board.ApplyCoreAsync(Owner(recipe), recipe.Controls, acceptPartiallyPreExisting: true, cancellationToken).ConfigureAwait(false);
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
            return FailureCommandResult.NotSupported($"'{command.Key}' cannot be cleared on this aircraft (no documented Synaptic control, or unsupported target).");
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
                    return FailureCommandResult.Succeeded;
                case BoardOutcome.Unavailable:
                    return FailureCommandResult.Unavailable(outcome.Message);
                default:
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
}
