namespace FSGAP.Abstractions.Telemetry;

/// <summary>Flight control surfaces. Deliberately minimal; extended as consumers need more.</summary>
public sealed record FlightControlsTelemetry
{
    /// <summary>Flap/slat extension, in percent of full extension.</summary>
    public TelemetryValue<double> FlapsExtensionPercent { get; init; }

    /// <summary>Speed brake / spoiler deployment, in percent of full deployment.</summary>
    public TelemetryValue<double> SpeedBrakeDeploymentPercent { get; init; }
}
