using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Fenix.Telemetry;

/// <summary>
/// What a Fenix session keeps, and what it hides, of the simulator's generic telemetry.
/// </summary>
/// <remarks>
/// <para>
/// The Fenix A32x simulates its own systems outside the stock MSFS model, so some stock SimVars read a value that
/// looks plausible and is wrong for this aircraft. The generic transport cannot know that, and must not: it does
/// not know what a Fenix is. The knowledge lives here, and is applied as a pure transformation of each generic
/// snapshot on the Fenix session (<see cref="Core.Telemetry.TransformedTelemetryProvider"/>).
/// </para>
/// <para>
/// A value confirmed wrong on Fenix becomes <see cref="TelemetryValue{T}.Unavailable"/>, not Unknown: no amount of
/// waiting will make it right. Source of every decision: docs/audits/fenix-fsgap-mapping.md.
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Masked:</b> <see cref="FlightControlsTelemetry.SpeedBrakeDeploymentPercent"/>. <c>SPOILERS LEFT/RIGHT
/// POSITION</c> has the wrong scale on Fenix (audit §1.5).
/// </description></item>
/// <item><description>
/// <b>Accepted:</b> flight state, warnings, engines (combustion, N1, N2, EGT, fuel flow), gear handle and legs, flaps
/// handle and trailing-edge surfaces (the latter verified on Fenix by the audit).
/// </description></item>
/// <item><description>
/// <b>Not read generically, so nothing to mask:</b> the other SimVars the audit lists as wrong on Fenix (APU RPM,
/// starter and generator, engine anti-ice, slats, yellow hydraulics, BAT2). Where FSGAP.Fenix has a proven Fenix
/// source, <see cref="FenixTelemetryComposer"/> supplies the value instead (green and blue hydraulics); otherwise it
/// stays Unavailable (APU operating state, yellow hydraulics, batteries).
/// </description></item>
/// </list>
/// <para>
/// The mask runs first in <see cref="FenixTelemetryComposer.Compose"/>, which then lays the Fenix-specific sections
/// over the masked generic snapshot.
/// </para>
/// </remarks>
internal static class FenixGenericTelemetryPolicy
{
    /// <summary>
    /// What a Fenix session declares when it has generic telemetry underneath: the sections the policy keeps at
    /// least partly. Flight controls stays declared, because the flaps are supported although the speed brake is not.
    /// </summary>
    internal static TelemetryCapabilities GenericSections { get; } = new()
    {
        FlightState = true,
        Warnings = true,
        Engines = true,
        LandingGear = true,
        FlightControls = true,
    };

    /// <summary>
    /// What the Fenix-specific reads add: inertial reference modes, fuel pump switches, green/blue hydraulic
    /// pressure, and the fire panel (handles, engine fire lights). Not <c>Apu</c> (no proven source for the APU's
    /// operating state), not <c>Electrical</c> (no contract field for the one reliable battery reading).
    /// </summary>
    internal static TelemetryCapabilities SystemSections { get; } = new()
    {
        InertialReferences = true,
        FuelPumps = true,
        Hydraulics = true,
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
    };

    /// <summary>Returns the snapshot with every value known to be wrong on Fenix made Unavailable.</summary>
    internal static AircraftTelemetry Apply(AircraftTelemetry generic)
    {
        ArgumentNullException.ThrowIfNull(generic);
        return generic with
        {
            FlightControls = generic.FlightControls with
            {
                SpeedBrakeDeploymentPercent = TelemetryValue<double>.Unavailable,
            },
        };
    }
}
