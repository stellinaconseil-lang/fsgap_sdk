using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Simulator;
using FSGAP.Synaptic.Systems;
using Microsoft.Extensions.Logging;

namespace FSGAP.Synaptic.Degradations;

/// <summary>
/// Controlled degradations of one attached Synaptic A220 (see <see cref="SynapticDegradationControls"/>), on the shared
/// <see cref="SynapticControlBoard"/>: every command reads the control, writes an explicit qualified value (never a toggle)
/// and succeeds only once the value is read back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership.</b> The provider remembers the one degradation <em>this session</em> applied. A control already degraded
/// when FSGAP looks at it (by the pilot, or held by an active failure of the same session) is
/// <see cref="DegradationState.PreExisting"/>: never applied over, never restored, never released. Ownership is dropped,
/// never carried over, when the simulator disconnects or another aircraft is loaded; afterwards states are read again.
/// </para>
/// <para><b>One at a time.</b> Each degradation was qualified alone, so a second apply is rejected while one is owned.</para>
/// <para>
/// <b>Disposal.</b> A clean dispose restores the owned degradation (best effort, bounded), only while the attached aircraft
/// is still the loaded one; it never writes to a replaced aircraft and never reports a restore it could not do.
/// </para>
/// </remarks>
internal sealed class SynapticDegradationProvider : IDegradationProvider, IAsyncDisposable
{
    private const string OwnerPrefix = "degradation:";

    private readonly SynapticControlBoard _board;
    private readonly bool _ownsBoard;
    private int _disposed;

    /// <summary>A standalone provider with its own board.</summary>
    internal SynapticDegradationProvider(
        ISimulatorVariableReader reader,
        ISimulatorVariableWriter writer,
        IAircraftDetector? detector,
        AircraftDescriptor attached,
        ILogger logger,
        SynapticControlBoard.Timing? timing = null)
        : this(new SynapticControlBoard(reader, writer, detector, attached, logger, timing), ownsBoard: true)
    {
    }

    /// <summary>A provider on a board shared with the failure recipes; <paramref name="ownsBoard"/> disposes it with this provider.</summary>
    internal SynapticDegradationProvider(SynapticControlBoard board, bool ownsBoard)
    {
        _board = board;
        _ownsBoard = ownsBoard;
    }

    /// <summary>What sessions with this provider declare.</summary>
    internal static DegradationCapabilities Capabilities { get; } = new() { Catalog = SynapticDegradationControls.Catalog, MaxActive = 1 };

    /// <summary>The degradation this session applied and still owns, if any.</summary>
    internal DegradationKey? OwnedKey => OwnedDegradation()?.Key;

    /// <inheritdoc />
    public async Task<DegradationState> GetStateAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var degradation = SynapticDegradationControls.Find(key)
            ?? throw new NotSupportedException($"'{key}' is not a controlled degradation of this aircraft.");
        using (await _board.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Volatile.Read(ref _disposed) != 0 || _board.AircraftGone())
            {
                return DegradationState.Unavailable;
            }

            var raw = (await _board.ReadCoreAsync([degradation.Control], cancellationToken).ConfigureAwait(false))[0];
            if (raw is null)
            {
                return DegradationState.Unavailable;
            }

            if (Owns(degradation) && degradation.Control.IsNormal(raw.Value))
            {
                // Set back to normal by someone else (typically the pilot): nothing left to own or to restore.
                _board.ReleaseCore(Owner(degradation), "found back at its normal value");
            }

            return Classify(degradation, raw.Value);
        }
    }

    /// <inheritdoc />
    public async Task<DegradationCommandResult> ApplyAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (SynapticDegradationControls.Find(key) is not { } degradation)
        {
            return Result(DegradationCommandStatus.NotSupported, DegradationState.Unavailable, $"'{key}' is not a controlled degradation of this aircraft.");
        }

        using (await _board.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Unreachable() is { } unreachable)
            {
                return unreachable;
            }

            var raw = (await _board.ReadCoreAsync([degradation.Control], cancellationToken).ConfigureAwait(false))[0];
            if (raw is null)
            {
                return Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, "The control could not be read; nothing was written.");
            }

            var owned = OwnedDegradation();
            if (ReferenceEquals(owned, degradation))
            {
                if (degradation.Control.IsDegraded(raw.Value))
                {
                    return Result(DegradationCommandStatus.Succeeded, DegradationState.Applied, "Already applied by this session; nothing was written.");
                }

                _board.ReleaseCore(Owner(degradation), "found away from its degraded value");
                owned = null;
            }

            if (owned is not null)
            {
                return Result(DegradationCommandStatus.Rejected, Classify(degradation, raw.Value),
                    $"'{owned.Key}' is applied by this session; only one controlled degradation may be active at a time. Restore it first.");
            }

            if (_board.OwnerOf(degradation.Control) is { } holder)
            {
                return Result(DegradationCommandStatus.Rejected, Classify(degradation, raw.Value),
                    $"The control is held by '{holder}' in this session; clear it first. Nothing was written.");
            }

            if (degradation.Control.IsDegraded(raw.Value))
            {
                return Result(DegradationCommandStatus.Rejected, DegradationState.PreExisting,
                    "The control is already in its degraded configuration, set outside this session; FSGAP does not take ownership of it.");
            }

            if (!degradation.Control.IsNormal(raw.Value))
            {
                return Result(DegradationCommandStatus.Rejected, DegradationState.Unknown,
                    "The control holds a value that is neither its qualified normal nor its degraded value; nothing was written.");
            }

            var outcome = await _board.ApplyCoreAsync(Owner(degradation), [degradation.Control], acceptPartiallyPreExisting: false, cancellationToken).ConfigureAwait(false);
            var state = outcome.Values.Count == 1 && outcome.Values[0] is { } value ? Classify(degradation, value) : DegradationState.Unavailable;
            switch (outcome.Outcome)
            {
                case BoardOutcome.Applied or BoardOutcome.AlreadyApplied:
                    _board.Logger.LogInformation("Controlled degradation {Key} applied and confirmed.", degradation.Key);
                    return Result(DegradationCommandStatus.Succeeded, DegradationState.Applied);
                case BoardOutcome.Unavailable:
                    return Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, outcome.Message);
                case BoardOutcome.Unconfirmed:
                    return Result(DegradationCommandStatus.Unconfirmed, state, outcome.Message);
                default:
                    return Result(DegradationCommandStatus.Rejected, state, outcome.Message);
            }
        }
    }

    /// <inheritdoc />
    public async Task<DegradationCommandResult> RestoreAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (SynapticDegradationControls.Find(key) is not { } degradation)
        {
            return Result(DegradationCommandStatus.NotSupported, DegradationState.Unavailable, $"'{key}' is not a controlled degradation of this aircraft.");
        }

        using (await _board.LockAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Unreachable() is { } unreachable)
            {
                return unreachable;
            }

            if (!Owns(degradation))
            {
                var raw = (await _board.ReadCoreAsync([degradation.Control], cancellationToken).ConfigureAwait(false))[0];
                return Result(DegradationCommandStatus.Rejected, raw is null ? DegradationState.Unavailable : Classify(degradation, raw.Value),
                    "This degradation was not applied by this session; FSGAP restores only what it applied. Nothing was written.");
            }

            var outcome = await _board.RestoreCoreAsync(Owner(degradation), cancellationToken).ConfigureAwait(false);
            switch (outcome.Outcome)
            {
                case BoardOutcome.Restored:
                    _board.Logger.LogInformation("Controlled degradation {Key} restored and confirmed.", degradation.Key);
                    return Result(DegradationCommandStatus.Succeeded, DegradationState.Normal);
                case BoardOutcome.Unavailable:
                    return Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, outcome.Message);
                default:
                    var state = outcome.Values.Count == 1 && outcome.Values[0] is { } value ? Classify(degradation, value) : DegradationState.Unknown;
                    return Result(DegradationCommandStatus.Unconfirmed, state, outcome.Message);
            }
        }
    }

    /// <summary>Restores the owned degradation if the attached aircraft is still loaded (best effort, bounded), then stops.</summary>
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
                if (OwnedDegradation() is { } owned)
                {
                    if (_board.AircraftGone())
                    {
                        _board.Logger.LogWarning("Controlled degradation {Key} not restored on dispose: the attached aircraft is no longer loaded.", owned.Key);
                        _board.ReleaseCore(Owner(owned), "session disposed with the aircraft gone");
                    }
                    else
                    {
                        var outcome = await _board.RestoreCoreAsync(Owner(owned), limit.Token).ConfigureAwait(false);
                        if (outcome.Outcome == BoardOutcome.Restored)
                        {
                            _board.Logger.LogInformation("Controlled degradation {Key} restored on dispose.", owned.Key);
                        }
                        else
                        {
                            _board.Logger.LogWarning("Controlled degradation {Key} could not be restored on dispose ({Outcome}): {Message}", owned.Key, outcome.Outcome, outcome.Message);
                            _board.ReleaseCore(Owner(owned), "session disposed");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _board.Logger.LogWarning("Controlled degradation restore on dispose timed out after {Timeout}.", _board.Times.DisposeRestoreTimeout);
        }

        if (_ownsBoard)
        {
            await _board.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static string Owner(SynapticDegradationControl degradation) => OwnerPrefix + degradation.Key.Value;

    private static DegradationCommandResult Result(DegradationCommandStatus status, DegradationState state, string? message = null) => new(status, state, message);

    private bool Owns(SynapticDegradationControl degradation) => _board.OwnedBy(Owner(degradation)).Count > 0;

    private SynapticDegradationControl? OwnedDegradation() =>
        _board.Owners().Where(o => o.StartsWith(OwnerPrefix, StringComparison.Ordinal))
            .Select(o => SynapticDegradationControls.Find(DegradationKey.Parse(o[OwnerPrefix.Length..])))
            .FirstOrDefault(d => d is not null);

    private DegradationState Classify(SynapticDegradationControl degradation, double raw) =>
        degradation.Control.IsNormal(raw) ? DegradationState.Normal
        : degradation.Control.IsDegraded(raw) ? (Owns(degradation) ? DegradationState.Applied : DegradationState.PreExisting)
        : DegradationState.Unknown;

    private DegradationCommandResult? Unreachable() =>
        Volatile.Read(ref _disposed) != 0 ? Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, "The session is disposed.")
        : _board.AircraftGone() ? Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, "The attached aircraft is no longer loaded or the simulator is not connected; nothing was written.")
        : null;
}
