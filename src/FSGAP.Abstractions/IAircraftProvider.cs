using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Abstractions;

/// <summary>
/// Entry point of an aircraft integration (Fenix, PMDG, generic SimConnect...). A provider recognizes the aircraft
/// it supports and opens sessions on them; everything it exposes is expressed in normalized, vendor-neutral terms.
/// </summary>
/// <remarks>
/// A provider is registered once and is stateless with respect to the loaded aircraft: per-aircraft state
/// (identity, capabilities, telemetry, failures) lives in the <see cref="IAircraftSession"/> returned by
/// <see cref="AttachAsync"/>.
/// </remarks>
public interface IAircraftProvider
{
    /// <summary>
    /// Stable, unique identifier of the provider (e.g. <c>fenix</c>). Used for registration, logging and
    /// configuration; never changes between versions.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Determines whether this provider supports the described aircraft and, if so, identifies it.
    /// Must be fast, side-effect free and must not throw for unrecognized aircraft.
    /// </summary>
    /// <param name="aircraft">Aircraft detected in the simulator.</param>
    AircraftMatch Match(AircraftDescriptor aircraft);

    /// <summary>Shortcut for <c>Match(aircraft).IsSupported</c>.</summary>
    /// <param name="aircraft">Aircraft detected in the simulator.</param>
    bool CanHandle(AircraftDescriptor aircraft) => Match(aircraft).IsSupported;

    /// <summary>Opens a session on a supported aircraft.</summary>
    /// <param name="aircraft">Aircraft detected in the simulator.</param>
    /// <param name="cancellationToken">Cancels the attach operation.</param>
    /// <exception cref="NotSupportedException">The provider does not support <paramref name="aircraft"/>.</exception>
    Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default);
}
