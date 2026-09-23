namespace FSGAP.Abstractions.Aircraft;

/// <summary>
/// Raw description of the aircraft currently loaded in the simulator, as reported by a detection source
/// (for example the simulator's aircraft title and ATC data), before any provider has interpreted it.
/// </summary>
/// <remarks>
/// Every field is optional: detection sources differ in what they expose, and a missing value means
/// "not reported", never an empty or default value. Providers inspect a descriptor in
/// <see cref="IAircraftProvider.Match"/> to decide whether they support the aircraft.
/// </remarks>
public sealed record AircraftDescriptor
{
    /// <summary>Aircraft title as reported by the simulator (usually includes the livery name).</summary>
    public string? Title { get; init; }

    /// <summary>Manufacturer as reported by the simulator, e.g. the ATC type string.</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Model as reported by the simulator, e.g. the ATC model string.</summary>
    public string? Model { get; init; }

    /// <summary>ICAO aircraft type designator when reported (e.g. <c>A320</c>, <c>B738</c>, <c>C172</c>).</summary>
    public string? IcaoType { get; init; }

    /// <summary>Registration / tail number (ATC id) when reported.</summary>
    public string? Registration { get; init; }

    /// <summary>Livery name when reported separately from the title.</summary>
    public string? Livery { get; init; }

    /// <summary>
    /// Path or package identifier of the aircraft content in the simulator (for example the package folder or
    /// the path of its configuration file) when available.
    /// </summary>
    public string? PackagePath { get; init; }
}
