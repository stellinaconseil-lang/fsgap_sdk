using System.Diagnostics.CodeAnalysis;

namespace FSGAP.Core.Resolution;

/// <summary>Result of resolving which provider should handle an aircraft.</summary>
public sealed record ProviderResolution
{
    private ProviderResolution(ProviderResolutionStatus status, ProviderMatch? selected, IReadOnlyList<ProviderMatch> candidates)
    {
        Status = status;
        Selected = selected;
        Candidates = candidates;
    }

    /// <summary>Outcome of the resolution.</summary>
    public ProviderResolutionStatus Status { get; }

    /// <summary><see langword="true"/> when a single provider was selected.</summary>
    [MemberNotNullWhen(true, nameof(Selected))]
    public bool IsResolved => Status == ProviderResolutionStatus.Resolved;

    /// <summary>The selected provider and its match; <see langword="null"/> unless <see cref="IsResolved"/>.</summary>
    public ProviderMatch? Selected { get; }

    /// <summary>
    /// Every provider that supports the aircraft, most specific first, in registration order within a specificity.
    /// Empty when <see cref="Status"/> is <see cref="ProviderResolutionStatus.NotSupported"/>.
    /// </summary>
    public IReadOnlyList<ProviderMatch> Candidates { get; }

    internal static ProviderResolution FromCandidates(IReadOnlyList<ProviderMatch> candidates)
    {
        if (candidates.Count == 0)
        {
            return new ProviderResolution(ProviderResolutionStatus.NotSupported, null, candidates);
        }

        var best = candidates[0].Match.Specificity;
        var tied = candidates.Count > 1 && candidates[1].Match.Specificity == best;
        return tied
            ? new ProviderResolution(ProviderResolutionStatus.Ambiguous, null, candidates)
            : new ProviderResolution(ProviderResolutionStatus.Resolved, candidates[0], candidates);
    }
}
