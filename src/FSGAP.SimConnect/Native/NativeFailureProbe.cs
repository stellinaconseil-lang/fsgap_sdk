using System.Runtime.InteropServices;
using SimConnect.NET;

namespace FSGAP.SimConnect.Native;

// BLOCK 10C.1/10C.2 — RESEARCH ONLY, diagnostic. Transmits one of nine documented MSFS 2024 failure key events (Key
// Events > Aircraft Misc Events > Aircraft Failures) to the user aircraft, on the transport's own connection, so the live
// sample can observe whether the Synaptic A220 reacts. Internal; no production path uses it, nothing here is public,
// and it is not a FailureProvider: no FailureKey, no Trigger/Clear contract, FailureCapabilities stay None.

/// <summary>The only events the diagnostic transport accepts, each with a fixed client event id.</summary>
internal static class NativeFailureProbeEvents
{
    /// <summary>Documented: "Toggles left brake failure".</summary>
    internal const string ToggleLeftBrakeFailure = "TOGGLE_LEFT_BRAKE_FAILURE";

    /// <summary>Documented: "Toggles right brake failure".</summary>
    internal const string ToggleRightBrakeFailure = "TOGGLE_RIGHT_BRAKE_FAILURE";

    /// <summary>Documented: "Toggles brake failure (both)".</summary>
    internal const string ToggleTotalBrakeFailure = "TOGGLE_TOTAL_BRAKE_FAILURE";

    /// <summary>Documented: "Toggle engine 1/2/3/4 failure" (engine 1).</summary>
    internal const string ToggleEngine1Failure = "TOGGLE_ENGINE1_FAILURE";

    /// <summary>Documented: "Toggle engine 1/2/3/4 failure" (engine 2).</summary>
    internal const string ToggleEngine2Failure = "TOGGLE_ENGINE2_FAILURE";

    /// <summary>Documented: "Toggles hydraulic system failure".</summary>
    internal const string ToggleHydraulicFailure = "TOGGLE_HYDRAULIC_FAILURE";

    /// <summary>Documented: "Toggle electrical system failure".</summary>
    internal const string ToggleElectricalFailure = "TOGGLE_ELECTRICAL_FAILURE";

    /// <summary>Documented: "Toggles blocked pitot tube".</summary>
    internal const string TogglePitotBlockage = "TOGGLE_PITOT_BLOCKAGE";

    /// <summary>Documented: "Toggles blocked static port".</summary>
    internal const string ToggleStaticPortBlockage = "TOGGLE_STATIC_PORT_BLOCKAGE";

    /// <summary>
    /// Client event ids, well away from the system-event subscription ids of the session (1 and 2), which share the
    /// connection's event id space.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, uint> ClientEventIds = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        [ToggleLeftBrakeFailure] = 0x10C1_0001,
        [ToggleRightBrakeFailure] = 0x10C1_0002,
        [ToggleTotalBrakeFailure] = 0x10C1_0003,
        [ToggleEngine1Failure] = 0x10C1_0004,
        [ToggleEngine2Failure] = 0x10C1_0005,
        [ToggleHydraulicFailure] = 0x10C1_0006,
        [ToggleElectricalFailure] = 0x10C1_0007,
        [TogglePitotBlockage] = 0x10C1_0008,
        [ToggleStaticPortBlockage] = 0x10C1_0009,
    };

    /// <summary>Returns the client event id of an allowed event.</summary>
    /// <exception cref="ArgumentException">The event is not one of the nine allowed documented failure toggles.</exception>
    internal static uint IdOf(string eventName) =>
        ClientEventIds.TryGetValue(eventName, out var id)
            ? id
            : throw new ArgumentException($"'{eventName}' is not an allowed diagnostic event.", nameof(eventName));
}

/// <summary>What happened when one diagnostic event was sent.</summary>
internal sealed record NativeFailureEventResult
{
    public required string EventName { get; init; }

    public required uint ClientEventId { get; init; }

    /// <summary>HRESULT of <c>SimConnect_MapClientEventToSimEvent</c>; <see langword="null"/> when already mapped on this connection.</summary>
    public int? MapHResult { get; init; }

    public int TransmitHResult { get; init; }

    /// <summary>SimConnect exception packets received in the observation window after the transmission.</summary>
    public IReadOnlyList<string> Exceptions { get; init; } = [];

    public DateTimeOffset SentAt { get; init; }

    /// <summary>The call returned S_OK and no exception packet arrived: the simulator accepted the event (says nothing about its effect).</summary>
    public bool Accepted => (MapHResult ?? 0) >= 0 && TransmitHResult >= 0 && Exceptions.Count == 0;
}

/// <summary>
/// <c>SimConnect_MapClientEventToSimEvent</c> and <c>SimConnect_TransmitClientEvent</c>: SimConnect.NET 0.2.2 exposes a
/// transmit but no binding for the mapping, so both are plain P/Invokes of the documented exports, called with the
/// transport's own handle on the library's dispatcher (<see cref="FacilityInterop.InvokeNativeAsync"/>), like
/// <see cref="LiveryInterop"/>. One native connection.
/// </summary>
internal static class ClientEventInterop
{
    /// <summary><c>SIMCONNECT_OBJECT_ID_USER</c>.</summary>
    internal const uint UserObjectId = 0;

    /// <summary><c>SIMCONNECT_GROUP_PRIORITY_HIGHEST</c>.</summary>
    internal const uint GroupPriorityHighest = 1;

    /// <summary><c>SIMCONNECT_EVENT_FLAG_GROUPID_IS_PRIORITY</c>.</summary>
    internal const uint GroupIdIsPriority = 0x00000010;

    internal static Task<int> MapAsync(SimConnectClient client, uint clientEventId, string eventName, CancellationToken cancellationToken) =>
        FacilityInterop.InvokeNativeAsync(client, handle => MapClientEventToSimEvent(handle, clientEventId, eventName), cancellationToken);

    internal static Task<int> TransmitToUserAsync(SimConnectClient client, uint clientEventId, CancellationToken cancellationToken) =>
        FacilityInterop.InvokeNativeAsync(
            client,
            handle => TransmitClientEvent(handle, UserObjectId, clientEventId, 0, GroupPriorityHighest, GroupIdIsPriority),
            cancellationToken);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_MapClientEventToSimEvent", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern int MapClientEventToSimEvent(IntPtr handle, uint eventId, [MarshalAs(UnmanagedType.LPStr)] string eventName);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_TransmitClientEvent")]
    private static extern int TransmitClientEvent(IntPtr handle, uint objectId, uint eventId, uint data, uint groupId, uint flags);
}

/// <summary>
/// BLOCK 10C.3 — RESEARCH ONLY: the transport-side guard of diagnostic writes. Only local (L:) variables of the user
/// aircraft, with a unit and a finite value; never a simulation variable, never an event. The whitelist lives in the caller.
/// </summary>
internal static class DiagnosticLocalWrite
{
    /// <exception cref="ArgumentException">Not an L: variable, no unit, or a non-finite value.</exception>
    internal static void Validate(string name, string unit, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(unit);
        if (!name.StartsWith("L:", StringComparison.Ordinal) || name.Length < 3)
        {
            throw new ArgumentException($"'{name}' is not a local (L:) variable.", nameof(name));
        }

        if (!double.IsFinite(value))
        {
            throw new ArgumentException("The value must be finite.", nameof(value));
        }
    }
}
