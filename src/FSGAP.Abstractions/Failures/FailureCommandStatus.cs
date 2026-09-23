namespace FSGAP.Abstractions.Failures;

/// <summary>Outcome of a <see cref="FailureCommand"/>.</summary>
public enum FailureCommandStatus
{
    /// <summary>The command was applied.</summary>
    Succeeded = 0,

    /// <summary>The provider does not support this failure type, target or operation.</summary>
    NotSupported,

    /// <summary>The command is supported but was refused in the current state (e.g. unknown target instance).</summary>
    Rejected,

    /// <summary>The command was attempted but an error occurred while applying it.</summary>
    Failed,
}
