using FSGAP.Abstractions.Aircraft;

namespace FSGAP.Fenix.Catalog;

/// <summary>Result of one scan: the installed aircraft found, per-item errors, and how many folders were skipped.</summary>
internal sealed record ScanOutcome(IReadOnlyList<InstalledAircraft> Aircraft, IReadOnlyList<string> Errors, int Skipped);

/// <summary>
/// Walks the Fenix packages of the given package roots and parses every livery folder. Read-only, cancellable, and
/// robust: an unreadable folder or a broken livery is recorded as an error and the scan continues.
/// </summary>
/// <remarks>
/// <para>
/// A folder is a livery candidate when it directly contains <c>livery.cfg</c> or <c>aircraft.cfg</c>. Fenix's own
/// internal trees, <c>presets</c> and <c>attachments</c>, are pruned. Their <c>aircraft.cfg</c> files carry
/// <c>[FLTSIM]</c> sections with house registrations (G-SMOL, G-FBIG, G-FENX) but are aircraft variants, not
/// selectable liveries. The pruning matches exact folder names only, so a livery folder named
/// <c>presets-collection</c> is kept.
/// </para>
/// <para>
/// Progress: one <c>Discovering</c> report, then <c>Scanning</c> reports throttled to about 20 over the whole scan,
/// always ending with processed = total.
/// </para>
/// </remarks>
internal static class FenixLiveryScanner
{
    private const int MaxDepth = 10;
    private const int ProgressReports = 20;
    private static readonly string[] InternalFolderNames = ["presets", "attachments"];

    public static ScanOutcome Scan(IReadOnlyList<string> packageRoots, IProgress<CatalogScanProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new CatalogScanProgress(CatalogScanPhase.Discovering, 0, null));
        var errors = new List<string>();
        var work = new List<(FenixPackage Package, string Folder)>();
        foreach (var package in FenixPackageLocator.Find(packageRoots, errors))
        {
            cancellationToken.ThrowIfCancellationRequested();
            work.AddRange(FindLiveryFolders(package.PackagePath, errors, cancellationToken).Select(folder => (package, folder)));
        }

        var aircraft = new List<InstalledAircraft>(work.Count);
        var skipped = 0;
        var step = Math.Max(1, work.Count / ProgressReports);
        progress?.Report(new CatalogScanProgress(CatalogScanPhase.Scanning, 0, work.Count));
        for (var i = 0; i < work.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (package, folder) = work[i];
            try
            {
                var result = FenixLiveryParser.Parse(folder, package);
                if (result.Aircraft is { } parsed)
                {
                    aircraft.Add(parsed);
                }
                else
                {
                    skipped++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"{package.PackageName}/{Path.GetRelativePath(package.PackagePath, folder)}: {ex.GetType().Name}: {ex.Message}");
            }

            var processed = i + 1;
            if (processed % step == 0 || processed == work.Count)
            {
                progress?.Report(new CatalogScanProgress(CatalogScanPhase.Scanning, processed, work.Count));
            }
        }

        return new ScanOutcome(aircraft, errors, skipped);
    }

    /// <summary>Whether a folder name is one of Fenix's internal (non-livery) trees; exact, case-insensitive.</summary>
    internal static bool IsInternalFolder(string folderName) =>
        InternalFolderNames.Contains(folderName, StringComparer.OrdinalIgnoreCase);

    private static List<string> FindLiveryFolders(string packagePath, List<string> errors, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        Walk(packagePath, 0);
        return found;

        void Walk(string directory, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > MaxDepth)
            {
                return;
            }

            string[] subdirectories;
            try
            {
                if (File.Exists(Path.Combine(directory, "livery.cfg")) || File.Exists(Path.Combine(directory, "aircraft.cfg")))
                {
                    found.Add(directory);
                }

                subdirectories = Directory.GetDirectories(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Folder '{directory}' could not be read: {ex.Message}");
                return;
            }

            foreach (var subdirectory in subdirectories.Where(d => !IsInternalFolder(Path.GetFileName(d))))
            {
                Walk(subdirectory, depth + 1);
            }
        }
    }
}
