using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;

namespace FSGAP;

/// <summary>Why the runtime has, or has not, a session for the loaded aircraft.</summary>
public enum FsgapSessionStatus
{
    /// <summary>No aircraft is loaded, or the simulator is not connected.</summary>
    NoAircraft = 0,

    /// <summary>A built-in provider supports the loaded aircraft and a session is open.</summary>
    Attached,

    /// <summary>No built-in provider supports the loaded aircraft; no session.</summary>
    NotSupported,

    /// <summary>Several built-in providers claim the aircraft equally; none is picked silently, no session.</summary>
    Ambiguous,

    /// <summary>A provider supports the aircraft but opening the session failed; no session.</summary>
    AttachFailed,
}

/// <summary>The runtime's view of the loaded aircraft and its session.</summary>
/// <param name="Status">Whether a session is open, and why not.</param>
/// <param name="Aircraft">The loaded aircraft as reported by the simulator, or <see langword="null"/>.</param>
/// <param name="Session">The open session when <paramref name="Status"/> is <see cref="FsgapSessionStatus.Attached"/>.</param>
/// <param name="Detail">Human-readable detail (for example why attaching failed), for logs.</param>
public sealed record FsgapSessionState(FsgapSessionStatus Status, AircraftDescriptor? Aircraft, IAircraftSession? Session, string? Detail = null)
{
    /// <summary>No aircraft, no session.</summary>
    public static FsgapSessionState None { get; } = new(FsgapSessionStatus.NoAircraft, null, null);
}
