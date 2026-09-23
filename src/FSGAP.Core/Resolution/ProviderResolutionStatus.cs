namespace FSGAP.Core.Resolution;

/// <summary>Outcome of <see cref="AircraftProviderRegistry.Resolve"/>.</summary>
public enum ProviderResolutionStatus
{
    /// <summary>Exactly one provider is the best match.</summary>
    Resolved = 0,

    /// <summary>No registered provider supports the aircraft.</summary>
    NotSupported,

    /// <summary>Several providers support the aircraft with the same, highest specificity; none is selected.</summary>
    Ambiguous,
}
