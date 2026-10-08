using System.Text.RegularExpressions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Core.Msfs;
using FSGAP.Synaptic.Identity;

namespace FSGAP.Synaptic.Catalog;

/// <summary>One Synaptic A220-300 livery found on disk, from its own <c>livery.cfg</c>.</summary>
/// <param name="LiveryName">The <c>[General] name</c> — the same name the simulator enumeration reports.</param>
/// <param name="LiveryFolder">The livery's folder name (what the simulator reports as <c>LIVERY FOLDER</c>).</param>
/// <param name="Registration">The registration the livery DECLARES, or <see langword="null"/> (house / white liveries).</param>
/// <param name="Package">The package folder it was found in (diagnostic only).</param>
internal sealed record DiskLivery(string LiveryName, string LiveryFolder, string? Registration, string Package);

/// <summary>What one disk scan found, and why some candidates were not listed.</summary>
internal sealed record DiskLiveryScan(IReadOnlyList<DiskLivery> Liveries, IReadOnlyList<string> Skipped, int PackagesScanned);

/// <summary>
/// Enumerates the installed Synaptic A220-300 liveries from the MSFS 2024 package folders — no simulator, no aircraft
/// loaded, nothing learned beforehand. Strictly read-only.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where.</b> Every package folder under the given roots (the provider passes Community, Community2024, Official2024,
/// Official2020 and StreamedPackages). A livery is a <c>livery.cfg</c> at
/// <c>SimObjects/Airplanes/synaptic_a220/liveries/&lt;vendor&gt;/&lt;livery folder&gt;/livery.cfg</c>, either as a plain file
/// (third-party Community liveries) or inside an UNENCRYPTED package archive (the streamed Marketplace packages
/// <c>fs24-inibuilds-a220-liveries</c> / <c>fs24-inibuilds-aircraft-a220</c> keep their <c>livery.cfg</c> files uncompressed in
/// <c>minimal.fsarchive</c>). Encrypted archives and compressed entries are skipped, never decoded.
/// </para>
/// <para>
/// <b>Which.</b> Only liveries that declare the A220-300 fuselage in <c>[Selection] required_tags</c>
/// (<see cref="SynapticIdentity.FuselageTag"/>) and have a <c>[General] name</c>. Anything else (another model, no name,
/// unreadable) is reported in <see cref="DiskLiveryScan.Skipped"/>.
/// </para>
/// <para>
/// <b>Registration</b> — DECLARED by the livery, never inferred from its folder: <c>[FLTSIM] atc_id</c> /
/// <c>[General] atc_id</c> when present, otherwise the Synaptic panel parameter <c>param.N = "config,&lt;registration&gt;"</c>
/// of <c>[Panel_DynamicParameters]</c>. Accepted only with a known registration shape
/// (<see cref="RegistrationResolver.IsRegistrationShape"/>); reported with source
/// <see cref="RegistrationSource.Authoritative"/> by the catalog. None → <see langword="null"/>.
/// </para>
/// <para>
/// <b>Duplicates.</b> The same livery name found twice (two packages, a plain copy and an archive copy) is one livery:
/// the first in root order, then package name order, then path order — deterministic.
/// </para>
/// </remarks>
internal static partial class SynapticLiveryDiskScanner
{
    public static DiskLiveryScan Scan(IReadOnlyList<string> packageRoots, CancellationToken cancellationToken = default)
    {
        var found = new List<DiskLivery>();
        var skipped = new List<string>();
        var packages = 0;
        foreach (var root in packageRoots)
        {
            foreach (var package in SafeDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                packages++;
                var name = Path.GetFileName(package);

                // Plain livery.cfg files (Community / third-party liveries).
                var liveriesDir = Path.Combine(package, "SimObjects", "Airplanes", "synaptic_a220", "liveries");
                foreach (var cfg in SafeFiles(liveriesDir, "livery.cfg").Order(StringComparer.OrdinalIgnoreCase))
                {
                    var rel = "simobjects\\airplanes\\synaptic_a220\\liveries\\" + Path.GetRelativePath(liveriesDir, cfg).Replace('/', '\\');
                    Add(rel, SafeReadText(cfg), name, found, skipped);
                }

                // livery.cfg entries of unencrypted package archives (streamed Marketplace packages).
                foreach (var archive in SafeFiles(package, "*.fsarchive").Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (FsArchive.TryReadIndex(archive) is not { } entries)
                    {
                        continue; // encrypted or not an archive this reader knows: nothing claimed
                    }

                    foreach (var entry in entries.Where(e => LiveryCfgPath().IsMatch(e.Path)).OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
                    {
                        if (!entry.IsStoredRaw)
                        {
                            skipped.Add($"{name}: {entry.Path} is compressed in its archive");
                            continue;
                        }

                        Add(entry.Path, FsArchive.TryReadText(archive, entry), name, found, skipped);
                    }
                }
            }
        }

        var unique = found
            .GroupBy(l => l.LiveryName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
        foreach (var dup in found.Except(unique))
        {
            skipped.Add($"{dup.Package}: duplicate of livery '{dup.LiveryName}' (first copy kept)");
        }

        return new DiskLiveryScan(unique, skipped, packages);
    }

    /// <summary>Parses one livery.cfg found at <paramref name="relativePath"/> (inside the package) into a livery, or explains why not.</summary>
    internal static DiskLivery? Parse(string relativePath, string? text, string package, out string? skipReason)
    {
        skipReason = null;
        var match = LiveryCfgPath().Match(relativePath);
        if (!match.Success)
        {
            skipReason = $"{package}: {relativePath} is not a Synaptic A220 livery path";
            return null;
        }

        if (text is null)
        {
            skipReason = $"{package}: {relativePath} could not be read";
            return null;
        }

        var cfg = IniConfigFile.Parse(text.Split('\n'));
        var tags = cfg.Get("Selection", "required_tags") ?? string.Empty;
        if (!tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(SynapticIdentity.FuselageTag, StringComparer.OrdinalIgnoreCase))
        {
            skipReason = $"{package}: {relativePath} does not declare the A220-300 fuselage ({SynapticIdentity.FuselageTag})";
            return null;
        }

        var name = cfg.Get("General", "name")?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            skipReason = $"{package}: {relativePath} has no [General] name";
            return null;
        }

        return new DiskLivery(name, match.Groups["folder"].Value.Trim(), DeclaredRegistration(cfg), package);
    }

    /// <summary>atc_id first, then the Synaptic panel parameter "config,&lt;registration&gt;"; only a registration shape counts.</summary>
    internal static string? DeclaredRegistration(IniConfigFile cfg)
    {
        foreach (var section in cfg.SectionsStartingWith("FLTSIM").Prepend("General"))
        {
            if (cfg.Get(section, "atc_id") is { } atcId && RegistrationResolver.IsRegistrationShape(atcId))
            {
                return RegistrationResolver.Normalize(atcId);
            }
        }

        for (var i = 0; i < 32; i++)
        {
            if (cfg.Get("Panel_DynamicParameters", $"param.{i}") is { } value
                && PanelConfig().Match(value) is { Success: true } m
                && RegistrationResolver.IsRegistrationShape(m.Groups["registration"].Value))
            {
                return RegistrationResolver.Normalize(m.Groups["registration"].Value);
            }
        }

        return null;
    }

    private static void Add(string relativePath, string? text, string package, List<DiskLivery> found, List<string> skipped)
    {
        if (Parse(relativePath, text, package, out var reason) is { } livery)
        {
            found.Add(livery);
        }
        else if (reason is not null)
        {
            skipped.Add(reason);
        }
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeFiles(string path, string pattern)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.GetFiles(path, pattern, new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive })
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? SafeReadText(string path)
    {
        try
        {
            return File.ReadAllText(path).TrimStart('﻿');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary><c>simobjects\airplanes\synaptic_a220\liveries\&lt;vendor&gt;\&lt;folder&gt;\livery.cfg</c>, case-insensitive.</summary>
    [GeneratedRegex(@"^simobjects[\\/]airplanes[\\/]synaptic_a220[\\/]liveries[\\/][^\\/]+[\\/](?<folder>[^\\/]+)[\\/]livery\.cfg$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LiveryCfgPath();

    /// <summary>The Synaptic panel parameter carrying the livery's registration: <c>config,&lt;registration&gt;</c>.</summary>
    [GeneratedRegex(@"^\s*config\s*,\s*(?<registration>[A-Za-z0-9-]+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex PanelConfig();
}
