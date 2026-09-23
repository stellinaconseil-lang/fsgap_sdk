using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Core.Sessions;

/// <summary>
/// Default <see cref="IAircraftSession"/> implementation that providers can return from
/// <see cref="IAircraftProvider.AttachAsync"/>. Disposing the session disposes its telemetry and failure
/// providers when they are disposable, exactly once.
/// </summary>
public sealed class AircraftSession : IAircraftSession
{
    private int _disposed;

    /// <summary>Creates a session.</summary>
    public AircraftSession(
        string providerId,
        AircraftIdentity identity,
        AircraftCapabilities capabilities,
        ITelemetryProvider telemetry,
        IFailureProvider failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
    }

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <inheritdoc />
    public AircraftIdentity Identity { get; }

    /// <inheritdoc />
    public AircraftCapabilities Capabilities { get; }

    /// <inheritdoc />
    public ITelemetryProvider Telemetry { get; }

    /// <inheritdoc />
    public IFailureProvider Failures { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await DisposeComponentAsync(Telemetry).ConfigureAwait(false);
        if (!ReferenceEquals(Failures, Telemetry))
        {
            await DisposeComponentAsync(Failures).ConfigureAwait(false);
        }
    }

    private static ValueTask DisposeComponentAsync(object component)
    {
        switch (component)
        {
            case IAsyncDisposable asyncDisposable:
                return asyncDisposable.DisposeAsync();
            case IDisposable disposable:
                disposable.Dispose();
                return ValueTask.CompletedTask;
            default:
                return ValueTask.CompletedTask;
        }
    }
}
