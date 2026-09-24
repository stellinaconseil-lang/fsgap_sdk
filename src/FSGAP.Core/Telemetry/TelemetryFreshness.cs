using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Core.Telemetry;

/// <summary>
/// Applies the freshness rule to a whole snapshot: every known value observed more than a maximum age before the
/// snapshot's <see cref="AircraftTelemetry.Timestamp"/> becomes <see cref="ValueState.Unknown"/>. Providers call it
/// when building snapshots, so a value that stopped being fed never stays "known" indefinitely.
/// </summary>
public static class TelemetryFreshness
{
    /// <summary>Returns a copy of <paramref name="telemetry"/> in which stale known values are Unknown.</summary>
    /// <param name="telemetry">Snapshot to check; not modified.</param>
    /// <param name="maxAge">Maximum age, typically <c>FsgapOptions.Telemetry.StaleAfter</c>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxAge"/> is negative.</exception>
    public static AircraftTelemetry ExpireStaleValues(AircraftTelemetry telemetry, TimeSpan maxAge)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAge, TimeSpan.Zero);
        var now = telemetry.Timestamp;

        TelemetryValue<TValue> E<TValue>(TelemetryValue<TValue> value)
            where TValue : struct => value.ExpireIfOlderThan(now, maxAge);

        var f = telemetry.Flight;
        var w = telemetry.Warnings;
        var a = telemetry.Apu;
        var gear = telemetry.LandingGear;
        var controls = telemetry.FlightControls;
        var pressurization = telemetry.Pressurization;
        var environment = telemetry.Environment;

        return telemetry with
        {
            Flight = f with
            {
                OnGround = E(f.OnGround),
                LatitudeDegrees = E(f.LatitudeDegrees),
                LongitudeDegrees = E(f.LongitudeDegrees),
                AltitudeFeet = E(f.AltitudeFeet),
                HeightAboveGroundFeet = E(f.HeightAboveGroundFeet),
                RadioAltitudeFeet = E(f.RadioAltitudeFeet),
                IndicatedAirspeedKnots = E(f.IndicatedAirspeedKnots),
                GroundSpeedKnots = E(f.GroundSpeedKnots),
                VerticalSpeedFeetPerMinute = E(f.VerticalSpeedFeetPerMinute),
                TouchdownVerticalSpeedFeetPerMinute = E(f.TouchdownVerticalSpeedFeetPerMinute),
                HeadingMagneticDegrees = E(f.HeadingMagneticDegrees),
                PitchDegrees = E(f.PitchDegrees),
                BankDegrees = E(f.BankDegrees),
                GLoad = E(f.GLoad),
                AngleOfAttackDegrees = E(f.AngleOfAttackDegrees),
                GrossWeightKilograms = E(f.GrossWeightKilograms),
                BodyAccelerationXG = E(f.BodyAccelerationXG),
                BodyAccelerationYG = E(f.BodyAccelerationYG),
                BodyAccelerationZG = E(f.BodyAccelerationZG),
            },
            Warnings = w with
            {
                Overspeed = E(w.Overspeed),
                FlapSpeedExceeded = E(w.FlapSpeedExceeded),
                GearSpeedExceeded = E(w.GearSpeedExceeded),
                Stall = E(w.Stall),
            },
            Engines = telemetry.Engines.Select(e => e with
            {
                Running = E(e.Running),
                N1Percent = E(e.N1Percent),
                N2Percent = E(e.N2Percent),
                EgtCelsius = E(e.EgtCelsius),
                FuelFlowKilogramsPerHour = E(e.FuelFlowKilogramsPerHour),
                FireDetected = E(e.FireDetected),
                FireHandlePulled = E(e.FireHandlePulled),
                FireWarningLit = E(e.FireWarningLit),
                StarterActive = E(e.StarterActive),
                OilTemperatureCelsius = E(e.OilTemperatureCelsius),
                OilPressurePsi = E(e.OilPressurePsi),
                ThrottleLeverPercent = E(e.ThrottleLeverPercent),
                ReverserEngaged = E(e.ReverserEngaged),
            }).ToArray(),
            Apu = a with
            {
                Available = E(a.Available),
                Running = E(a.Running),
                MasterSwitchOn = E(a.MasterSwitchOn),
                BleedOn = E(a.BleedOn),
                FireDetected = E(a.FireDetected),
                FireHandlePulled = E(a.FireHandlePulled),
            },
            InertialReferences = telemetry.InertialReferences
                .Select(i => i with { Mode = E(i.Mode), Aligned = E(i.Aligned), Fault = E(i.Fault) }).ToArray(),
            FuelPumps = telemetry.FuelPumps.Select(p => p with { IsOn = E(p.IsOn), Fault = E(p.Fault) }).ToArray(),
            ElectricalBuses = telemetry.ElectricalBuses.Select(b => b with { Powered = E(b.Powered) }).ToArray(),
            HydraulicSystems = telemetry.HydraulicSystems
                .Select(h => h with { Pressurized = E(h.Pressurized), PressurePsi = E(h.PressurePsi), ReservoirPercent = E(h.ReservoirPercent) })
                .ToArray(),
            Batteries = telemetry.Batteries.Select(b => b with { VoltageVolts = E(b.VoltageVolts) }).ToArray(),
            FireZones = telemetry.FireZones.Select(z => z with { FireDetected = E(z.FireDetected) }).ToArray(),
            LandingGear = gear with
            {
                HandleDown = E(gear.HandleDown),
                BrakeLeftPercent = E(gear.BrakeLeftPercent),
                BrakeRightPercent = E(gear.BrakeRightPercent),
                SteeringInputPercent = E(gear.SteeringInputPercent),
                AntiskidActive = E(gear.AntiskidActive),
                Units = gear.Units.Select(u => u with { ExtensionPercent = E(u.ExtensionPercent) }).ToArray(),
            },
            FlightControls = controls with
            {
                FlapsHandlePercent = E(controls.FlapsHandlePercent),
                FlapSurfaces = controls.FlapSurfaces.Select(s => s with { ExtensionPercent = E(s.ExtensionPercent) }).ToArray(),
                SpeedBrakeDeploymentPercent = E(controls.SpeedBrakeDeploymentPercent),
                AileronLeftDeflectionPercent = E(controls.AileronLeftDeflectionPercent),
                AileronRightDeflectionPercent = E(controls.AileronRightDeflectionPercent),
                ElevatorDeflectionPercent = E(controls.ElevatorDeflectionPercent),
                RudderDeflectionPercent = E(controls.RudderDeflectionPercent),
            },
            Pressurization = pressurization with
            {
                CabinAltitudeFeet = E(pressurization.CabinAltitudeFeet),
                CabinAltitudeRateFeetPerMinute = E(pressurization.CabinAltitudeRateFeetPerMinute),
            },
            Environment = environment with
            {
                OutsideAirTemperatureCelsius = E(environment.OutsideAirTemperatureCelsius),
                WindDirectionDegreesTrue = E(environment.WindDirectionDegreesTrue),
                WindSpeedKnots = E(environment.WindSpeedKnots),
                Precipitation = E(environment.Precipitation),
                PrecipitationRateMillimeters = E(environment.PrecipitationRateMillimeters),
            },
        };
    }
}
