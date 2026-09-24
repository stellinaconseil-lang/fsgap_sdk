using FSGAP.Abstractions.Failures;
using Microsoft.Extensions.Logging;

namespace FSGAP.Fenix.Failures;

/// <summary>
/// <see cref="IFailureProvider"/> for one Fenix session, over the Fenix EFB. Speaks only <see cref="FailureKey"/> to
/// the outside; the Fenix id and title are looked up internally.
/// </summary>
/// <remarks>
/// <para>
/// <b>Commands.</b> Key → catalog entry (unknown key, operation or target: <see cref="FailureCommandStatus.NotSupported"/>,
/// no HTTP) → <c>saveManual</c> → confirmation. The EFB's echo confirms the state; when it does not (some failures,
/// e.g. tyre pressures, echo a stale state), the failure list is read back up to
/// <see cref="FenixOptions.ConfirmationReadbacks"/> times. The outcome is honest about what is known:
/// </para>
/// <list type="table">
/// <item><term>confirmed</term><description><see cref="FailureCommandStatus.Succeeded"/></description></item>
/// <item><term>EFB unreachable, or another aircraft now loaded</term><description><see cref="FailureCommandStatus.Unavailable"/> (nothing applied)</description></item>
/// <item><term>timeout, broken exchange, not confirmed, session ended mid-command</term><description><see cref="FailureCommandStatus.Unconfirmed"/> (may be applied)</description></item>
/// <item><term>HTTP 4xx</term><description><see cref="FailureCommandStatus.Rejected"/></description></item>
/// <item><term>HTTP 5xx</term><description><see cref="FailureCommandStatus.Failed"/></description></item>
/// </list>
/// <para>
/// A command is <b>never retried</b>: the EFB may have applied it. Commands of one session run one at a time, so a
/// trigger and a clear of the same failure land in the order they were issued.
/// </para>
/// <para>
/// <b>Active failures</b> come from the EFB's live list only; nothing is inferred from the commands sent. A failed
/// entry with a key is reported with it; a failed entry without one (known to Fenix but not normalized, or unknown)
/// is still reported, with <c>Key = null</c> and no vendor id. An answer that cannot be fully interpreted throws
/// <see cref="FailuresUnavailableException"/> rather than risking a list that silently misses a failure.
/// </para>
/// <para>
/// <b>Lifetime.</b> Owned by the session. Disposing cancels in-flight calls; new calls throw
/// <see cref="ObjectDisposedException"/>. The HTTP client is shared and never disposed here.
/// </para>
/// </remarks>
internal sealed class FenixFailureProvider : IFailureProvider, IAsyncDisposable
{
    private readonly FenixEfbClient _efb;
    private readonly FenixFailureCatalogData _catalog;
    private readonly Func<bool> _aircraftReplaced;
    private readonly FenixOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _commands = new(1, 1);

    private int _disposed;
    private int _readFailing;

    internal FenixFailureProvider(
        FenixEfbClient efb,
        FenixFailureCatalogData catalog,
        Func<bool> aircraftReplaced,
        FenixOptions options,
        TimeProvider time,
        ILogger logger)
    {
        _efb = efb;
        _catalog = catalog;
        _aircraftReplaced = aircraftReplaced;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<AircraftFailure>> GetActiveFailuresAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_aircraftReplaced())
        {
            throw new FailuresUnavailableException("The aircraft this session was attached to is no longer loaded.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        EfbListResult result;
        try
        {
            result = await _efb.GetManualAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(FenixFailureProvider), "The session ended while the active failures were being read.");
        }

        if (result.Outcome != EfbOutcome.Ok)
        {
            var reason = Describe(result.Outcome, result.HttpStatus);
            if (Interlocked.Exchange(ref _readFailing, 1) == 0)
            {
                _logger.LogWarning("Could not read the Fenix active failures from the EFB at {Origin}: {Reason}", _efb.Origin, reason);
            }

            throw new FailuresUnavailableException($"The active failures cannot be read: {reason}.");
        }

        if (Interlocked.Exchange(ref _readFailing, 0) == 1)
        {
            _logger.LogInformation("Fenix active failures readable again");
        }

        return MapActive(result.Entries!);
    }

    /// <inheritdoc />
    public Task<FailureCommandResult> TriggerAsync(FailureCommand command, CancellationToken cancellationToken = default) =>
        ExecuteAsync(command, FailureOperations.Trigger, cancellationToken);

    /// <inheritdoc />
    public Task<FailureCommandResult> ClearAsync(FailureCommand command, CancellationToken cancellationToken = default) =>
        ExecuteAsync(command, FailureOperations.Clear, cancellationToken);

    /// <summary>Cancels in-flight calls; later calls throw <see cref="ObjectDisposedException"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
    }

    /// <summary>Maps the EFB list to active failures (exposed for tests).</summary>
    /// <exception cref="FailuresUnavailableException">An entry's state cannot be read.</exception>
    internal IReadOnlyCollection<AircraftFailure> MapActive(IReadOnlyList<EfbFailureEntry> entries)
    {
        if (entries.Any(e => e.Failed is null))
        {
            _logger.LogWarning("The Fenix EFB failure list has {Count} entries without a readable state", entries.Count(e => e.Failed is null));
            throw new FailuresUnavailableException("The failure list contains entries whose state cannot be read.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var active = new List<AircraftFailure>();
        foreach (var entry in entries.Where(e => e.Failed == true))
        {
            if (entry.Id is { } id && !seen.Add(id))
            {
                continue; // the same failure listed twice is one active failure
            }

            active.Add(ToActiveFailure(entry));
        }

        return active;
    }

    private AircraftFailure ToActiveFailure(EfbFailureEntry entry)
    {
        if (entry.Id is null)
        {
            _logger.LogWarning("The Fenix EFB lists an active failure without an id (title {Title})", entry.Title);
            return new AircraftFailure { Description = "Unidentified failure" };
        }

        if (_catalog.TryGetMapped(entry.Id, out var mapped))
        {
            return new AircraftFailure
            {
                Key = mapped.Definition.Key,
                Target = mapped.Target,
                Category = mapped.Definition.Category,
                Description = mapped.Definition.DisplayName,
            };
        }

        if (_catalog.TryGetRaw(entry.Id, out var raw))
        {
            // Known to Fenix, not normalized: reported (something is failed), never commandable, no vendor id.
            return new AircraftFailure { Category = FenixFailureCatalogData.CategoryOf(raw.Ata), Description = raw.Title };
        }

        _logger.LogWarning("The Fenix EFB lists an active failure unknown to the embedded catalog: {FenixId} ({Title})", entry.Id, entry.Title);
        return new AircraftFailure { Description = entry.Title is { Length: > 0 } title ? title : "Unidentified failure" };
    }

    private async Task<FailureCommandResult> ExecuteAsync(FailureCommand command, FailureOperations operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var verb = operation == FailureOperations.Trigger ? "trigger" : "clear";

        if (!_catalog.TryGetByKey(command.Key, out var mapped))
        {
            return FailureCommandResult.NotSupported($"'{command.Key}' is not a Fenix failure known to FSGAP.");
        }

        if (!mapped.Definition.Operations.HasFlag(operation))
        {
            return FailureCommandResult.NotSupported($"'{command.Key}' cannot be {(operation == FailureOperations.Trigger ? "triggered" : "cleared")}.");
        }

        if (!mapped.Definition.Supports(command.Target))
        {
            return FailureCommandResult.NotSupported($"'{command.Key}' applies to {mapped.Target}, not {command.Target}.");
        }

        if (_aircraftReplaced())
        {
            return FailureCommandResult.Unavailable("The aircraft this session was attached to is no longer loaded.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        try
        {
            await _commands.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(FenixFailureProvider), "The session ended before the command was sent.");
        }

        try
        {
            return await SendAsync(command.Key, mapped.Raw, operation == FailureOperations.Trigger, verb, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Disposal during the exchange: the EFB may already have applied the command.
            return FailureCommandResult.Unconfirmed($"The session ended while '{command.Key}' was being sent; its outcome is unknown.");
        }
        finally
        {
            _commands.Release();
        }
    }

    private async Task<FailureCommandResult> SendAsync(FailureKey key, FenixRawFailure raw, bool failed, string verb, CancellationToken cancellationToken)
    {
        var sent = await _efb.SaveManualAsync(raw, failed, cancellationToken).ConfigureAwait(false);
        switch (sent.Outcome)
        {
            case EfbOutcome.Unreachable:
                _logger.LogWarning("Fenix EFB not reachable at {Origin}; could not {Verb} {FenixId}", _efb.Origin, verb, raw.Id);
                return FailureCommandResult.Unavailable($"The Fenix EFB is not reachable (is a Fenix aircraft loaded?). '{key}' was not {Past(verb)}.");
            case EfbOutcome.TimedOut:
                _logger.LogWarning("Fenix EFB did not answer within {Timeout} to {Verb} {FenixId}", _options.RequestTimeout, verb, raw.Id);
                return FailureCommandResult.Unconfirmed($"The Fenix EFB did not answer within {_options.RequestTimeout.TotalSeconds:0.#} s; '{key}' may or may not have been {Past(verb)}.");
            case EfbOutcome.Interrupted:
                _logger.LogWarning("The exchange with the Fenix EFB broke while trying to {Verb} {FenixId}", verb, raw.Id);
                return FailureCommandResult.Unconfirmed($"The exchange with the Fenix EFB broke; '{key}' may or may not have been {Past(verb)}.");
            case EfbOutcome.HttpError:
                _logger.LogWarning("Fenix EFB answered HTTP {Status} to {Verb} {FenixId}", sent.HttpStatus, verb, raw.Id);
                return sent.HttpStatus is >= 400 and < 500
                    ? FailureCommandResult.Rejected($"The Fenix EFB refused to {verb} '{key}' (HTTP {sent.HttpStatus}).")
                    : FailureCommandResult.Failed($"The Fenix EFB failed to {verb} '{key}' (HTTP {sent.HttpStatus}).");
        }

        if (sent.EchoedFailed == failed || await ConfirmByReadbackAsync(raw, failed, cancellationToken).ConfigureAwait(false))
        {
            return FailureCommandResult.Succeeded;
        }

        _logger.LogWarning(
            "Fenix EFB accepted the command to {Verb} {FenixId} but did not confirm failed={Failed} (echo {Echo}, {Readbacks} read-backs)",
            verb, raw.Id, failed, sent.EchoedFailed?.ToString() ?? "none", _options.ConfirmationReadbacks);
        return FailureCommandResult.Unconfirmed($"The Fenix EFB accepted the command but did not confirm that '{key}' was {Past(verb)}.");
    }

    /// <summary>
    /// Reads the failure list until it shows the requested state. The first read is immediate; later ones wait
    /// <see cref="FenixOptions.ConfirmationDelay"/> (the state was seen to settle within about 500 ms).
    /// </summary>
    private async Task<bool> ConfirmByReadbackAsync(FenixRawFailure raw, bool failed, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < _options.ConfirmationReadbacks; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(_options.ConfirmationDelay, _time, cancellationToken).ConfigureAwait(false);
            }

            var list = await _efb.GetManualAsync(cancellationToken).ConfigureAwait(false);
            if (list.Outcome == EfbOutcome.Ok
                && list.Entries!.Any(e => string.Equals(e.Id, raw.Id, StringComparison.OrdinalIgnoreCase) && e.Failed == failed))
            {
                return true;
            }
        }

        return false;
    }

    private static string Past(string verb) => verb == "trigger" ? "triggered" : "cleared";

    private static string Describe(EfbOutcome outcome, int? status) => outcome switch
    {
        EfbOutcome.Unreachable => "the Fenix EFB is not reachable (is a Fenix aircraft loaded?)",
        EfbOutcome.TimedOut => "the Fenix EFB did not answer in time",
        EfbOutcome.Interrupted => "the exchange with the Fenix EFB broke",
        EfbOutcome.HttpError => $"the Fenix EFB answered HTTP {status}",
        EfbOutcome.Malformed => "the Fenix EFB answer could not be interpreted",
        _ => outcome.ToString(),
    };
}
