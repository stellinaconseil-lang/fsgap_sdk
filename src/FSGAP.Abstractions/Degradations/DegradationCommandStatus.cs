namespace FSGAP.Abstractions.Degradations;

/// <summary>Outcome of <see cref="IDegradationProvider.ApplyAsync"/> or <see cref="IDegradationProvider.RestoreAsync"/>.</summary>
/// <remarks>
/// As for failures, two outcomes tell a caller whether retrying is safe: <see cref="Unavailable"/> means nothing was
/// written (retry later), <see cref="Unconfirmed"/> means a write was sent but the read-back did not confirm it (read the
/// state before deciding, never retry blindly).
/// </remarks>
public enum DegradationCommandStatus
{
    /// <summary>The write was made and the read-back confirmed the requested control value.</summary>
    Succeeded = 0,

    /// <summary>The provider does not support this degradation or operation. Nothing was written.</summary>
    NotSupported,

    /// <summary>
    /// Refused in the current state; nothing was written (another degradation is active, the control is already degraded
    /// without FSGAP, the control holds an unexpected value, or the degradation is not owned by this session).
    /// </summary>
    Rejected,

    /// <summary>The aircraft control could not be reached (not connected, aircraft gone, read failed). Nothing was written.</summary>
    Unavailable,

    /// <summary>A write was sent but its outcome is unknown: the write did not complete, or the read-back did not confirm it.</summary>
    Unconfirmed,
}
