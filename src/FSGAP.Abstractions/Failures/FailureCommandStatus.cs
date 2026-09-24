namespace FSGAP.Abstractions.Failures;

/// <summary>Outcome of a <see cref="FailureCommand"/>.</summary>
/// <remarks>
/// Two outcomes tell a caller whether retrying is safe: <see cref="Unavailable"/> means nothing was applied (retry
/// later), <see cref="Unconfirmed"/> means the command may have been applied (read the active failures before
/// deciding, never retry blindly).
/// </remarks>
public enum FailureCommandStatus
{
    /// <summary>The command was applied, and the aircraft confirmed the requested state.</summary>
    Succeeded = 0,

    /// <summary>The provider does not support this failure type, target or operation. Nothing was sent.</summary>
    NotSupported,

    /// <summary>The command is supported but was refused in the current state (e.g. unknown target instance).</summary>
    Rejected,

    /// <summary>The command was attempted but an error occurred while applying it.</summary>
    Failed,

    /// <summary>
    /// The failure system could not be reached (not running, connection refused, or the attached aircraft is no
    /// longer loaded). The command was not applied; it can be retried later.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The command was sent but its outcome is unknown: no answer in time, or the aircraft did not confirm the
    /// requested state. It may or may not have been applied, so it must not be retried blindly.
    /// </summary>
    Unconfirmed,
}
