namespace FSGAP.Abstractions.Failures;

/// <summary>A failure currently active on the aircraft, expressed in normalized terms.</summary>
public sealed record AircraftFailure
{
    /// <summary>Normalized failure type; <see cref="FailureType.Unclassified"/> when the provider cannot map it.</summary>
    public required FailureType Type { get; init; }

    /// <summary>Affected system instance.</summary>
    public required FailureTarget Target { get; init; }

    /// <summary>Operational severity, when the provider assesses it.</summary>
    public FailureSeverity Severity { get; init; }

    /// <summary>Human-readable description for display. Not a vendor identifier and not meant to be parsed.</summary>
    public string? Description { get; init; }
}
