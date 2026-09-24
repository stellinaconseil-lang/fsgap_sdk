using System.Text.Json;
using FSGAP.Abstractions.Aircraft;
using Microsoft.Extensions.Logging;

namespace FSGAP.Fenix.Catalog;

/// <summary>
/// Persists the last scan result as one JSON file, so lookups work right after the application starts, before any
/// rescan. The file is only an accelerator; the installed files remain the source of truth.
/// </summary>
/// <remarks>
/// <para>
/// JSON was chosen over SQLite. The catalog holds a few hundred entries, loaded whole into memory and indexed there,
/// so a database would add a dependency without a benefit.
/// </para>
/// <list type="bullet">
/// <item><description>Writes are atomic: temporary file, then rename.</description></item>
/// <item><description>A missing, unreadable, corrupted or other-schema file is ignored and rebuilt by the next refresh.</description></item>
/// <item><description>
/// The file lives only under <c>FsgapOptions.DataDirectory</c>. Nothing is ever written to the simulator or its
/// packages.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class CatalogCache(string path, ILogger logger)
{
    internal const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public string Path { get; } = path;

    public IReadOnlyList<InstalledAircraft>? TryLoad()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(Path), JsonOptions);
            if (file is { SchemaVersion: SchemaVersion, Aircraft: not null } && file.Aircraft.All(a => a?.Identity is not null && a.Id is not null))
            {
                return file.Aircraft;
            }

            logger.LogWarning("Ignoring the installed-aircraft cache {Path}: unexpected content or schema version", Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Ignoring the unreadable installed-aircraft cache {Path}; it will be rebuilt", Path);
        }

        return null;
    }

    public void Save(IReadOnlyList<InstalledAircraft> aircraft, DateTimeOffset savedAt)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temporary = $"{Path}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CacheFile(SchemaVersion, savedAt, aircraft), JsonOptions));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write the installed-aircraft cache {Path}; the catalog stays in memory only", Path);
        }
    }

    private sealed record CacheFile(int SchemaVersion, DateTimeOffset SavedAt, IReadOnlyList<InstalledAircraft>? Aircraft);
}
