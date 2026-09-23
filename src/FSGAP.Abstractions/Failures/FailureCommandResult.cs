namespace FSGAP.Abstractions.Failures;

/// <summary>Result of triggering or clearing a failure.</summary>
/// <param name="Status">Outcome of the command.</param>
/// <param name="Message">Optional human-readable detail, typically set when the command did not succeed.</param>
public sealed record FailureCommandResult(FailureCommandStatus Status, string? Message = null)
{
    /// <summary><see langword="true"/> when the command was applied.</summary>
    public bool IsSuccess => Status == FailureCommandStatus.Succeeded;

    /// <summary>A successful result.</summary>
    public static FailureCommandResult Succeeded { get; } = new(FailureCommandStatus.Succeeded);

    /// <summary>Creates a result stating the command is not supported.</summary>
    public static FailureCommandResult NotSupported(string? message = null) => new(FailureCommandStatus.NotSupported, message);

    /// <summary>Creates a result stating the command was refused in the current state.</summary>
    public static FailureCommandResult Rejected(string? message = null) => new(FailureCommandStatus.Rejected, message);

    /// <summary>Creates a result stating the command failed while being applied.</summary>
    public static FailureCommandResult Failed(string? message = null) => new(FailureCommandStatus.Failed, message);
}
