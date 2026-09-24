using FSGAP.Abstractions.Internal;

namespace FSGAP.Abstractions.Telemetry;

/// <summary>
/// Landing gear: the pilot's command (handle) and the actual state of each gear unit. Aircraft with fixed gear
/// report no handle and fully extended units.
/// </summary>
public sealed record LandingGearTelemetry
{
    private readonly IReadOnlyList<GearUnitTelemetry> _units = [];

    /// <summary>Whether the gear handle is in the DOWN position (the pilot's command, not the actual gear state).</summary>
    public TelemetryValue<bool> HandleDown { get; init; }

    /// <summary>Left wheel brake application, in percent (0 = released, 100 = full).</summary>
    public TelemetryValue<double> BrakeLeftPercent { get; init; }

    /// <summary>Right wheel brake application, in percent (0 = released, 100 = full).</summary>
    public TelemetryValue<double> BrakeRightPercent { get; init; }

    /// <summary>Nose-wheel steering input, in percent, signed as the simulator reports it.</summary>
    public TelemetryValue<double> SteeringInputPercent { get; init; }

    /// <summary>Whether the anti-skid system is active.</summary>
    public TelemetryValue<bool> AntiskidActive { get; init; }

    /// <summary>Actual state of each gear unit. The assigned collection is copied.</summary>
    public IReadOnlyList<GearUnitTelemetry> Units
    {
        get => _units;
        init => _units = ReadOnlyCopy.Of(value, nameof(Units));
    }
}
