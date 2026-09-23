using System.Diagnostics.CodeAnalysis;
using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Abstractions;

/// <summary>Answer of a provider to "do you support this aircraft?".</summary>
public sealed record AircraftMatch
{
    private AircraftMatch(MatchSpecificity specificity, AircraftIdentity? identity)
    {
        Specificity = specificity;
        Identity = identity;
    }

    /// <summary>The provider does not support the aircraft.</summary>
    public static AircraftMatch NotSupported { get; } = new(MatchSpecificity.None, null);

    /// <summary>Whether the provider supports the aircraft.</summary>
    [MemberNotNullWhen(true, nameof(Identity))]
    public bool IsSupported => Specificity != MatchSpecificity.None;

    /// <summary>How specifically the aircraft is supported; <see cref="MatchSpecificity.None"/> when not supported.</summary>
    public MatchSpecificity Specificity { get; }

    /// <summary>Normalized identity established by the provider; <see langword="null"/> when not supported.</summary>
    public AircraftIdentity? Identity { get; }

    /// <summary>Creates a positive match.</summary>
    /// <param name="identity">Normalized identity of the recognized aircraft.</param>
    /// <param name="specificity">Match specificity; must not be <see cref="MatchSpecificity.None"/>.</param>
    public static AircraftMatch Supported(AircraftIdentity identity, MatchSpecificity specificity = MatchSpecificity.Dedicated)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!Enum.IsDefined(specificity) || specificity == MatchSpecificity.None)
        {
            throw new ArgumentOutOfRangeException(nameof(specificity), specificity, "A supported match needs a specificity other than None.");
        }

        return new AircraftMatch(specificity, identity);
    }
}
