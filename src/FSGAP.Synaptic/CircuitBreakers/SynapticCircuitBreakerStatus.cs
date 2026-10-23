using FSGAP.Abstractions.Failures;

namespace FSGAP.Synaptic.CircuitBreakers;

/// <summary>What this session knows of a breaker's cell. Never derived from the Synaptic version.</summary>
internal enum SynapticBreakerInterface
{
    /// <summary>Not used yet in this session: its cell has not been read (an unknown L: is never read speculatively).</summary>
    Unverified,

    /// <summary>The cell was read with a coherent value (0 or 1). A readable cell is not a qualified write.</summary>
    Accessible,

    /// <summary>The cell could not be read while the aircraft was attached and the simulator answered: disabled for the session.</summary>
    Unavailable,

    /// <summary>The cell held a value that is neither 0 nor 1: disabled for the session.</summary>
    IncoherentValue,

    /// <summary>A write of the cell was not confirmed by its read-back (trip or reset): disabled for the session, never retried automatically.</summary>
    WriteIneffective,
}

/// <summary>
/// Independent, read-only evidence that the breaker belongs to the loaded aircraft: its input event (<c>B:</c>) in the
/// aircraft's own input-event list. Required before the breaker's cell is read or written; never derived from the cell.
/// </summary>
internal enum SynapticBreakerEvidence
{
    /// <summary>Not checked yet in this session.</summary>
    NotChecked,

    /// <summary>The breaker's input event is listed by the loaded aircraft with the hash the lab recorded.</summary>
    EventPresent,

    /// <summary>The input event is listed under the same name with another hash (another Synaptic build): presence established, change recorded.</summary>
    EventPresentHashChanged,

    /// <summary>The loaded aircraft does not list the input event: compatibility unverified, the cell is never read nor written.</summary>
    EventAbsent,

    /// <summary>The input-event list could not be obtained (not connected, timeout, aircraft not ready): nothing done, retry later.</summary>
    InventoryUnavailable,

    /// <summary>The session has no input-event inventory at all: breakers are listed but never executable.</summary>
    NoInventory,
}

/// <summary>Outcome of the last trip or reset this session attempted on a breaker.</summary>
internal enum SynapticBreakerExecution
{
    /// <summary>No trip or reset attempted in this session.</summary>
    NotAttempted,

    /// <summary>
    /// POSITION_CONFIRMED: the cell reached the requested value and was read back. Says nothing about the aircraft's
    /// reaction (see <see cref="SynapticBreakerFunctionalEffect"/>).
    /// </summary>
    PositionConfirmed,

    /// <summary>The trip or reset failed or was refused for compatibility (explicit result returned to the caller).</summary>
    Failed,
}

/// <summary>Whether an operational effect of the breaker has been observed. Never inferred from a successful write.</summary>
internal enum SynapticBreakerFunctionalEffect
{
    /// <summary>
    /// AIRCRAFT_EFFECT_UNVERIFIED: no effect of the breaker on the aircraft is observed by FSGAP (50 of 55: not observable
    /// from telemetry; a confirmed position never upgrades this).
    /// </summary>
    AircraftEffectUnverified,

    /// <summary>Observed in the lab as a system response (the master caution rises when the breaker is pulled).</summary>
    MasterCautionInLab,
}

/// <summary>
/// One breaker as this session sees it, each dimension separate: listed in the catalog, independent evidence (input event),
/// interface (cell) state, experimental qualification of the direct trip, whether it is executable here, the last
/// execution (position confirmed or not), the observed effect on the aircraft.
/// </summary>
internal sealed record SynapticCircuitBreakerStatus(
    string Id,
    FailureKey Key,
    bool Listed,
    SynapticBreakerEvidence Evidence,
    SynapticBreakerInterface Interface,
    SynapticBreakerQualification TripQualification,
    bool Executable,
    string? NotExecutableReason,
    SynapticBreakerExecution LastExecution,
    string? LastExecutionMessage,
    SynapticBreakerFunctionalEffect FunctionalEffect);
