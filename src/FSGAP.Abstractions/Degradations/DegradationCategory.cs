namespace FSGAP.Abstractions.Degradations;

/// <summary>
/// Coarse classification of a degradation by aircraft system, for grouping and display. It is <b>not</b> an identity: a
/// degradation is always identified by its <see cref="DegradationKey"/>.
/// </summary>
public enum DegradationCategory
{
    /// <summary>Not classified, or no better category.</summary>
    Other = 0,

    /// <summary>Air conditioning and pressurization (packs).</summary>
    AirConditioning,

    /// <summary>Electrical power.</summary>
    Electrical,

    /// <summary>Flight controls.</summary>
    FlightControls,

    /// <summary>Hydraulic power.</summary>
    Hydraulic,

    /// <summary>Pneumatic (bleed air).</summary>
    Pneumatic,
}
