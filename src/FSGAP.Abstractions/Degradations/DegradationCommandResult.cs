namespace FSGAP.Abstractions.Degradations;

/// <summary>Result of applying or restoring a controlled degradation.</summary>
/// <param name="Status">Outcome of the command.</param>
/// <param name="State">The control state observed when the command ended (read back, or the last known reading).</param>
/// <param name="Message">Optional human-readable detail, typically set when the command did not succeed.</param>
public sealed record DegradationCommandResult(DegradationCommandStatus Status, DegradationState State, string? Message = null)
{
    /// <summary><see langword="true"/> when the read-back confirmed the requested control value.</summary>
    public bool IsSuccess => Status == DegradationCommandStatus.Succeeded;
}
