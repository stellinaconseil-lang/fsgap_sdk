using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Cockpit;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Cockpit;
using FSGAP.Core.Degradations;

namespace FSGAP.Core.Sessions;

/// <summary>
/// Default <see cref="IAircraftSession"/> implementation that providers can return from
/// <see cref="IAircraftProvider.AttachAsync"/>. Disposing the session disposes its degradation, telemetry and failure
/// providers when they are disposable, exactly once each, degradations first (a provider that restores what it applied
/// does so while the telemetry and the connection are still in use).
/// </summary>
public sealed class AircraftSession : IAircraftSession
{
    private readonly Func<AircraftIdentity> _identity;
    private int _disposed;

    /// <summary>Creates a session with a fixed identity and no controlled degradation.</summary>
    public AircraftSession(
        string providerId,
        AircraftIdentity identity,
        AircraftCapabilities capabilities,
        ITelemetryProvider telemetry,
        IFailureProvider failures)
        : this(providerId, Fixed(identity ?? throw new ArgumentNullException(nameof(identity))), capabilities, telemetry, failures, UnsupportedDegradationProvider.Instance)
    {
    }

    /// <summary>
    /// Creates a session whose <see cref="Identity"/> is read from <paramref name="identity"/> on each access, with no
    /// controlled degradation (see the overload taking an <see cref="IDegradationProvider"/>).
    /// </summary>
    public AircraftSession(
        string providerId,
        Func<AircraftIdentity> identity,
        AircraftCapabilities capabilities,
        ITelemetryProvider telemetry,
        IFailureProvider failures)
        : this(providerId, identity, capabilities, telemetry, failures, UnsupportedDegradationProvider.Instance)
    {
    }

    /// <summary>
    /// Creates a session whose <see cref="Identity"/> is read from <paramref name="identity"/> on each access, so that
    /// a provider can reflect metadata that changes while the same aircraft stays loaded (for example a registration,
    /// see <see cref="AircraftContinuity"/>). The function must be cheap, thread-safe and never return
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="providerId">Provider identifier.</param>
    /// <param name="identity">Identity function.</param>
    /// <param name="capabilities">What the provider can do; its <see cref="AircraftCapabilities.Degradations"/> must describe <paramref name="degradations"/>.</param>
    /// <param name="telemetry">Normalized telemetry.</param>
    /// <param name="failures">Normalized failures.</param>
    /// <param name="degradations">Controlled degradations; <see cref="UnsupportedDegradationProvider.Instance"/> when none.</param>
    public AircraftSession(
        string providerId,
        Func<AircraftIdentity> identity,
        AircraftCapabilities capabilities,
        ITelemetryProvider telemetry,
        IFailureProvider failures,
        IDegradationProvider degradations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ProviderId = providerId;
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
        Degradations = degradations ?? throw new ArgumentNullException(nameof(degradations));
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
    public IDegradationProvider Degradations { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Read-only cockpit observations, set with an object initializer; a provider that reads nothing leaves the
    /// default <see cref="UnsupportedCockpitObservationProvider.Instance"/>. Set on a session's own line so a provider
    /// that applies no degradation never has to name a degradation type.
    /// </remarks>
    public ICockpitObservationProvider CockpitObservations { get; init; } = UnsupportedCockpitObservationProvider.Instance;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await DisposeComponentAsync(Degradations).ConfigureAwait(false);
        if (!ReferenceEquals(Telemetry, Degradations))
        {
            await DisposeComponentAsync(Telemetry).ConfigureAwait(false);
        }

        if (!ReferenceEquals(Failures, Telemetry) && !ReferenceEquals(Failures, Degradations))
        {
            await DisposeComponentAsync(Failures).ConfigureAwait(false);
        }

        if (!ReferenceEquals(CockpitObservations, Degradations)
            && !ReferenceEquals(CockpitObservations, Telemetry)
            && !ReferenceEquals(CockpitObservations, Failures))
        {
            await DisposeComponentAsync(CockpitObservations).ConfigureAwait(false);
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
