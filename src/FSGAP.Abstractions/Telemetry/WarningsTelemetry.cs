namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// Flight-envelope warnings raised by the aircraft. Providers must sample them often enough (at least 1 Hz) that a
/// short exceedance is not missed.
/// </summary>
public sealed record WarningsTelemetry
{
    /// <summary>Maximum operating speed exceeded (VMO/MMO).</summary>
    public TelemetryValue<bool> Overspeed { get; init; }

    /// <summary>Maximum speed for the current flap/slat configuration exceeded (VFE).</summary>
    public TelemetryValue<bool> FlapSpeedExceeded { get; init; }

    /// <summary>Maximum speed with landing gear extended or in transit exceeded (VLE/VLO).</summary>
    public TelemetryValue<bool> GearSpeedExceeded { get; init; }

    /// <summary>Stall warning active.</summary>
    public TelemetryValue<bool> Stall { get; init; }
}
