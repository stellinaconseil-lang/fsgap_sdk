namespace FSGAP.Abstractions.Capabilities;

/// <summary>
/// Telemetry sections a provider can read. Each flag matches a section of
/// <see cref="Telemetry.AircraftTelemetry"/>.
/// </summary>
/// <remarks>
/// A supported section may still contain individual values that are unavailable (e.g. engines supported but
/// no EGT reading); each value's <see cref="Telemetry.ValueState"/> is authoritative at that level. An
/// unsupported section is always entirely unavailable or empty.
/// </remarks>
public sealed record TelemetryCapabilities
{
    /// <summary>No telemetry section supported.</summary>
    public static TelemetryCapabilities None { get; } = new();

    /// <summary>Position, attitude and speeds.</summary>
    public bool FlightState { get; init; }

    /// <summary>Flight-envelope warnings (overspeed, flap/gear speed, stall).</summary>
    public bool Warnings { get; init; }

    /// <summary>Engine readings.</summary>
    public bool Engines { get; init; }

    /// <summary>APU operating readings (available, running, master, bleed). The APU fire handle is under <see cref="Fire"/>.</summary>
    public bool Apu { get; init; }

    /// <summary>Inertial reference units.</summary>
    public bool InertialReferences { get; init; }

    /// <summary>Fuel pumps.</summary>
    public bool FuelPumps { get; init; }

    /// <summary>Electrical buses and batteries.</summary>
    public bool Electrical { get; init; }

    /// <summary>Hydraulic systems.</summary>
    public bool Hydraulics { get; init; }

    /// <summary>
    /// Fire: detection zones, engine/APU fire detection, and the fire panel state (fire handles, fire warning
    /// lights). A provider may support the panel state while fire detection itself stays unavailable.
    /// </summary>
    public bool Fire { get; init; }

    /// <summary>Landing gear handle and units, wheel brakes and steering.</summary>
    public bool LandingGear { get; init; }

    /// <summary>Flap handle, flap surfaces, speed brake and control surface deflections.</summary>
    public bool FlightControls { get; init; }

    /// <summary>Cabin pressurization.</summary>
    public bool Pressurization { get; init; }

    /// <summary>Weather around the aircraft.</summary>
    public bool Environment { get; init; }
}
