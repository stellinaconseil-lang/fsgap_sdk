namespace FSGAP.Abstractions.Failures;

/// <summary>
/// Coarse, optional classification of a failure, for grouping and display. It is <b>not</b> an identity: two
/// different failures can share a category, and a failure is always identified by its <see cref="FailureKey"/>.
/// </summary>
public enum FailureCategory
{
    /// <summary>Not classified, or no better category.</summary>
    Other = 0,

    /// <summary>Air conditioning and pressurization.</summary>
    AirConditioning,

    /// <summary>Auxiliary power unit.</summary>
    Apu,

    /// <summary>Autoflight (autopilot, flight management).</summary>
    Autoflight,

    /// <summary>Communications.</summary>
    Communications,

    /// <summary>Doors.</summary>
    Doors,

    /// <summary>Electrical power.</summary>
    Electrical,

    /// <summary>Engines (power plant, engine controls, starting).</summary>
    Engine,

    /// <summary>Fire and smoke detection and protection.</summary>
    Fire,

    /// <summary>Flight controls.</summary>
    FlightControls,

    /// <summary>Fuel.</summary>
    Fuel,

    /// <summary>Hydraulic power.</summary>
    Hydraulic,

    /// <summary>Ice and rain protection.</summary>
    IceAndRain,

    /// <summary>Indicating and displays.</summary>
    Instruments,

    /// <summary>Landing gear, brakes and tyres.</summary>
    LandingGear,

    /// <summary>Navigation.</summary>
    Navigation,

    /// <summary>Oxygen.</summary>
    Oxygen,

    /// <summary>Pneumatic (bleed air).</summary>
    Pneumatic,
}
