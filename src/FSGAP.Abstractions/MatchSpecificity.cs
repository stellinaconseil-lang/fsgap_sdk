namespace FSGAP.Abstractions;

/// <summary>
/// How specifically a provider supports an aircraft. When several providers support the same aircraft, the most
/// specific one wins.
/// </summary>
public enum MatchSpecificity
{
    /// <summary>The provider does not support the aircraft.</summary>
    None = 0,

    /// <summary>
    /// The provider supports the aircraft through generic means only (e.g. standard simulator variables), and
    /// should give way to a dedicated provider.
    /// </summary>
    Generic = 1,

    /// <summary>The provider is dedicated to this aircraft (e.g. a Fenix provider for a Fenix A320).</summary>
    Dedicated = 2,
}
