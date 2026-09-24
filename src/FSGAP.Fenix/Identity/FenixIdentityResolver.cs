using FSGAP.Abstractions.Aircraft;
using FSGAP.Fenix.Detection;
using Microsoft.Extensions.Logging;

namespace FSGAP.Fenix.Identity;

/// <summary>
/// Combines what the simulator reports about the loaded Fenix with the matching installed livery.
/// </summary>
/// <remarks>
/// <para>Priority rules:</para>
/// <list type="bullet">
/// <item><description>
/// <b>Registration:</b> the installed livery matched by <c>LIVERY FOLDER</c>, then the simulator's <c>ATC ID</c>,
/// otherwise unknown. The livery folder comes first because the ATC id can be empty, generic, or inconsistent
/// between liveries. It is not always empty: SX-DNH was read live on a Fenix A321.
/// </description></item>
/// <item><description>
/// <b>Model, engine, wingtip:</b> the loaded <c>TITLE</c> first, because it names the variant MSFS actually loaded;
/// the installed livery's <c>required_tags</c> fill in what the title does not say. When both are known and differ,
/// the title wins and the conflict is logged, never silently merged.
/// </description></item>
/// <item><description>
/// <b>Livery name:</b> the simulator's <c>LIVERY NAME</c>, then the installed livery's display name.
/// </description></item>
/// <item><description><b>Operator ICAO:</b> installed livery only.</description></item>
/// </list>
/// </remarks>
internal static class FenixIdentityResolver
{
    public static AircraftIdentity Resolve(FenixVariant loaded, AircraftDescriptor aircraft, InstalledAircraft? installed, ILogger logger)
    {
        var fromCatalog = installed?.Identity;
        var engine = Prefer(loaded.Engine, ParseEngine(fromCatalog?.EngineVariant), "engine", aircraft, logger);
        var wingtip = Prefer(loaded.Wingtip, ParseWingtip(fromCatalog?.WingtipConfiguration), "wingtip", aircraft, logger);
        if (fromCatalog?.Model is { } catalogModel && catalogModel != loaded.Model.ToString())
        {
            logger.LogWarning(
                "Loaded Fenix model {TitleModel} differs from the installed livery '{LiveryFolder}' ({CatalogModel}); keeping the loaded model",
                loaded.Model,
                aircraft.LiveryFolder,
                catalogModel);
        }

        return FenixIdentity.Create(
            loaded.Model,
            engine,
            wingtip,
            fromCatalog?.Registration ?? RegistrationText.Normalize(aircraft.Registration),
            aircraft.Livery ?? fromCatalog?.Livery,
            fromCatalog?.OperatorIcao);
    }

    private static T? Prefer<T>(T? fromTitle, T? fromCatalog, string what, AircraftDescriptor aircraft, ILogger logger)
        where T : struct
    {
        if (fromTitle is { } title && fromCatalog is { } catalog && !EqualityComparer<T>.Default.Equals(title, catalog))
        {
            logger.LogWarning(
                "Fenix {What} conflict for '{Title}': title says {FromTitle}, installed livery '{LiveryFolder}' says {FromCatalog}; keeping the title",
                what,
                aircraft.Title,
                title,
                aircraft.LiveryFolder,
                catalog);
        }

        return fromTitle ?? fromCatalog;
    }

    private static FenixEngine? ParseEngine(string? value) =>
        value is null ? null : FenixTokens.Engine([value]);

    private static FenixWingtip? ParseWingtip(string? value) => value switch
    {
        "Sharklets" => FenixWingtip.Sharklets,
        "WingtipFence" => FenixWingtip.WingtipFence,
        _ => null,
    };
}
