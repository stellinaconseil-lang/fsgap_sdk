using FSGAP.Abstractions.Aircraft;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect;

/// <summary>
/// Turns the raw identity SimVars into an <see cref="AircraftDescriptor"/>. Pure normalization: values are trimmed,
/// empty values become <see langword="null"/>, and no vendor or aircraft type is inferred.
/// </summary>
internal static class AircraftIdentityMapper
{
    /// <returns>The descriptor, or <see langword="null"/> when no aircraft is loaded (empty <c>TITLE</c>).</returns>
    public static AircraftDescriptor? ToDescriptor(RawAircraftIdentity raw)
    {
        var title = Clean(raw.Title);
        if (title is null)
        {
            return null;
        }

        return new AircraftDescriptor
        {
            Title = title,
            Registration = Clean(raw.AtcId),
            LiveryFolder = Clean(raw.LiveryFolder),
            Livery = Clean(raw.LiveryName),
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
