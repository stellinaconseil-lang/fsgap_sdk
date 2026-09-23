using FSGAP.Abstractions.Failures;

namespace FSGAP.Core.Failures;

/// <summary>
/// Failure provider for sessions that support no failure operation. Pair it with
/// <see cref="Abstractions.Capabilities.FailureCapabilities.None"/>.
/// </summary>
public sealed class UnsupportedFailureProvider : IFailureProvider
{
    /// <summary>Shared instance; the provider is stateless.</summary>
    public static UnsupportedFailureProvider Instance { get; } = new();

    private UnsupportedFailureProvider()
    {
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always (surfaced through the returned task).</exception>
    public Task<IReadOnlyCollection<AircraftFailure>> GetActiveFailuresAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<IReadOnlyCollection<AircraftFailure>>(
            new NotSupportedException("This provider cannot read active failures."));

    /// <inheritdoc />
    public Task<FailureCommandResult> TriggerAsync(FailureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Task.FromResult(FailureCommandResult.NotSupported("This provider cannot trigger failures."));
    }

    /// <inheritdoc />
    public Task<FailureCommandResult> ClearAsync(FailureCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Task.FromResult(FailureCommandResult.NotSupported("This provider cannot clear failures."));
    }
}
