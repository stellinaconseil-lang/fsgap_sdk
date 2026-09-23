using System.Diagnostics.CodeAnalysis;

namespace FSGAP.Abstractions.Failures;

/// <summary>A failure currently active on the aircraft, expressed in normalized terms.</summary>
public sealed record AircraftFailure
{
    /// <summary>
    /// Normalized identity of the failure, or <see langword="null"/> when the provider sees an active failure it
    /// cannot map to a key. Such a failure is still reported, so that "something is failed" is never hidden, but
    /// it cannot be commanded. Its vendor identifier is never exposed.
    /// </summary>
    public FailureKey? Key { get; init; }

    /// <summary>Whether the failure maps to a normalized <see cref="Key"/>.</summary>
    [MemberNotNullWhen(true, nameof(Key))]
    public bool IsClassified => Key is not null;

    /// <summary>Affected system instance.</summary>
    public FailureTarget Target { get; init; } = FailureTarget.Aircraft;

    /// <summary>Coarse classification, for grouping only.</summary>
    public FailureCategory Category { get; init; }

    /// <summary>Operational severity, when the provider assesses it.</summary>
    public FailureSeverity Severity { get; init; }

    /// <summary>Human-readable description for display. Not a vendor identifier and not meant to be parsed.</summary>
    public string? Description { get; init; }
}
