using System.Text.Json;
using System.Text.Json.Serialization;
using FSGAP.Abstractions.Aircraft;
using Microsoft.Extensions.Logging;

namespace FSGAP.Synaptic.Catalog;

/// <summary>One logical Synaptic A220 livery as the catalog knows it.</summary>
/// <param name="LiveryName">The livery name the simulator enumerates (the grouping key with the model).</param>
/// <param name="Presets">Preset titles the livery was enumerated under (cabin, no cabin).</param>
/// <param name="LiveryFolder">Livery folder, learned when the user loaded this livery; <see langword="null"/> until then.</param>
/// <param name="Registration">Resolved registration, or <see langword="null"/>.</param>
/// <param name="RegistrationSource">Where <paramref name="Registration"/> came from (kept unchanged when cached).</param>
/// <param name="LastObserved">When the livery was last seen loaded; <see langword="null"/> if never.</param>
internal sealed record LearnedLivery(
    string LiveryName,
    IReadOnlyList<string> Presets,
    string? LiveryFolder,
    string? Registration,
    RegistrationSource? RegistrationSource,
    DateTimeOffset? LastObserved);

/// <summary>
/// Persists the Synaptic catalog (enumerated liveries and what was learned about them) as one small JSON file under
/// <c>FsgapOptions.DataDirectory/synaptic/</c>.
/// </summary>
/// <remarks>
/// Same philosophy as the Fenix catalog cache: an accelerator, never a source of truth; atomic writes (temporary file,
/// then rename); a missing, unreadable, corrupted or other-schema file is ignored with a warning. It holds livery names,
/// folders and registrations only: no path, no user data.
/// </remarks>
internal sealed class LearnedLiveryCache(string path, ILogger logger)
{
    internal const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public IReadOnlyList<LearnedLivery>? TryLoad()
    {
        if (!File.Exists(Path))
        {
            return null;
        }

        try
        {
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(Path), JsonOptions);
            if (file is { SchemaVersion: SchemaVersion, Liveries: not null }
                && file.Liveries.All(l => l is not null && !string.IsNullOrWhiteSpace(l.LiveryName) && l.Presets is not null))
            {
                return file.Liveries;
            }

            logger.LogWarning("Ignoring the Synaptic livery cache {Path}: unexpected content or schema version", Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "Ignoring the unreadable Synaptic livery cache {Path}; it will be rebuilt", Path);
        }

        return null;
    }

    public void Save(IReadOnlyList<LearnedLivery> liveries, DateTimeOffset savedAt)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temporary = $"{Path}.{Environment.ProcessId}.tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new CacheFile(SchemaVersion, savedAt, liveries), JsonOptions));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write the Synaptic livery cache {Path}; the catalog stays in memory only", Path);
        }
    }

    private sealed record CacheFile(int SchemaVersion, DateTimeOffset SavedAt, IReadOnlyList<LearnedLivery>? Liveries);
}
