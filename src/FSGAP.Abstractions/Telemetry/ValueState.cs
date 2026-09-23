namespace FSGAP.Abstractions.Telemetry;

/// <summary>State of a <see cref="TelemetryValue{T}"/>.</summary>
public enum ValueState
{
    /// <summary>
    /// The provider cannot supply this value for this aircraft at all (not implemented, or not exposed by the
    /// aircraft). This is the default state, so an unset value is never mistaken for <c>false</c> or <c>0</c>.
    /// </summary>
    Unavailable = 0,

    /// <summary>The provider supports this value, but has no valid reading right now (not received yet, stale, invalid).</summary>
    Unknown = 1,

    /// <summary>The value is known and can be read.</summary>
    Known = 2,
}
