namespace FSGAP.Abstractions.Failures;

/// <summary>A request to trigger or clear a normalized failure on a target.</summary>
/// <param name="Type">Failure type.</param>
/// <param name="Target">Affected system instance.</param>
public sealed record FailureCommand(FailureType Type, FailureTarget Target);
