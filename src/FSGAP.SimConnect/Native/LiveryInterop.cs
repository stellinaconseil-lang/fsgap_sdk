using System.Runtime.InteropServices;
using SimConnect.NET;

namespace FSGAP.SimConnect.Native;

/// <summary>
/// BLOCK 10A.5 — EXPERIMENTAL, discovery only. The MSFS 2024 SimConnect functions that SimConnect.NET 0.2.2 does not
/// bind: livery enumeration and AI aircraft creation with a livery and a tail number. Not used by any production path.
/// </summary>
/// <remarks>
/// <para>
/// Every call runs on the transport's own client through <see cref="FacilityInterop.InvokeNativeAsync"/>, so it uses the
/// single native connection and is serialized with the library's message loop. These are plain P/Invoke declarations
/// of documented exports of <c>SimConnect.dll</c> (the same native library SimConnect.NET loads), not reflection.
/// </para>
/// <para>
/// No method here is <c>async</c>: the client is only passed through, never held, so the architecture rule "only the
/// native seam holds a client" is unchanged.
/// </para>
/// </remarks>
internal static class LiveryInterop
{
    /// <summary><c>SIMCONNECT_SIMOBJECT_TYPE_AIRCRAFT</c> (SimConnect.NET <c>SimConnectSimObjectType.Aircraft</c>).</summary>
    internal const uint AircraftObjectType = 2;

    /// <summary>Asks for every (aircraft title, livery name) pair of the given SimObject type.</summary>
    internal static Task<int> EnumerateAsync(SimConnectClient client, uint requestId, uint simObjectType, CancellationToken cancellationToken) =>
        FacilityInterop.InvokeNativeAsync(client, handle => EnumerateSimObjectsAndLiveries(handle, requestId, simObjectType), cancellationToken);

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

    [DllImport("SimConnect.dll", EntryPoint = "SimConnect_EnumerateSimObjectsAndLiveries")]
    private static extern int EnumerateSimObjectsAndLiveries(IntPtr handle, uint requestId, uint simObjectType);

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
