using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions;

/// <summary>
/// A provider attached to one specific aircraft: what it is, what the provider can do for it, and access to its
/// telemetry and failures. Dispose the session when the aircraft is unloaded or the consumer stops.
/// </summary>
public interface IAircraftSession : IAsyncDisposable
{
    /// <summary><see cref="IAircraftProvider.ProviderId"/> of the provider that opened the session.</summary>
    string ProviderId { get; }

    /// <summary>Normalized identity of the attached aircraft.</summary>
    AircraftIdentity Identity { get; }

    /// <summary>What the provider can do for this aircraft.</summary>
    AircraftCapabilities Capabilities { get; }

    /// <summary>Normalized telemetry.</summary>
    ITelemetryProvider Telemetry { get; }

    /// <summary>Normalized failures.</summary>
    IFailureProvider Failures { get; }
}
