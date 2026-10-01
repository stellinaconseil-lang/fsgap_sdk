namespace FSGAP.Abstractions.Degradations;

/// <summary>
/// A controlled degradation a provider supports: its normalized key, how to present it and what the provider can do
/// with it.
/// </summary>
/// <remarks>
/// A controlled degradation means "FSGAP deliberately put an aircraft system into a documented degraded control
/// configuration" (for example a generator switch forced off). It does <b>not</b> mean that the underlying component
/// failed: the aircraft reacts to the configuration, the component itself is healthy. Contains no vendor data; the
/// mapping from <see cref="Key"/> to an aircraft control lives inside the provider.
/// </remarks>
public sealed record DegradationDescriptor
{
    private readonly string _displayName = string.Empty;
    private readonly string _description = string.Empty;

    /// <summary>Normalized identity of the degradation.</summary>
    public required DegradationKey Key { get; init; }

    /// <summary>Human-readable name, for display.</summary>
    /// <exception cref="ArgumentException">Set to an empty or white-space value.</exception>
    public required string DisplayName
    {
        get => _displayName;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _displayName = value;
        }
    }

    /// <summary>What FSGAP does to the aircraft and the expected consequence, in plain words.</summary>
    /// <exception cref="ArgumentException">Set to an empty or white-space value.</exception>
    public required string Description
    {
        get => _description;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            _description = value;
        }
    }

    /// <summary>Coarse classification, for grouping only.</summary>
    public DegradationCategory Category { get; init; }

    /// <summary>Operations the provider supports for this degradation. Defaults to <see cref="DegradationOperations.None"/>.</summary>
    public DegradationOperations Operations { get; init; }
}
