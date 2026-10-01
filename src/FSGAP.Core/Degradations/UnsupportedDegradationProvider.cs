using FSGAP.Abstractions.Degradations;

namespace FSGAP.Core.Degradations;

/// <summary>
/// Degradation provider for sessions that support no controlled degradation. Pair it with
/// <see cref="Abstractions.Capabilities.DegradationCapabilities.None"/>. Never contacts the aircraft.
/// </summary>
public sealed class UnsupportedDegradationProvider : IDegradationProvider
{
    /// <summary>Shared instance; the provider is stateless.</summary>
    public static UnsupportedDegradationProvider Instance { get; } = new();

    private UnsupportedDegradationProvider()
    {
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always (surfaced through the returned task).</exception>
    public Task<DegradationState> GetStateAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromException<DegradationState>(new NotSupportedException("This provider supports no controlled degradation."));
    }

    /// <inheritdoc />
    public Task<DegradationCommandResult> ApplyAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(NotSupported);
    }

    /// <inheritdoc />
    public Task<DegradationCommandResult> RestoreAsync(DegradationKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Task.FromResult(NotSupported);
    }

    private static DegradationCommandResult NotSupported { get; } =
        new(DegradationCommandStatus.NotSupported, DegradationState.Unavailable, "This provider supports no controlled degradation.");
}
