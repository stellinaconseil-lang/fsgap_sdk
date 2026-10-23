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

/// <summary>Outcome of the last trip or reset this session attempted on a breaker.</summary>
internal enum SynapticBreakerExecution
{
    /// <summary>No trip or reset attempted in this session.</summary>
    NotAttempted,

    /// <summary>The cell reached the requested value and was read back.</summary>
    Succeeded,

    /// <summary>The trip or reset failed (explicit result returned to the caller).</summary>
    Failed,
}

/// <summary>Whether an operational effect of the breaker has been observed. Never inferred from a successful write.</summary>
internal enum SynapticBreakerFunctionalEffect
{
    /// <summary>No effect observed (50 of 55 breakers: not observable from telemetry; pilot EICAS pass pending).</summary>
    NotObserved,

    /// <summary>Observed in the lab as a system response (the master caution rises when the breaker is pulled).</summary>
    MasterCautionInLab,
}

/// <summary>
/// One breaker as this session sees it, each dimension separate: listed in the catalog, interface (cell) state,
/// experimental qualification of the direct trip, whether it is executable here, the last execution, the observed effect.
/// </summary>
internal sealed record SynapticCircuitBreakerStatus(
    string Id,
    FailureKey Key,
    bool Listed,
    SynapticBreakerInterface Interface,
    SynapticBreakerQualification TripQualification,
    bool Executable,
    string? NotExecutableReason,
    SynapticBreakerExecution LastExecution,
    string? LastExecutionMessage,
    SynapticBreakerFunctionalEffect FunctionalEffect);
