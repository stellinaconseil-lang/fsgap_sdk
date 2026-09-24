using System.Reflection;
using SimConnect.NET;

namespace FSGAP.SimConnect.Native;

/// <summary>
/// The only reflection into SimConnect.NET internals in FSGAP (ADR 0004): the two members needed to request the
/// airport list on the transport's own connection. Pinned to SimConnect.NET <b>0.2.2</b>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why reflection.</b> SimConnect.NET 0.2.2 declares the facility structures publicly but exposes no method that
/// requests a facility list. What exists is internal:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>SimConnectNative.SimConnect_RequestFacilitiesList_EX1(IntPtr, uint, uint) : int</c>, the library's own P/Invoke
/// of the native export (reused rather than redeclared, so there is one binding to <c>SimConnect.dll</c>);
/// </description></item>
/// <item><description>
/// <c>SimConnectClient.InvokeNativeAsync&lt;T&gt;(Func&lt;IntPtr, T&gt;, CancellationToken)</c>, which runs a native call on
/// the library's dispatcher, serialized with its message loop.
/// </description></item>
/// </list>
/// <para>
/// The second one is what allows a single connection. FSHANGAR called the native function directly on its main
/// client's handle, from its own thread, raced the library's message loop, and lost every other request (E_FAIL) for
/// the rest of the session; it then moved the lookup to a separate short-lived connection. Going through the
/// dispatcher removes the race without a second connection.
/// </para>
/// <para>
/// <b>Failure mode.</b> Members are resolved once and their signatures checked. When anything is missing or different
/// (another library version), <see cref="Compatibility"/> explains what, and every request fails with that message
/// instead of a <see cref="NullReferenceException"/>. A test pins the expected members.
/// </para>
/// </remarks>
internal static class FacilityInterop
{
    /// <summary>The SimConnect.NET version these members were verified against.</summary>
    internal const string PinnedLibraryVersion = "0.2.2";

    /// <summary><c>SIMCONNECT_FACILITY_LIST_TYPE_AIRPORT</c>.</summary>
    internal const uint AirportListType = 0;

    private const string NativeTypeName = "SimConnect.NET.SimConnectNative";
    private const string RequestListMethodName = "SimConnect_RequestFacilitiesList_EX1";
    private const string InvokeNativeMethodName = "InvokeNativeAsync";

    private static readonly Lazy<Resolution> Resolved = new(() => Resolve(typeof(SimConnectClient).Assembly, typeof(SimConnectClient)));

    /// <summary><see langword="null"/> when the library matches; otherwise why the airport request cannot work.</summary>
    internal static string? Compatibility => Resolved.Value.Error;

    /// <summary>Sends the airport-list request through the client's dispatcher and returns the native HRESULT.</summary>
    /// <exception cref="InvalidOperationException">The library does not match (see <see cref="Compatibility"/>).</exception>
    internal static Task<int> RequestAirportListAsync(SimConnectClient client, uint requestId, CancellationToken cancellationToken)
    {
        var resolution = Resolved.Value;
        if (resolution.Error is { } error)
        {
            throw new InvalidOperationException(error);
        }

        Func<IntPtr, int> call = handle => resolution.RequestList!(handle, AirportListType, requestId);
        return (Task<int>)resolution.InvokeNative!.Invoke(client, [call, cancellationToken])!;
    }

    /// <summary>Resolves and checks the members in <paramref name="library"/> (a parameter so tests can feed another assembly).</summary>
    internal static Resolution Resolve(Assembly library, Type clientType)
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic;
        var version = library.GetName().Version?.ToString(3) ?? "unknown";
        string Incompatible(string what) =>
            $"SimConnect.NET {version} is not compatible with the FSGAP airport service (verified with {PinnedLibraryVersion}): {what}.";

        var native = library.GetType(NativeTypeName);
        if (native is null)
        {
            return new Resolution(null, null, Incompatible($"type {NativeTypeName} not found"));
        }

        var requestList = native.GetMethod(RequestListMethodName, Any | BindingFlags.Static, [typeof(IntPtr), typeof(uint), typeof(uint)]);
        if (requestList is null || requestList.ReturnType != typeof(int))
        {
            return new Resolution(null, null, Incompatible($"{RequestListMethodName}(IntPtr, uint, uint) : int not found"));
        }

        var invokeNative = clientType.GetMethods(Any | BindingFlags.Instance)
            .SingleOrDefault(m => m.Name == InvokeNativeMethodName && m.IsGenericMethodDefinition && m.GetParameters() is [var p0, var p1]
                && p0.ParameterType.IsGenericType && p0.ParameterType.GetGenericTypeDefinition() == typeof(Func<,>)
                && p0.ParameterType.GetGenericArguments()[0] == typeof(IntPtr)
                && p1.ParameterType == typeof(CancellationToken));
        if (invokeNative is null)
        {
            return new Resolution(null, null, Incompatible($"{InvokeNativeMethodName}<T>(Func<IntPtr, T>, CancellationToken) not found"));
        }

        var closed = invokeNative.MakeGenericMethod(typeof(int));
        if (closed.ReturnType != typeof(Task<int>))
        {
            return new Resolution(null, null, Incompatible($"{InvokeNativeMethodName} does not return Task<T>"));
        }

        return new Resolution(requestList.CreateDelegate<Func<IntPtr, uint, uint, int>>(), closed, null);
    }

    /// <summary>The resolved members, or why they could not be resolved.</summary>
    internal sealed record Resolution(Func<IntPtr, uint, uint, int>? RequestList, MethodInfo? InvokeNative, string? Error);
}
