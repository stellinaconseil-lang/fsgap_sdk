using System.Text.Json;

namespace FSGAP.Fenix.Catalog;

/// <summary>A top-level simulator package identified as belonging to Fenix.</summary>
internal sealed record FenixPackage(string PackagePath, string PackageName);

/// <summary>
/// Finds Fenix packages among the package roots. Ported from the audited applications.
/// </summary>
/// <remarks>
/// A top-level package folder is a candidate when its name contains <c>fenix</c> or <c>fnx</c>, or when its
/// <c>manifest.json</c> title, creator or content type does. This is a discovery rule, not a closed list, because
/// Fenix package names differ between the community and store packages. A candidate without any livery (for example
/// a third-party add-on whose name contains <c>fnx</c>) simply contributes nothing. A manifest that only names the
/// airframe (A320) without Fenix is another developer's aircraft and is never accepted.
/// </remarks>
internal static class FenixPackageLocator
{
    private static readonly string[] Markers = ["fenix", "fnx"];

    private static readonly HashSet<string> ManifestFields =
        new(["title", "package_title", "creator", "manufacturer", "content_type", "contentType"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FenixPackage> Find(IEnumerable<string> packageRoots, ICollection<string> errors)
    {
        var packages = new List<FenixPackage>();
        foreach (var root in packageRoots)
        {
            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Package root '{root}' could not be read: {ex.Message}");
                continue;
            }

            packages.AddRange(folders.Where(IsFenixPackage).Select(f => new FenixPackage(f, Path.GetFileName(f))));
        }

        return packages;
    }

    private static bool IsFenixPackage(string folder) =>
        MentionsFenix(Path.GetFileName(folder)) || ManifestMentionsFenix(Path.Combine(folder, "manifest.json"));

    private static bool ManifestMentionsFenix(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(p =>
                    ManifestFields.Contains(p.Name)
                    && p.Value.ValueKind == JsonValueKind.String
                    && MentionsFenix(p.Value.GetString()));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return false; // judged by its folder name only
        }
    }

    private static bool MentionsFenix(string? text) =>
        text is not null && Markers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
}
