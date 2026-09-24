using FSGAP.Abstractions.Aircraft;
using FSGAP.Fenix.Detection;

namespace FSGAP.Fenix.Identity;

/// <summary>
/// Normalized identity values for Fenix aircraft.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Developer <c>Fenix Simulations</c>; manufacturer <c>Airbus</c>.</description></item>
/// <item><description>
/// Family <c>A320</c> for all three models, following the <see cref="AircraftIdentity.Family"/> convention.
/// </description></item>
/// <item><description>Model <c>A319</c>/<c>A320</c>/<c>A321</c>.</description></item>
/// <item><description>
/// <c>IcaoType</c> is <b>inferred from the recognized model</b>. For these three models the model name is the ICAO
/// type designator. It is not a value read from MSFS.
/// </description></item>
/// <item><description>
/// <c>Variant</c> stays null: the sub-series (e.g. A320-214) is not known.
/// </description></item>
/// <item><description>
/// Engine family <c>CFM</c>/<c>IAE</c>; wingtip <c>Sharklets</c>/<c>WingtipFence</c>. Both are null when unknown,
/// never defaulted.
/// </description></item>
/// </list>
/// </remarks>
internal static class FenixIdentity
{
    public const string Developer = "Fenix Simulations";
    public const string Manufacturer = "Airbus";
    public const string Family = "A320";

    public static AircraftIdentity Create(
        FenixModel? model,
        FenixEngine? engine,
        FenixWingtip? wingtip,
        string? registration,
        string? livery,
        string? operatorIcao) => new()
        {
            Developer = Developer,
            Manufacturer = Manufacturer,
            Family = Family,
            Model = model?.ToString(),
            IcaoType = model?.ToString(),
            EngineVariant = engine switch
            {
                FenixEngine.Cfm => "CFM",
                FenixEngine.Iae => "IAE",
                _ => null,
            },
            WingtipConfiguration = wingtip switch
            {
                FenixWingtip.Sharklets => "Sharklets",
                FenixWingtip.WingtipFence => "WingtipFence",
                _ => null,
            },
            Registration = registration,
            Livery = livery,
            OperatorIcao = operatorIcao,
        };
}
