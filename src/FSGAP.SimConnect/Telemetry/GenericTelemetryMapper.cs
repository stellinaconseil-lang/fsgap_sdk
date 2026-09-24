using FSGAP.Abstractions.Telemetry;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Telemetry;

/// <summary>
/// Turns a raw SimVar group into the matching sections of the normalized contract.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions, deliberately: given a struct and a timestamp they return contract records and touch nothing
/// else. That is what makes the whole mapping testable against recorded values without a simulator, which is the
/// only practical way to prove a conversion is right.
/// </para>
/// <para>
/// Every produced value is <see cref="TelemetryValue{T}.Known"/> with the observation time of its group. A group
/// that was never read leaves its section at the contract default, which is <c>Unavailable</c> — never zero and
/// never false.
/// </para>
/// </remarks>
internal static class GenericTelemetryMapper
{
    /// <summary>Maps the 1 Hz group to flight state.</summary>
    internal static FlightStateTelemetry ToFlightState(in FastGroupVars vars, DateTimeOffset observedAt)
    {
        TelemetryValue<double> Number(double value) => TelemetryValue<double>.Known(value, observedAt);

        return new FlightStateTelemetry
        {
            OnGround = TelemetryValue<bool>.Known(TelemetryConversions.ToBoolean(vars.OnGround), observedAt),
            LatitudeDegrees = Number(vars.LatitudeDegrees),
            LongitudeDegrees = Number(vars.LongitudeDegrees),
            AltitudeFeet = Number(vars.AltitudeFeet),
            HeightAboveGroundFeet = Number(vars.HeightAboveGroundFeet),

            // RadioAltitudeFeet is deliberately left Unavailable. It is a different quantity from geometric
            // height above ground — an aircraft's own radio altimeter, with its own limits and failure modes —
            // and the audit found no generic SimVar the applications trusted for it. Reporting height above
            // ground under that name would be a quiet lie.
            IndicatedAirspeedKnots = Number(vars.IndicatedAirspeedKnots),
            GroundSpeedKnots = Number(vars.GroundSpeedKnots),
            VerticalSpeedFeetPerMinute =
                Number(TelemetryConversions.FeetPerSecondToFeetPerMinute(vars.VerticalSpeedFeetPerSecond)),
            TouchdownVerticalSpeedFeetPerMinute =
                TelemetryConversions.TouchdownToFeetPerMinute(vars.TouchdownNormalVelocityFeetPerSecond) is { } touchdown
                    ? Number(touchdown)
                    : TelemetryValue<double>.Unknown,
            HeadingMagneticDegrees = Number(vars.HeadingMagneticDegrees),
            PitchDegrees = Number(TelemetryConversions.SimPitchToNoseUpDegrees(vars.PitchDegrees)),
            BankDegrees = Number(TelemetryConversions.SimBankToRightWingDownDegrees(vars.BankDegrees)),
            GLoad = Number(vars.GLoad),
        };
    }

    /// <summary>Maps the 1 Hz group to flight-envelope warnings.</summary>
    internal static WarningsTelemetry ToWarnings(in FastGroupVars vars, DateTimeOffset observedAt)
    {
        TelemetryValue<bool> Flag(double value) =>
            TelemetryValue<bool>.Known(TelemetryConversions.ToBoolean(value), observedAt);

        return new WarningsTelemetry
        {
            Overspeed = Flag(vars.OverspeedWarning),
            FlapSpeedExceeded = Flag(vars.FlapSpeedExceeded),
            GearSpeedExceeded = Flag(vars.GearSpeedExceeded),
            Stall = Flag(vars.StallWarning),
        };
    }

    /// <summary>
    /// Maps the configuration group to landing gear.
    /// </summary>
    /// <remarks>
    /// The handle and the legs are separate readings and stay separate. A handle selected down while the legs
    /// are still travelling is a real state the audited applications could not express, because they used the
    /// nose leg as a proxy for the whole gear.
    /// </remarks>
    internal static LandingGearTelemetry ToLandingGear(in NormalGroupVars vars, DateTimeOffset observedAt)
    {
        GearUnitTelemetry Unit(string id, string name, double percent) => new()
        {
            Id = id,
            Name = name,
            ExtensionPercent = TelemetryValue<double>.Known(percent, observedAt),
        };

        return new LandingGearTelemetry
        {
            HandleDown = TelemetryValue<bool>.Known(TelemetryConversions.IsGearHandleDown(vars.GearHandlePercent), observedAt),

            // MSFS "center" gear is the nose gear on tricycle aircraft, which is every aircraft the consumers fly.
            // Ids follow the contract's conventional keys.
            Units =
            [
                Unit("nose", "Nose gear", vars.GearCenterPercent),
                Unit("left-main", "Left main gear", vars.GearLeftPercent),
                Unit("right-main", "Right main gear", vars.GearRightPercent),
            ],
        };
    }

    /// <summary>Maps the configuration group to flight controls.</summary>
    internal static FlightControlsTelemetry ToFlightControls(in NormalGroupVars vars, DateTimeOffset observedAt)
    {
        FlapSurfaceTelemetry Surface(string id, string name, double percent) => new()
        {
            Id = id,
            Name = name,
            ExtensionPercent = TelemetryValue<double>.Known(percent, observedAt),
        };

        return new FlightControlsTelemetry
        {
            FlapsHandlePercent = TelemetryValue<double>.Known(vars.FlapsHandlePercent, observedAt),
            FlapSurfaces =
            [
                Surface("trailing-left", "Trailing edge left", vars.FlapsLeftPercent),
                Surface("trailing-right", "Trailing edge right", vars.FlapsRightPercent),
            ],

            // One deployment figure for a two-sided surface: the larger of the two, so an asymmetry reports the
            // deployed side rather than averaging it away into something neither side is doing.
            SpeedBrakeDeploymentPercent = TelemetryValue<double>.Known(
                Math.Max(vars.SpoilersLeftPercent, vars.SpoilersRightPercent),
                observedAt),
        };
    }

    /// <summary>
    /// Maps the engine group.
    /// </summary>
    /// <remarks>
    /// Two engines, because every consumer audited in BLOCK 1 flies an A32x and inventing a third would mean
    /// publishing readings for something that does not exist. The contract's collection has no fixed size, so
    /// widening this is a change to this method and to <see cref="SlowGroupVars"/> and to nothing else.
    /// </remarks>
    internal static IReadOnlyList<EngineTelemetry> ToEngines(in SlowGroupVars vars, DateTimeOffset observedAt)
    {
        return
        [
            Engine(
                1,
                vars.Engine1Combustion,
                vars.Engine1N1Percent,
                vars.Engine1N2Percent,
                vars.Engine1EgtCelsius,
                vars.Engine1FuelFlowPoundsPerHour,
                observedAt),
            Engine(
                2,
                vars.Engine2Combustion,
                vars.Engine2N1Percent,
                vars.Engine2N2Percent,
                vars.Engine2EgtCelsius,
                vars.Engine2FuelFlowPoundsPerHour,
                observedAt),
        ];
    }

    private static EngineTelemetry Engine(
        int index,
        double combustion,
        double n1,
        double n2,
        double egt,
        double fuelFlowPph,
        DateTimeOffset observedAt) => new()
        {
            Index = index,
            Running = TelemetryValue<bool>.Known(TelemetryConversions.ToBoolean(combustion), observedAt),
            N1Percent = TelemetryValue<double>.Known(n1, observedAt),
            N2Percent = TelemetryValue<double>.Known(n2, observedAt),
            EgtCelsius = TelemetryValue<double>.Known(egt, observedAt),
            FuelFlowKilogramsPerHour = TelemetryValue<double>.Known(
                TelemetryConversions.PoundsPerHourToKilogramsPerHour(fuelFlowPph),
                observedAt),

            // FireDetected stays Unavailable: the audit found no generic SimVar for it, and the Fenix LVAR
            // candidates also light during a fire test, so they cannot yet distinguish a real fire from a check.
        };
}
