using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Synaptic.Identity;

/// <summary>The normalized identity of the Synaptic A220-300.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><description><c>IcaoType</c> is the ICAO Doc 8643 designator <c>BCS3</c>, never the simulator's <c>ATC TYPE</c> (<c>223</c>).</description></item>
/// <item><description><c>EngineVariant</c> is the documented engine family <c>PW1500G</c>; the exact thrust rating is not exposed by the aircraft.</description></item>
/// <item><description>No variant, wingtip or operator: no reliable source exists for them.</description></item>
/// </list>
/// </remarks>
internal static class SynapticIdentity
{
    internal const string Developer = "Synaptic Simulations";
    internal const string Manufacturer = "Airbus";
    internal const string Family = "A220";
    internal const string Model = "A220-300";
    internal const string IcaoType = "BCS3";
    internal const string EngineVariant = "PW1500G";

    public static AircraftIdentity Create(string? registration, RegistrationSource? source, string? livery) => new()
    {
        Developer = Developer,
        Manufacturer = Manufacturer,
        Family = Family,
        Model = Model,
        IcaoType = IcaoType,
        EngineVariant = EngineVariant,
        Registration = registration,
        RegistrationSource = registration is null ? null : source,
        Livery = livery,
    };
}
