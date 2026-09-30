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
    private readonly Func<AircraftIdentity> _identity;
    private int _disposed;

    /// <summary>Creates a session with a fixed identity.</summary>
    public AircraftSession(
        string providerId,
        AircraftIdentity identity,
        AircraftCapabilities capabilities,
        ITelemetryProvider telemetry,
        IFailureProvider failures)
        : this(providerId, Fixed(identity ?? throw new ArgumentNullException(nameof(identity))), capabilities, telemetry, failures)
    {
    }

    /// <summary>
    /// Creates a session whose <see cref="Identity"/> is read from <paramref name="identity"/> on each access, so that
    /// a provider can reflect metadata that changes while the same aircraft stays loaded (for example a registration,
    /// see <see cref="AircraftContinuity"/>). The function must be cheap, thread-safe and never return
    /// <see langword="null"/>.
    /// </summary>
    public AircraftSession(
        string providerId,
        Func<AircraftIdentity> identity,
        AircraftCapabilities capabilities,
        ITelemetryProvider telemetry,
        IFailureProvider failures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
    }

    /// <inheritdoc />
    public string ProviderId { get; }

    /// <inheritdoc />
    public AircraftIdentity Identity =>
        _identity() ?? throw new InvalidOperationException($"The '{ProviderId}' session identity function returned null.");

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

    private static Func<AircraftIdentity> Fixed(AircraftIdentity identity) => () => identity;

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
