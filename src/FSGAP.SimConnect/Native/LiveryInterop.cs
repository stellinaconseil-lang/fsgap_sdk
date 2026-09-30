using System.Runtime.InteropServices;
using SimConnect.NET;

namespace FSGAP.SimConnect.Native;

/// <summary>
/// <c>SimConnect_EnumerateSimObjectsAndLiveries</c>, which SimConnect.NET 0.2.2 does not bind (it only defines the
/// receive id). A plain P/Invoke of the documented export of <c>SimConnect.dll</c> (the native library SimConnect.NET
/// loads), called with the transport's own handle.
/// </summary>
/// <remarks>
/// The call runs on the library's dispatcher through <see cref="FacilityInterop.InvokeNativeAsync"/>, serialized with its
/// message loop, exactly like the airport list: one native connection, no thread of our own touching the handle. No
/// method here is <c>async</c>: the client is passed through, never held.
/// </remarks>
internal static class LiveryInterop
{
    /// <summary><c>SIMCONNECT_SIMOBJECT_TYPE_AIRCRAFT</c> (SimConnect.NET <c>SimConnectSimObjectType.Aircraft</c>).</summary>
    internal const uint AircraftObjectType = 2;

    /// <summary>Asks for every (aircraft title, livery name) pair of the given SimObject type; returns the HRESULT.</summary>
    internal static Task<int> EnumerateAsync(SimConnectClient client, uint requestId, uint simObjectType, CancellationToken cancellationToken) =>
        FacilityInterop.InvokeNativeAsync(client, handle => EnumerateSimObjectsAndLiveries(handle, requestId, simObjectType), cancellationToken);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_EnumerateSimObjectsAndLiveries")]
    private static extern int EnumerateSimObjectsAndLiveries(IntPtr handle, uint requestId, uint simObjectType);
}

/// <summary>
/// BLOCK 10A.5 — EXPERIMENTAL, diagnostic only: AI aircraft creation with a livery and a tail number, and removal. Used by
/// the live sample's discovery harness through internal entry points; never part of the public API and not a production
/// technique (BLOCK 10A.5 conclusion).
/// </summary>
internal static class AiProbeInterop
{
    /// <summary>Creates a non-ATC AI aircraft from a container title, a livery and a tail number.</summary>
    internal static Task<int> CreateNonAtcAircraftAsync(
        SimConnectClient client,
        string containerTitle,
        string livery,
        string tailNumber,
        SimConnectDataInitPosition position,
        uint requestId,
        CancellationToken cancellationToken) =>
        FacilityInterop.InvokeNativeAsync(
            client,
            handle => AICreateNonATCAircraftEx1(handle, containerTitle, livery, tailNumber, position, requestId),
            cancellationToken);

    /// <summary>Removes an AI object created by this client.</summary>
    internal static Task<int> RemoveObjectAsync(SimConnectClient client, uint objectId, uint requestId, CancellationToken cancellationToken) =>
        FacilityInterop.InvokeNativeAsync(client, handle => AIRemoveObject(handle, objectId, requestId), cancellationToken);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_AICreateNonATCAircraft_EX1", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern int AICreateNonATCAircraftEx1(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string containerTitle,
        [MarshalAs(UnmanagedType.LPStr)] string livery,
        [MarshalAs(UnmanagedType.LPStr)] string tailNumber,
        SimConnectDataInitPosition position,
        uint requestId);

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_AIRemoveObject")]
    private static extern int AIRemoveObject(IntPtr handle, uint objectId, uint requestId);
}
