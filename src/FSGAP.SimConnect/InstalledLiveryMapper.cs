using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect;

/// <summary>
/// Turns the raw livery enumeration into <see cref="InstalledLivery"/> rows. Pure and faithful: order and duplicates are
/// kept, nothing is inferred.
/// </summary>
internal static class InstalledLiveryMapper
{
    /// <summary>
    /// Rows without an aircraft title are dropped (they name no aircraft); an empty livery name becomes
    /// <see langword="null"/> (the simulator's unnamed default livery). Values are already trimmed by the parser.
    /// </summary>
    internal static IReadOnlyList<InstalledLivery> ToInstalledLiveries(IReadOnlyList<RawLiveryEntry> raw)
    {
        var rows = new List<InstalledLivery>(raw.Count);
        foreach (var entry in raw)
        {
            if (string.IsNullOrWhiteSpace(entry.AircraftTitle))
            {
                continue;
            }

            rows.Add(new InstalledLivery
            {
                AircraftTitle = entry.AircraftTitle.Trim(),
                LiveryName = string.IsNullOrWhiteSpace(entry.LiveryName) ? null : entry.LiveryName.Trim(),
            });
        }

        return rows;
    }
}
