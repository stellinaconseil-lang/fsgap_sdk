using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Synaptic.Telemetry;

/// <summary>
/// What a Synaptic A220 session keeps, and what it hides, of the simulator's generic telemetry. Every decision comes
/// from the BLOCK 10A-LIVE matrix (docs/audits/synaptic-a220-discovery.md §6); only values proven wrong are hidden.
/// </summary>
/// <remarks>
/// <para><b>Masked (GENERIC_WRONG, compared with the EICAS or dead):</b></para>
/// <list type="bullet">
/// <item><description><c>Engines[n].Running</c>: false with both engines running, even in flight.</description></item>
/// <item><description><c>Engines[n].FuelFlowKilogramsPerHour</c>: 0 at every thrust (EICAS 325–715 kg/h).</description></item>
/// <item><description><c>Engines[n].StarterActive</c>: never true during either engine start.</description></item>
/// <item><description><c>Engines[n].OilTemperatureCelsius</c>: constant at about the OAT (EICAS 32–51 °C).</description></item>
/// <item><description><c>Engines[n].OilPressurePsi</c>: 25–30 % below the EICAS, identical on both engines.</description></item>
/// <item><description><c>Apu.BleedOn</c>: false while APU bleed started both engines (the overlay supplies the selection).</description></item>
/// <item><description><c>Pressurization</c> (both fields): disagreed with the EICAS on the ground and froze at 8 132 ft in cruise.</description></item>
/// </list>
/// <para>
/// <b>Kept:</b> everything else the generic transport reads. That includes fields validated live (position, speeds,
/// attitude, N1, N2, EGT, throttle, gear, flaps, surfaces, touchdown, speed brake, weather) and fields not proven wrong
/// (the four envelope warnings, reverser, antiskid). An untested field is not a wrong field.
/// </para>
/// <para>
/// The generic electrical, battery and hydraulic SimVars (dead or suspect on the A220) are not read by the generic
/// transport at all, so there is nothing to mask; the session does not declare those sections.
/// </para>
/// </remarks>
internal static class SynapticGenericTelemetryPolicy
{
    /// <summary>Sections a Synaptic session declares from the generic telemetry.</summary>
    internal static TelemetryCapabilities GenericSections { get; } = new()
    {
        FlightState = true,
        Warnings = true,
        Engines = true,
        LandingGear = true,
        FlightControls = true,
        Environment = true,
    };

    /// <summary>Sections the Synaptic overlay adds: boost pumps, the APU switches, the engine fire pushbuttons.</summary>
    internal static TelemetryCapabilities OverlaySections { get; } = new()
    {
        FuelPumps = true,
        Apu = true,
        Fire = true,
    };

    /// <summary>The union of two section sets.</summary>
    internal static TelemetryCapabilities Union(TelemetryCapabilities a, TelemetryCapabilities b) => new()
    {
        FlightState = a.FlightState || b.FlightState,
        Warnings = a.Warnings || b.Warnings,
        Engines = a.Engines || b.Engines,
        Apu = a.Apu || b.Apu,
        InertialReferences = a.InertialReferences || b.InertialReferences,
        FuelPumps = a.FuelPumps || b.FuelPumps,
        Electrical = a.Electrical || b.Electrical,
        Hydraulics = a.Hydraulics || b.Hydraulics,
        Fire = a.Fire || b.Fire,
        LandingGear = a.LandingGear || b.LandingGear,
        FlightControls = a.FlightControls || b.FlightControls,
        Pressurization = a.Pressurization || b.Pressurization,
        Environment = a.Environment || b.Environment,
    };

    /// <summary>Returns the snapshot with every generic value proven wrong on the A220 made Unavailable.</summary>
    internal static AircraftTelemetry Apply(AircraftTelemetry generic)
    {
        ArgumentNullException.ThrowIfNull(generic);
        return generic with
        {
            Engines = generic.Engines.Select(e => e with
            {
                Running = TelemetryValue<bool>.Unavailable,
                FuelFlowKilogramsPerHour = TelemetryValue<double>.Unavailable,
                StarterActive = TelemetryValue<bool>.Unavailable,
                OilTemperatureCelsius = TelemetryValue<double>.Unavailable,
                OilPressurePsi = TelemetryValue<double>.Unavailable,
            }).ToArray(),
            Apu = generic.Apu with { BleedOn = TelemetryValue<bool>.Unavailable },
            Pressurization = new PressurizationTelemetry(),
        };
    }
}
