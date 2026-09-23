using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Core.Resolution;

/// <summary>
/// Holds the registered aircraft providers and selects the one that should handle a detected aircraft.
/// Thread-safe.
/// </summary>
/// <remarks>
/// Resolution rules: providers that do not support the aircraft are ignored; the highest
/// <see cref="MatchSpecificity"/> wins (a dedicated provider beats a generic one); if several providers share the
/// highest specificity the result is <see cref="ProviderResolutionStatus.Ambiguous"/> and no provider is picked
/// silently. Registration order never decides between two equally specific providers.
/// </remarks>
public sealed class AircraftProviderRegistry
{
    private readonly object _gate = new();
    private readonly List<IAircraftProvider> _providers = [];

    /// <summary>Registered providers, in registration order.</summary>
    public IReadOnlyList<IAircraftProvider> Providers
    {
        get
        {
            lock (_gate)
            {
                return _providers.ToArray();
            }
        }
    }

    /// <summary>Registers a provider.</summary>
    /// <param name="provider">Provider to register.</param>
    /// <exception cref="ArgumentException">The provider has an empty <see cref="IAircraftProvider.ProviderId"/>.</exception>
    /// <exception cref="InvalidOperationException">A provider with the same id (case-insensitive) is already registered.</exception>
    public void Register(IAircraftProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.ProviderId))
        {
            throw new ArgumentException("Provider id must not be empty.", nameof(provider));
        }

        lock (_gate)
        {
            if (_providers.Any(p => string.Equals(p.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"A provider with id '{provider.ProviderId}' is already registered.");
            }

            _providers.Add(provider);
        }
    }

    /// <summary>Selects the provider that should handle <paramref name="aircraft"/>.</summary>
    /// <param name="aircraft">Aircraft detected in the simulator.</param>
    public ProviderResolution Resolve(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);

        var candidates = Providers
            .Select(provider => new ProviderMatch(provider, provider.Match(aircraft)))
            .Where(candidate => candidate.Match.IsSupported)
            .OrderByDescending(candidate => candidate.Match.Specificity) // stable: keeps registration order on ties
            .ToArray();

        return ProviderResolution.FromCandidates(candidates);
    }
}
