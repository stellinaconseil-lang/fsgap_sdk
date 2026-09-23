using FSGAP.Abstractions;

namespace FSGAP.Core.Resolution;

/// <summary>A provider together with its positive answer for an aircraft.</summary>
/// <param name="Provider">The provider.</param>
/// <param name="Match">Its match for the aircraft.</param>
public sealed record ProviderMatch(IAircraftProvider Provider, AircraftMatch Match);
