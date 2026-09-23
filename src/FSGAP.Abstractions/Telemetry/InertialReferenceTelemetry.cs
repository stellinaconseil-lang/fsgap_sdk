namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of one inertial reference unit (IRS, ADIRU, AHRS... depending on the aircraft).</summary>
public sealed record InertialReferenceTelemetry
{
    /// <summary>1-based unit number.</summary>
    public required int Index { get; init; }

    /// <summary>Current operating mode.</summary>
    public TelemetryValue<InertialReferenceMode> Mode { get; init; }

    /// <summary>Whether alignment is complete.</summary>
    public TelemetryValue<bool> Aligned { get; init; }

    /// <summary>Whether the unit reports a fault.</summary>
    public TelemetryValue<bool> Fault { get; init; }
}
