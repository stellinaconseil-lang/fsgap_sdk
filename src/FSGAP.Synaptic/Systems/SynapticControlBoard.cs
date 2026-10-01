using System.Diagnostics;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Sessions;
using Microsoft.Extensions.Logging;

namespace FSGAP.Synaptic.Systems;

/// <summary>Outcome of a board operation.</summary>
internal enum BoardOutcome
{
    /// <summary>Every control written and read back degraded.</summary>
    Applied,

    /// <summary>The owner already holds the controls in their degraded value; nothing written.</summary>
    AlreadyApplied,

    /// <summary>The controls are already degraded but not by this owner; nothing written, nothing claimed.</summary>
    PreExisting,

    /// <summary>A control holds a value that is neither normal nor degraded; nothing written.</summary>
    UnexpectedValue,

    /// <summary>A control is held by another owner; nothing written.</summary>
    Conflict,

    /// <summary>The controls cannot be reached (not connected, aircraft gone, read failed); nothing written.</summary>
    Unavailable,

    /// <summary>A write was sent but not confirmed; the touched controls stay owned so they can be restored.</summary>
    Unconfirmed,

    /// <summary>Every owned control written back and read back restored; ownership released.</summary>
    Restored,

    /// <summary>The owner holds no control.</summary>
    NotOwned,
}

/// <summary>Result of a board operation: the outcome and the last values read for the controls concerned.</summary>
internal sealed record BoardResult(BoardOutcome Outcome, IReadOnlyList<double?> Values, string? Message = null, string? ConflictOwner = null);

/// <summary>
/// The one place that writes A22X controls for an attached Synaptic A220, shared by the controlled degradations and the
/// failure recipes: explicit set values only, read before write, read back before success, and per-control ownership.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ownership.</b> Each control is held by at most one owner (a degradation or a failure); its value before the write is
/// remembered. A control already degraded when an owner applies is never claimed. Ownership is claimed just before a write
/// (so a write whose outcome is unknown can still be restored), released once a restore is read back, dropped when the
/// owner finds its controls back to normal (the pilot restored them), and dropped for every owner when the simulator
/// disconnects or another aircraft is loaded. Never persisted.
/// </para>
/// <para>
/// <b>Locking.</b> Callers take <see cref="LockAsync"/> and use the <c>*Core</c> members inside it, so a provider's own rule
/// (for example one degradation at a time) and the board's state are checked and changed atomically.
/// </para>
/// </remarks>
internal sealed class SynapticControlBoard : IAsyncDisposable
{
    /// <summary>Read-back and disposal limits.</summary>
    internal sealed record Timing(TimeSpan ReadbackTimeout, TimeSpan ReadbackInterval, TimeSpan DisposeRestoreTimeout)
    {
        public static Timing Default { get; } = new(TimeSpan.FromSeconds(3), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(5));
    }

    private sealed record Claim(string Owner, double Original);

    private readonly ISimulatorVariableReader _reader;
    private readonly ISimulatorVariableWriter _writer;
    private readonly IAircraftDetector? _detector;
    private readonly AircraftDescriptor _attached;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<SynapticControl, Claim> _claims = new(ReferenceEqualityComparer.Instance);
    private readonly object _claimsLock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _watch;
    private int _disposed;

    internal SynapticControlBoard(
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
        Times = timing ?? Timing.Default;
        _watch = detector is null ? Task.CompletedTask : Task.Run(() => WatchAircraftAsync(_stop.Token));
    }

    internal Timing Times { get; }

    internal ILogger Logger => _logger;

    internal bool Disposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Serializes board operations; dispose the returned handle to release.</summary>
    internal async Task<IDisposable> LockAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_gate);
    }

    /// <summary>With a detector: no aircraft (disconnected) or a different aircraft than the attached one.</summary>
    internal bool AircraftGone() =>
        _detector is not null && (_detector.Current is not { } loaded || !AircraftContinuity.IsSameLoadedAircraft(_attached, loaded));

    internal string? OwnerOf(SynapticControl control)
    {
        lock (_claimsLock)
        {
            return _claims.TryGetValue(control, out var claim) ? claim.Owner : null;
        }
    }

    internal IReadOnlyList<SynapticControl> OwnedBy(string owner)
    {
        lock (_claimsLock)
        {
            return _claims.Where(c => c.Value.Owner == owner).Select(c => c.Key).ToArray();
        }
    }

    internal IReadOnlyList<string> Owners()
    {
        lock (_claimsLock)
        {
            return _claims.Values.Select(c => c.Owner).Distinct(StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>Drops every claim of <paramref name="owner"/> without writing.</summary>
    internal void ReleaseCore(string owner, string why)
    {
        int released;
        lock (_claimsLock)
        {
            var keys = _claims.Where(c => c.Value.Owner == owner).Select(c => c.Key).ToArray();
            foreach (var key in keys)
            {
                _claims.Remove(key);
            }

            released = keys.Length;
        }

        if (released > 0)
        {
            _logger.LogDebug("{Owner} released {Count} control(s): {Why}.", owner, released, why);
        }
    }

    /// <summary>Reads the controls in one request; every value is <see langword="null"/> when the read fails.</summary>
    internal async Task<double?[]> ReadCoreAsync(IReadOnlyList<SynapticControl> controls, CancellationToken cancellationToken)
    {
        try
        {
            var values = await _reader.ReadAsync(controls.Select(c => c.Variable).ToArray(), cancellationToken).ConfigureAwait(false);
            return values.Count == controls.Count ? values.Select(v => double.IsFinite(v) ? v : (double?)null).ToArray() : new double?[controls.Count];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Read of {Count} A22X control(s) failed.", controls.Count);
            return new double?[controls.Count];
        }
    }

    /// <summary>
    /// Forces <paramref name="controls"/> to their degraded values for <paramref name="owner"/>. With
    /// <paramref name="acceptPartiallyPreExisting"/>, controls already degraded are left alone (not claimed) as long as at
    /// least one control is written; otherwise any already-degraded control refuses the whole operation.
    /// </summary>
    internal async Task<BoardResult> ApplyCoreAsync(string owner, IReadOnlyList<SynapticControl> controls, bool acceptPartiallyPreExisting, CancellationToken cancellationToken)
    {
        if (Disposed || AircraftGone())
        {
            return new(BoardOutcome.Unavailable, new double?[controls.Count], "The attached aircraft is no longer loaded or the simulator is not connected; nothing was written.");
        }

        var values = await ReadCoreAsync(controls, cancellationToken).ConfigureAwait(false);
        if (values.Any(v => v is null))
        {
            return new(BoardOutcome.Unavailable, values, "The controls could not be read; nothing was written.");
        }

        var conflict = controls.Select(OwnerOf).FirstOrDefault(o => o is not null && o != owner);
        if (conflict is not null)
        {
            return new(BoardOutcome.Conflict, values, $"A control it needs is held by '{conflict}'; nothing was written.", conflict);
        }

        var mine = controls.Where(c => OwnerOf(c) == owner).ToArray();
        if (mine.Length > 0)
        {
            if (mine.All(c => c.IsDegraded(values[IndexOf(controls, c)]!.Value)))
            {
                return new(BoardOutcome.AlreadyApplied, values, "Already applied by this owner; nothing was written.");
            }

            ReleaseCore(owner, "found away from its degraded values");
        }

        var toWrite = new List<(SynapticControl Control, double Original)>();
        var preExisting = 0;
        for (var i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            var value = values[i]!.Value;
            if (control.IsDegraded(value))
            {
                preExisting++;
            }
            else if (!control.IsNormal(value))
            {
                return new(BoardOutcome.UnexpectedValue, values, $"The {control.Name} holds a value that is neither its normal nor its degraded value; nothing was written.");
            }
            else
            {
                toWrite.Add((control, value));
            }
        }

        if (toWrite.Count == 0 || (preExisting > 0 && !acceptPartiallyPreExisting))
        {
            return new(BoardOutcome.PreExisting, values, "The configuration is already degraded, set outside this owner; nothing was written and nothing is claimed.");
        }

        var written = 0;
        foreach (var (control, original) in toWrite)
        {
            lock (_claimsLock)
            {
                _claims[control] = new Claim(owner, original);
            }

            try
            {
                await _writer.WriteAsync(control.Variable, control.Degraded, cancellationToken).ConfigureAwait(false);
                written++;
            }
            catch (InvalidOperationException ex)
            {
                lock (_claimsLock)
                {
                    _claims.Remove(control);
                }

                return written == 0
                    ? new(BoardOutcome.Unavailable, values, $"Not written: {ex.Message}")
                    : new(BoardOutcome.Unconfirmed, values, $"Only {written} of {toWrite.Count} controls were written ({ex.Message}); they stay owned so they can be restored.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "{Owner}: write of the {Control} did not complete.", owner, control.Name);
                return new(BoardOutcome.Unconfirmed, values, $"The write of the {control.Name} did not complete ({ex.GetType().Name}); read the state before retrying.");
            }
        }

        var confirmed = await ConfirmCoreAsync(toWrite.Select(w => (w.Control, w.Control.Degraded)).ToArray(), cancellationToken).ConfigureAwait(false);
        var final = await ReadCoreAsync(controls, cancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            _logger.LogWarning("{Owner}: degraded values written but not confirmed within {Timeout}.", owner, Times.ReadbackTimeout);
            return new(BoardOutcome.Unconfirmed, final, "The write was sent but the read-back did not confirm it; read the state before retrying.");
        }

        return new(BoardOutcome.Applied, final);
    }

    /// <summary>Writes back every control <paramref name="owner"/> holds, then reads them back; releases on confirmation.</summary>
    internal async Task<BoardResult> RestoreCoreAsync(string owner, CancellationToken cancellationToken)
    {
        (SynapticControl Control, double Target)[] owned;
        lock (_claimsLock)
        {
            owned = _claims.Where(c => c.Value.Owner == owner).Select(c => (c.Key, c.Key.RestoreValue(c.Value.Original))).ToArray();
        }

        if (owned.Length == 0)
        {
            return new(BoardOutcome.NotOwned, [], "Nothing is held by this owner; nothing was written.");
        }

        if (AircraftGone())
        {
            return new(BoardOutcome.Unavailable, new double?[owned.Length], "The attached aircraft is no longer loaded or the simulator is not connected; nothing was written.");
        }

        var written = 0;
        foreach (var (control, target) in owned)
        {
            try
            {
                await _writer.WriteAsync(control.Variable, target, cancellationToken).ConfigureAwait(false);
                written++;
            }
            catch (InvalidOperationException ex)
            {
                return new(written == 0 ? BoardOutcome.Unavailable : BoardOutcome.Unconfirmed, new double?[owned.Length], $"Not restored: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "{Owner}: restore write of the {Control} did not complete.", owner, control.Name);
                return new(BoardOutcome.Unconfirmed, new double?[owned.Length], $"The restore of the {control.Name} did not complete ({ex.GetType().Name}); read the state before retrying.");
            }
        }

        var confirmed = await ConfirmCoreAsync(owned, cancellationToken).ConfigureAwait(false);
        var final = await ReadCoreAsync(owned.Select(o => o.Control).ToArray(), cancellationToken).ConfigureAwait(false);
        if (!confirmed)
        {
            _logger.LogWarning("{Owner}: normal values written but not confirmed within {Timeout}.", owner, Times.ReadbackTimeout);
            return new(BoardOutcome.Unconfirmed, final, "The restore was sent but the read-back did not confirm it; read the state before retrying.");
        }

        ReleaseCore(owner, "restored");
        return new(BoardOutcome.Restored, final);
    }

    /// <summary>
    /// Whether <paramref name="owner"/>'s controls are still all degraded. When one is back to normal (the pilot restored it)
    /// the owner's claims are dropped without writing. <see langword="null"/> when the controls cannot be read.
    /// </summary>
    internal async Task<bool?> StillAppliedCoreAsync(string owner, CancellationToken cancellationToken)
    {
        var owned = OwnedBy(owner);
        if (owned.Count == 0)
        {
            return false;
        }

        if (AircraftGone())
        {
            return null;
        }

        var values = await ReadCoreAsync(owned, cancellationToken).ConfigureAwait(false);
        if (values.Any(v => v is null))
        {
            return null;
        }

        if (owned.Select((c, i) => c.IsDegraded(values[i]!.Value)).All(d => d))
        {
            return true;
        }

        ReleaseCore(owner, "found back at normal (restored outside FSGAP)");
        return false;
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
            await _watch.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private static int IndexOf(IReadOnlyList<SynapticControl> controls, SynapticControl control)
    {
        for (var i = 0; i < controls.Count; i++)
        {
            if (ReferenceEquals(controls[i], control))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task<bool> ConfirmCoreAsync(IReadOnlyList<(SynapticControl Control, double Target)> expected, CancellationToken cancellationToken)
    {
        var controls = expected.Select(e => e.Control).ToArray();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var values = await ReadCoreAsync(controls, cancellationToken).ConfigureAwait(false);
            if (expected.Select((e, i) => values[i] is { } v && Math.Abs(v - e.Target) < e.Control.Tolerance).All(ok => ok))
            {
                return true;
            }

            if (clock.Elapsed >= Times.ReadbackTimeout)
            {
                return false;
            }

            await Task.Delay(Times.ReadbackInterval, cancellationToken).ConfigureAwait(false);
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
                    string[] dropped;
                    lock (_claimsLock)
                    {
                        dropped = _claims.Values.Select(c => c.Owner).Distinct(StringComparer.Ordinal).ToArray();
                        _claims.Clear();
                    }

                    foreach (var owner in dropped)
                    {
                        _logger.LogWarning(
                            "Ownership of {Owner} dropped: {Why}. Its state will be read again, never assumed.",
                            owner,
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
            _logger.LogWarning(ex, "Aircraft watch of the A22X controls stopped.");
        }
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
