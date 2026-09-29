namespace FSGAP.Abstractions.Aircraft;

/// <summary>Where a registration came from, and therefore how far it can be trusted.</summary>
/// <remarks>
/// A cached value keeps the source it was first learned from: caching never makes a registration more (or less)
/// trustworthy, so there is deliberately no "cached" member.
/// </remarks>
public enum RegistrationSource
{
    /// <summary>
    /// Parsed from a name that happens to contain it (for example a livery folder <c>AIR FRANCE F-HZUF</c>). A
    /// candidate, not a declaration: the text may be wrong.
    /// </summary>
    Derived = 1,

    /// <summary>Reported by the simulator for this aircraft and corroborated by the livery the provider sees.</summary>
    Observed = 2,

    /// <summary>Declared by the livery's own metadata (for example <c>atc_id</c> in its configuration file).</summary>
    Authoritative = 3,
}
