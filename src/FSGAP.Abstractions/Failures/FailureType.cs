namespace FSGAP.Abstractions.Failures;

/// <summary>
/// Normalized, vendor-neutral failure types. Providers translate these to and from their own failure identifiers,
/// which never appear in the public API.
/// </summary>
/// <remarks>
/// The list is intentionally short and grows only when a consumer needs a new type and at least one provider can
/// implement it. Combine with a <see cref="FailureTarget"/> to designate which system instance is affected.
/// </remarks>
public enum FailureType
{
    /// <summary>
    /// An active failure the provider cannot map to a normalized type. Only reported by
    /// <see cref="IFailureProvider.GetActiveFailuresAsync"/>; it cannot be triggered or cleared.
    /// </summary>
    Unclassified = 0,

    /// <summary>Engine failure (flame-out / loss of thrust).</summary>
    EngineFailure,

    /// <summary>Engine fire.</summary>
    EngineFire,

    /// <summary>APU failure.</summary>
    ApuFailure,

    /// <summary>APU fire.</summary>
    ApuFire,

    /// <summary>Fuel pump failure.</summary>
    FuelPumpFailure,

    /// <summary>Loss of a hydraulic system.</summary>
    HydraulicSystemFailure,

    /// <summary>Loss of an electrical bus.</summary>
    ElectricalBusFailure,

    /// <summary>Inertial reference unit failure.</summary>
    InertialReferenceFailure,
}
