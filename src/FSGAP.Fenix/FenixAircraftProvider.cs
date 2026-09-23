using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;
using FSGAP.Core.Telemetry;
using FSGAP.Fenix.Detection;

namespace FSGAP.Fenix;

/// <summary>
/// Aircraft provider for the Fenix Simulations A319, A320 and A321.
/// </summary>
/// <remarks>
/// For now it only recognizes and identifies the aircraft (placeholder rule). Sessions declare
/// <see cref="AircraftCapabilities.None"/>: telemetry snapshots are entirely unavailable and failure commands
/// return <c>NotSupported</c>, until the Fenix integration is extracted from FSHANGAR.
/// </remarks>
public sealed class FenixAircraftProvider : IAircraftProvider
{
    /// <summary>Value of <see cref="ProviderId"/>.</summary>
    public const string Id = "fenix";

    private const string Developer = "Fenix Simulations";
    private const string Manufacturer = "Airbus";
    private const string Family = "A320";

    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the provider.</summary>
    /// <param name="timeProvider">Clock used by sessions; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    public FenixAircraftProvider(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string ProviderId => Id;

    /// <inheritdoc />
    public AircraftMatch Match(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);

        return FenixAircraftDetector.Detect(aircraft) is { } model
            ? AircraftMatch.Supported(CreateIdentity(model, aircraft), MatchSpecificity.Dedicated)
            : AircraftMatch.NotSupported;
    }

    /// <inheritdoc />
    public Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var match = Match(aircraft);
        if (!match.IsSupported)
        {
            throw new NotSupportedException($"'{aircraft.Title}' is not a Fenix A319/A320/A321.");
        }

        IAircraftSession session = new AircraftSession(
            ProviderId,
            match.Identity,
            AircraftCapabilities.None,
            new UnavailableTelemetryProvider(_timeProvider),
            UnsupportedFailureProvider.Instance);
        return Task.FromResult(session);
    }

    private static AircraftIdentity CreateIdentity(FenixModel model, AircraftDescriptor aircraft) => new()
    {
        Developer = Developer,
        Manufacturer = Manufacturer,
        Family = Family,
        Model = model.ToString(),
        IcaoType = model.ToString(),
        Registration = aircraft.Registration,
        Livery = aircraft.Livery,
    };
}
