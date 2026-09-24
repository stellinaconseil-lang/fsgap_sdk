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
            AngleOfAttackDegrees = Number(vars.AngleOfAttackDegrees),
            GrossWeightKilograms = Number(vars.GrossWeightKilograms),
            BodyAccelerationXG = Number(vars.BodyAccelerationXG),
            BodyAccelerationYG = Number(vars.BodyAccelerationYG),
            BodyAccelerationZG = Number(vars.BodyAccelerationZG),
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
            BrakeLeftPercent = TelemetryValue<double>.Known(vars.BrakeLeftPercent, observedAt),
            BrakeRightPercent = TelemetryValue<double>.Known(vars.BrakeRightPercent, observedAt),
            SteeringInputPercent = TelemetryValue<double>.Known(vars.SteeringInputPercent, observedAt),
            AntiskidActive = TelemetryValue<bool>.Known(TelemetryConversions.ToBoolean(vars.AntiskidActive), observedAt),

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
    /// Maps the 1 Hz group's control surface deflections. The configuration fields of the section stay at their
    /// default: they come from the configuration group, see <see cref="WithDeflections"/>.
    /// </summary>
    internal static FlightControlsTelemetry ToControlDeflections(in FastGroupVars vars, DateTimeOffset observedAt) => new()
    {
        AileronLeftDeflectionPercent = TelemetryValue<double>.Known(vars.AileronLeftPercent, observedAt),
        AileronRightDeflectionPercent = TelemetryValue<double>.Known(vars.AileronRightPercent, observedAt),
        ElevatorDeflectionPercent = TelemetryValue<double>.Known(vars.ElevatorPercent, observedAt),
        RudderDeflectionPercent = TelemetryValue<double>.Known(vars.RudderPercent, observedAt),
    };

    /// <summary>
    /// Flight controls is fed by two groups at different rates: <paramref name="current"/> with the deflections of
    /// <paramref name="deflections"/>, every configuration field kept.
    /// </summary>
    internal static FlightControlsTelemetry WithDeflections(FlightControlsTelemetry current, FlightControlsTelemetry deflections) =>
        current with
        {
            AileronLeftDeflectionPercent = deflections.AileronLeftDeflectionPercent,
            AileronRightDeflectionPercent = deflections.AileronRightDeflectionPercent,
            ElevatorDeflectionPercent = deflections.ElevatorDeflectionPercent,
            RudderDeflectionPercent = deflections.RudderDeflectionPercent,
        };

    /// <summary>
    /// <paramref name="current"/> with the configuration fields (flaps, speed brake) of
    /// <paramref name="configuration"/>, every deflection kept.
    /// </summary>
    internal static FlightControlsTelemetry WithConfiguration(FlightControlsTelemetry current, FlightControlsTelemetry configuration) =>
        current with
        {
            FlapsHandlePercent = configuration.FlapsHandlePercent,
            FlapSurfaces = configuration.FlapSurfaces,
            SpeedBrakeDeploymentPercent = configuration.SpeedBrakeDeploymentPercent,
        };

    /// <summary>
    /// Maps the engine group's APU bleed. The other APU values have no generic source the audit trusted, so they
    /// stay as they are (Unavailable in the generic snapshot).
    /// </summary>
    /// <remarks>
    /// <c>PNEUMATICS APU BLEED AIR</c> is read because FSHANGAR reads it; FSHANGAR never verified it against the
    /// Fenix, so its value on that aircraft is unconfirmed.
    /// </remarks>
    internal static TelemetryValue<bool> ToApuBleed(in SlowGroupVars vars, DateTimeOffset observedAt) =>
        TelemetryValue<bool>.Known(TelemetryConversions.ToBoolean(vars.ApuBleedOn), observedAt);

    /// <summary>Maps the engine group's cabin pressurization. Rate converted from feet per second.</summary>
    internal static PressurizationTelemetry ToPressurization(in SlowGroupVars vars, DateTimeOffset observedAt) => new()
    {
        CabinAltitudeFeet = TelemetryValue<double>.Known(vars.CabinAltitudeFeet, observedAt),
        CabinAltitudeRateFeetPerMinute = TelemetryValue<double>.Known(
            TelemetryConversions.FeetPerSecondToFeetPerMinute(vars.CabinAltitudeRateFeetPerSecond),
            observedAt),
    };

    /// <summary>Maps the weather group. An undocumented precipitation mask is Unknown, never guessed.</summary>
    internal static EnvironmentTelemetry ToEnvironment(in EnvironmentGroupVars vars, DateTimeOffset observedAt) => new()
    {
        OutsideAirTemperatureCelsius = TelemetryValue<double>.Known(vars.OutsideAirTemperatureCelsius, observedAt),
        WindDirectionDegreesTrue = TelemetryValue<double>.Known(vars.WindDirectionDegreesTrue, observedAt),
        WindSpeedKnots = TelemetryValue<double>.Known(vars.WindSpeedKnots, observedAt),
        Precipitation = TelemetryConversions.PrecipitationFromMask(vars.PrecipitationMask) is { } precipitation
            ? TelemetryValue<PrecipitationType>.Known(precipitation, observedAt)
            : TelemetryValue<PrecipitationType>.Unknown,
        PrecipitationRateMillimeters = TelemetryValue<double>.Known(vars.PrecipitationRateMillimeters, observedAt),
    };

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
                vars.Engine1StarterActive,
                vars.Engine1OilTemperatureCelsius,
                vars.Engine1OilPressurePsi,
                vars.Engine1ThrottleLeverPercent,
                vars.Engine1ReverserEngaged,
                observedAt),
            Engine(
                2,
                vars.Engine2Combustion,
                vars.Engine2N1Percent,
                vars.Engine2N2Percent,
                vars.Engine2EgtCelsius,
                vars.Engine2FuelFlowPoundsPerHour,
                vars.Engine2StarterActive,
                vars.Engine2OilTemperatureCelsius,
                vars.Engine2OilPressurePsi,
                vars.Engine2ThrottleLeverPercent,
                vars.Engine2ReverserEngaged,
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
        double starter,
        double oilTemperature,
        double oilPressure,
        double throttle,
        double reverser,
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
            StarterActive = TelemetryValue<bool>.Known(TelemetryConversions.ToBoolean(starter), observedAt),
            OilTemperatureCelsius = TelemetryValue<double>.Known(oilTemperature, observedAt),
            OilPressurePsi = TelemetryValue<double>.Known(oilPressure, observedAt),
            ThrottleLeverPercent = TelemetryValue<double>.Known(throttle, observedAt),
            ReverserEngaged = TelemetryValue<bool>.Known(TelemetryConversions.ToBoolean(reverser), observedAt),

            // FireDetected stays Unavailable: the audit found no generic SimVar for it, and the Fenix LVAR
            // candidates also light during a fire test, so they cannot yet distinguish a real fire from a check.
        };
}
