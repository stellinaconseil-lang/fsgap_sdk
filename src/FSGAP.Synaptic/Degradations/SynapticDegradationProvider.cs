using System.Diagnostics;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FSGAP.Synaptic.Degradations;

/// <summary>
/// Controlled degradations of one attached Synaptic A220 (see <see cref="SynapticDegradationControls"/>): every command
/// reads the control, writes an explicit qualified value (never a toggle) and succeeds only once the value is read back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership.</b> The provider remembers the one degradation <em>this session</em> applied. A control already degraded
/// when FSGAP looks at it is <see cref="DegradationState.PreExisting"/>: never applied over, never restored, never released.
/// Ownership is dropped, never carried over, when the simulator disconnects or another aircraft is loaded (the detector
/// reports it); afterwards states are simply read again.
/// </para>
/// <para>
/// <b>One at a time.</b> Each degradation was qualified alone, so a second apply is rejected while one is owned.
/// </para>
/// <para>
/// <b>Disposal.</b> A clean dispose restores the owned degradation (best effort, bounded), only while the attached
/// aircraft is still the loaded one; it never writes to a replaced aircraft and never reports a restore it could not do.
/// </para>
/// </remarks>
internal sealed class SynapticDegradationProvider : IDegradationProvider, IAsyncDisposable
{
    /// <summary>Read-back and disposal limits.</summary>
    internal sealed record Timing(TimeSpan ReadbackTimeout, TimeSpan ReadbackInterval, TimeSpan DisposeRestoreTimeout)
    {
        public static Timing Default { get; } = new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
    }

    private readonly ISimulatorVariableReader _reader;
    private readonly ISimulatorVariableWriter _writer;
    private readonly IAircraftDetector? _detector;
    private readonly AircraftDescriptor _attached;
    private readonly ILogger _logger;
    private readonly Timing _timing;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watch;
    private SynapticDegradationControl? _owned;
    private int _disposed;

    internal SynapticDegradationProvider(
        ISimulatorVariableReader reader,
        ISimulatorVariableWriter writer,
        IAircraftDetector? detector,
        AircraftDescriptor attached,
        ILogger logger,
        Timing? timing = null)
    {
        _reader = reader;
        _writer = writer;
        _detector = detector;
        _attached = attached;
        _logger = logger;
        _timing = timing ?? Timing.Default;
        _watch = detector is null ? Task.CompletedTask : Task.Run(() => WatchAircraftAsync(_stop.Token));
    }

    /// <summary>What sessions with this provider declare.</summary>
    internal static DegradationCapabilities Capabilities { get; } = new() { Catalog = SynapticDegradationControls.Catalog, MaxActive = 1 };

    /// <summary>The degradation this session applied and still owns, if any.</summary>
    internal DegradationKey? OwnedKey => Volatile.Read(ref _owned)?.Key;

    /// <inheritdoc />
    public async Task<DegradationState> GetStateAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        var control = SynapticDegradationControls.Find(key)
            ?? throw new NotSupportedException($"'{key}' is not a controlled degradation of this aircraft.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || AircraftGone())
            {
                return DegradationState.Unavailable;
            }

            var raw = await TryReadAsync(control, cancellationToken).ConfigureAwait(false);
            if (raw is null)
            {
                return DegradationState.Unavailable;
            }

            if (ReferenceEquals(Volatile.Read(ref _owned), control) && Is(raw.Value, control.Normal))
            {
                // Set back to normal by someone else (typically the pilot): nothing left to own or to restore.
                Release(control, "found back at its normal value");
            }

            return Classify(control, raw.Value);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DegradationCommandResult> ApplyAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (SynapticDegradationControls.Find(key) is not { } control)
        {
            return Result(DegradationCommandStatus.NotSupported, DegradationState.Unavailable, $"'{key}' is not a controlled degradation of this aircraft.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Unreachable() is { } unreachable)
            {
                return unreachable;
            }

            var raw = await TryReadAsync(control, cancellationToken).ConfigureAwait(false);
            if (raw is null)
            {
                return Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, "The control could not be read; nothing was written.");
            }

            var owned = Volatile.Read(ref _owned);
            if (ReferenceEquals(owned, control))
            {
                if (Is(raw.Value, control.Degraded))
                {
                    return Result(DegradationCommandStatus.Succeeded, DegradationState.Applied, "Already applied by this session; nothing was written.");
                }

                Release(control, "found away from its degraded value");
                owned = null;
            }

            if (owned is not null)
            {
                return Result(DegradationCommandStatus.Rejected, Classify(control, raw.Value),
                    $"'{owned.Key}' is applied by this session; only one controlled degradation may be active at a time. Restore it first.");
            }

            if (Is(raw.Value, control.Degraded))
            {
                return Result(DegradationCommandStatus.Rejected, DegradationState.PreExisting,
                    "The control is already in its degraded configuration, set outside this session; FSGAP does not take ownership of it.");
            }

            if (!Is(raw.Value, control.Normal))
            {
                return Result(DegradationCommandStatus.Rejected, DegradationState.Unknown,
                    "The control holds a value that is neither its qualified normal nor its degraded value; nothing was written.");
            }

            var outcome = await WriteAndConfirmAsync(control, control.Degraded, owns: true, cancellationToken).ConfigureAwait(false);
            if (outcome.Status == DegradationCommandStatus.Succeeded)
            {
                _logger.LogInformation("Controlled degradation {Key} applied and confirmed.", control.Key);
            }

            return outcome;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DegradationCommandResult> RestoreAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (SynapticDegradationControls.Find(key) is not { } control)
        {
            return Result(DegradationCommandStatus.NotSupported, DegradationState.Unavailable, $"'{key}' is not a controlled degradation of this aircraft.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Unreachable() is { } unreachable)
            {
                return unreachable;
            }

            if (!ReferenceEquals(Volatile.Read(ref _owned), control))
            {
                var raw = await TryReadAsync(control, cancellationToken).ConfigureAwait(false);
                return Result(DegradationCommandStatus.Rejected, raw is null ? DegradationState.Unavailable : Classify(control, raw.Value),
                    "This degradation was not applied by this session; FSGAP restores only what it applied. Nothing was written.");
            }

            var outcome = await WriteAndConfirmAsync(control, control.Restore, owns: false, cancellationToken).ConfigureAwait(false);
            if (outcome.Status == DegradationCommandStatus.Succeeded)
            {
                _logger.LogInformation("Controlled degradation {Key} restored and confirmed.", control.Key);
            }

            return outcome;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Restores the owned degradation if the attached aircraft is still loaded (best effort, bounded), then stops.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        using var limit = new CancellationTokenSource(_timing.DisposeRestoreTimeout);
        var entered = false;
        try
        {
            await _gate.WaitAsync(limit.Token).ConfigureAwait(false);
            entered = true;
            if (Interlocked.Exchange(ref _owned, null) is { } owned)
            {
                if (AircraftGone())
                {
                    _logger.LogWarning("Controlled degradation {Key} not restored on dispose: the attached aircraft is no longer loaded.", owned.Key);
                }
                else
                {
                    var outcome = await WriteAndConfirmAsync(owned, owned.Restore, owns: false, limit.Token, track: false).ConfigureAwait(false);
                    if (outcome.Status == DegradationCommandStatus.Succeeded)
                    {
                        _logger.LogInformation("Controlled degradation {Key} restored on dispose.", owned.Key);
                    }
                    else
                    {
                        _logger.LogWarning("Controlled degradation {Key} could not be restored on dispose ({Status}): {Message}", owned.Key, outcome.Status, outcome.Message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Controlled degradation restore on dispose timed out after {Timeout}.", _timing.DisposeRestoreTimeout);
        }
        finally
        {
            if (entered)
            {
                _gate.Release();
            }
        }

        try
        {
            await _watch.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private static DegradationCommandResult Result(DegradationCommandStatus status, DegradationState state, string? message = null) => new(status, state, message);

    private static bool Is(double value, double expected) => Math.Abs(value - expected) < 0.5;

    private DegradationState Classify(SynapticDegradationControl control, double raw) =>
        Is(raw, control.Normal) ? DegradationState.Normal
        : Is(raw, control.Degraded) ? (ReferenceEquals(Volatile.Read(ref _owned), control) ? DegradationState.Applied : DegradationState.PreExisting)
        : DegradationState.Unknown;

    private DegradationCommandResult? Unreachable() =>
        Volatile.Read(ref _disposed) != 0 ? Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, "The session is disposed.")
        : AircraftGone() ? Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, "The attached aircraft is no longer loaded or the simulator is not connected; nothing was written.")
        : null;

    /// <summary>With a detector: no aircraft (disconnected) or a different aircraft than the attached one.</summary>
    private bool AircraftGone() =>
        _detector is not null && (_detector.Current is not { } loaded || !AircraftContinuity.IsSameLoadedAircraft(_attached, loaded));

    /// <summary>
    /// Writes <paramref name="value"/>, then reads until it is confirmed or the read-back time runs out. With
    /// <paramref name="owns"/>, the control is owned as soon as a write may have reached the aircraft; a confirmed restore
    /// releases it.
    /// </summary>
    private async Task<DegradationCommandResult> WriteAndConfirmAsync(SynapticDegradationControl control, double value, bool owns, CancellationToken cancellationToken, bool track = true)
    {
        try
        {
            await _writer.WriteAsync(control.Variable, value, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Result(DegradationCommandStatus.Unavailable, DegradationState.Unavailable, $"Not written: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (owns && track)
            {
                Volatile.Write(ref _owned, control);
            }

            _logger.LogWarning(ex, "Write of controlled degradation {Key} did not complete.", control.Key);
            return Result(DegradationCommandStatus.Unconfirmed, DegradationState.Unknown, $"The write did not complete ({ex.GetType().Name}); read the state before retrying.");
        }

        if (owns && track)
        {
            Volatile.Write(ref _owned, control);
        }

        var clock = Stopwatch.StartNew();
        double? last = null;
        while (true)
        {
            last = await TryReadAsync(control, cancellationToken).ConfigureAwait(false);
            if (last is { } raw && Is(raw, value))
            {
                if (!owns && track)
                {
                    Release(control, "restored");
                }

                return Result(DegradationCommandStatus.Succeeded, owns ? DegradationState.Applied : DegradationState.Normal);
            }

            if (clock.Elapsed >= _timing.ReadbackTimeout)
            {
                break;
            }

            await Task.Delay(_timing.ReadbackInterval, cancellationToken).ConfigureAwait(false);
        }

        var state = last is null ? DegradationState.Unavailable : Classify(control, last.Value);
        _logger.LogWarning("Controlled degradation {Key}: value {Value} written but not confirmed within {Timeout} (read back {ReadBack}).", control.Key, value, _timing.ReadbackTimeout, last);
        return Result(DegradationCommandStatus.Unconfirmed, state, "The write was sent but the read-back did not confirm it; read the state before retrying.");
    }

    private async Task<double?> TryReadAsync(SynapticDegradationControl control, CancellationToken cancellationToken)
    {
        try
        {
            var values = await _reader.ReadAsync([control.Variable], cancellationToken).ConfigureAwait(false);
            return values.Count == 1 && double.IsFinite(values[0]) ? values[0] : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Read of controlled degradation {Key} failed.", control.Key);
            return null;
        }
    }

    private void Release(SynapticDegradationControl control, string why)
    {
        if (Interlocked.CompareExchange(ref _owned, null, control) == control)
        {
            _logger.LogDebug("Controlled degradation {Key} released: {Why}.", control.Key, why);
        }
    }

    private async Task WatchAircraftAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var loaded in _detector!.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                if (loaded is null || !AircraftContinuity.IsSameLoadedAircraft(_attached, loaded))
                {
                    if (Interlocked.Exchange(ref _owned, null) is { } owned)
                    {
                        _logger.LogWarning(
                            "Ownership of controlled degradation {Key} dropped: {Why}. Its state will be read again, never assumed.",
                            owned.Key,
                            loaded is null ? "the simulator disconnected or the aircraft was unloaded" : "another aircraft was loaded");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Aircraft watch of the controlled degradations stopped.");
        }
    }
}
