using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Core.Tests;

/// <summary>Provider that supports every aircraft whose title contains <c>titleMarker</c>.</summary>
internal sealed class FakeAircraftProvider(string providerId, string titleMarker, MatchSpecificity specificity = MatchSpecificity.Dedicated)
    : IAircraftProvider
{
    public string ProviderId { get; } = providerId;

    public AircraftMatch Match(AircraftDescriptor aircraft) =>
        aircraft.Title?.Contains(titleMarker, StringComparison.OrdinalIgnoreCase) == true
            ? AircraftMatch.Supported(new AircraftIdentity { Model = titleMarker }, specificity)
            : AircraftMatch.NotSupported;

    public Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not needed by registry tests.");
}
