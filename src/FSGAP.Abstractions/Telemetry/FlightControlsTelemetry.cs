using FSGAP.Abstractions.Internal;

namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// Flight controls. The pilot's command (flap handle) and the actual surface positions are separate readings, because
/// they differ while surfaces are moving and when a surface fails.
/// </summary>
public sealed record FlightControlsTelemetry
{
    private readonly IReadOnlyList<FlapSurfaceTelemetry> _flapSurfaces = [];

    /// <summary>Flap handle position, in percent of its full travel (0 = UP/retracted detent, 100 = full).</summary>
    public TelemetryValue<double> FlapsHandlePercent { get; init; }

    /// <summary>Actual trailing-edge flap surface positions. The assigned collection is copied.</summary>
    public IReadOnlyList<FlapSurfaceTelemetry> FlapSurfaces
    {
        get => _flapSurfaces;
        init => _flapSurfaces = ReadOnlyCopy.Of(value, nameof(FlapSurfaces));
    }

    /// <summary>Speed brake / spoiler deployment, in percent of full deployment.</summary>
    public TelemetryValue<double> SpeedBrakeDeploymentPercent { get; init; }

    /// <summary>Left aileron deflection, in percent of its travel, signed as the simulator reports it.</summary>
    public TelemetryValue<double> AileronLeftDeflectionPercent { get; init; }

    /// <summary>Right aileron deflection, in percent of its travel, signed as the simulator reports it.</summary>
    public TelemetryValue<double> AileronRightDeflectionPercent { get; init; }

    /// <summary>Elevator deflection (one combined value), in percent of its travel, signed as the simulator reports it.</summary>
    public TelemetryValue<double> ElevatorDeflectionPercent { get; init; }

    /// <summary>Rudder deflection, in percent of its travel, signed as the simulator reports it.</summary>
    public TelemetryValue<double> RudderDeflectionPercent { get; init; }
}
