namespace FSGAP.Abstractions.Failures;

/// <summary>Kind of system a failure applies to.</summary>
public enum FailureTargetKind
{
    /// <summary>The aircraft as a whole, or no specific system.</summary>
    Aircraft = 0,

    /// <summary>An engine, designated by its 1-based index.</summary>
    Engine,

    /// <summary>The auxiliary power unit.</summary>
    Apu,

    /// <summary>A fuel pump, designated by its normalized id.</summary>
    FuelPump,

    /// <summary>A hydraulic system, designated by its normalized id.</summary>
    HydraulicSystem,

    /// <summary>An electrical bus, designated by its normalized id.</summary>
    ElectricalBus,

    /// <summary>An inertial reference unit, designated by its 1-based index.</summary>
    InertialReference,
}
