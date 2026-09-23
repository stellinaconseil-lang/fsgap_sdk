namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of a <see cref="TelemetryValue{T}"/>.</summary>
public enum ValueState
{
    /// <summary>
    /// The provider cannot supply this value for this aircraft at all (not implemented, or not exposed by the
    /// aircraft). This is the default state, so an unset value is never mistaken for <c>false</c> or <c>0</c>.
    /// </summary>
    Unavailable = 0,

    /// <summary>
    /// The provider supports this value, but has no valid reading right now: never received, invalid, or stale
    /// (older than the freshness limit).
    /// </summary>
    Unknown = 1,

    /// <summary>The value is known, fresh and can be read.</summary>
    Known = 2,
}
