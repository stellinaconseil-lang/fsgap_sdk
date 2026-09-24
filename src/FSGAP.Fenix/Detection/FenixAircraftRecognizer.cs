using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Fenix.Detection;

/// <summary>
/// Decides whether the aircraft the simulator reports is a Fenix A319/A320/A321, from the generic descriptor alone.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// Model: the Fenix title rule on <c>TITLE</c> first, then on <c>LIVERY FOLDER</c>. Fenix livery folders
/// usually carry no model (e.g. <c>AEE-SX-DNH-7F2F</c>), but Fenix house liveries do (e.g. <c>FNX_320_Fenix_CFM_WF</c>
/// is not matched, <c>Fenix_A320_house</c> is).
/// </description></item>
/// <item><description>Engine and wingtip: exact tokens of <c>TITLE</c> (the loaded preset variant).</description></item>
/// </list>
/// A title that merely contains "A320" (FlyByWire, iniBuilds, Asobo, generic) never matches: the "Fenix" marker is
/// required.
/// </remarks>
internal static class FenixAircraftRecognizer
{
    public static FenixVariant? Recognize(AircraftDescriptor aircraft)
    {
        ArgumentNullException.ThrowIfNull(aircraft);
        var model = FenixTokens.MatchModel(aircraft.Title) ?? FenixTokens.MatchModel(aircraft.LiveryFolder);
        if (model is null)
        {
            return null;
        }

        var words = FenixTokens.TitleWords(aircraft.Title);
        return new FenixVariant(model.Value, FenixTokens.Engine(words), FenixTokens.Wingtip(words));
    }
}
