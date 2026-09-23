namespace FSGAP.Abstractions.Telemetry;

/// <summary>Operating mode of an inertial reference unit.</summary>
public enum InertialReferenceMode
{
    /// <summary>Unit switched off.</summary>
    Off = 0,

    /// <summary>Unit aligning.</summary>
    Align = 1,

    /// <summary>Full navigation mode.</summary>
    Navigation = 2,

    /// <summary>Attitude-only (reversionary) mode.</summary>
    Attitude = 3,
}
