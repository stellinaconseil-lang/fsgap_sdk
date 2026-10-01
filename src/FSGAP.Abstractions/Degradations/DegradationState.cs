namespace FSGAP.Abstractions.Degradations;

/// <summary>
/// The observed state of a controlled degradation's <b>control</b>, read back from the aircraft. It describes the control
/// configuration, never the health of the component behind it.
/// </summary>
public enum DegradationState
{
    /// <summary>The control was read but holds a value that is neither its qualified normal nor its degraded value.</summary>
    Unknown = 0,

    /// <summary>The control holds its qualified normal value.</summary>
    Normal,

    /// <summary>The control holds its degraded value, and this session put it there.</summary>
    Applied,

    /// <summary>
    /// The control holds its degraded value, but this session did not put it there (set by the pilot, by another tool, or
    /// before a reconnection). FSGAP never restores a degradation it does not own.
    /// </summary>
    PreExisting,

    /// <summary>The control could not be read (simulator not connected, aircraft no longer loaded, read failed).</summary>
    Unavailable,
}
