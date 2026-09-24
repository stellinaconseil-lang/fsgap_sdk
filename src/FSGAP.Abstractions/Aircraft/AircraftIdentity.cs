namespace FSGAP.Abstractions.Aircraft;

/// <summary>
/// Normalized identity of an aircraft, as established by the provider that supports it.
/// </summary>
/// <remarks>
/// Values are open-ended strings rather than closed enumerations so that new developers, manufacturers and
/// families can be supported without changing this contract. Any field may be <see langword="null"/> when the
/// provider cannot establish it reliably; consumers must not substitute a guess.
/// <para>Examples: Fenix / Airbus / A320 / A319 — or PMDG / Boeing / 737 / 737-800.</para>
/// </remarks>
public sealed record AircraftIdentity
{
    /// <summary>Developer or publisher of the simulator add-on (e.g. <c>Fenix Simulations</c>, <c>PMDG</c>).</summary>
    public string? Developer { get; init; }

    /// <summary>Manufacturer of the real aircraft (e.g. <c>Airbus</c>, <c>Boeing</c>, <c>Cessna</c>).</summary>
    public string? Manufacturer { get; init; }

    /// <summary>Aircraft family (e.g. <c>A320</c> for A319/A320/A321, <c>737</c> for 737-700/800/900).</summary>
    public string? Family { get; init; }

    /// <summary>Model within the family (e.g. <c>A319</c>, <c>737-800</c>).</summary>
    public string? Model { get; init; }

    /// <summary>Sub-variant or series when known (e.g. <c>A320-214</c>).</summary>
    public string? Variant { get; init; }

    /// <summary>
    /// Engine variant when known, as precisely as the source allows: an engine family such as <c>CFM</c> or
    /// <c>IAE</c>, or a model such as <c>CFM56-5B</c>.
    /// </summary>
    public string? EngineVariant { get; init; }

    /// <summary>
    /// Wingtip device when known. Conventional values: <c>Sharklets</c>, <c>WingtipFence</c>, <c>Winglets</c>,
    /// <c>None</c>. Open-ended; <see langword="null"/> when unknown.
    /// </summary>
    public string? WingtipConfiguration { get; init; }

    /// <summary>
    /// ICAO designator of the operator whose livery is applied (e.g. <c>AEE</c>, <c>AFR</c>), when the livery
    /// declares one. Never derived from a display name.
    /// </summary>
    public string? OperatorIcao { get; init; }

    /// <summary>ICAO aircraft type designator (e.g. <c>A320</c>, <c>B738</c>).</summary>
    public string? IcaoType { get; init; }

    /// <summary>Registration / tail number.</summary>
    public string? Registration { get; init; }

    /// <summary>Livery name.</summary>
    public string? Livery { get; init; }
}
