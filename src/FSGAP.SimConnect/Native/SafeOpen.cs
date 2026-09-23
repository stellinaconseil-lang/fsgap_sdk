namespace FSGAP.SimConnect.Native;

/// <summary>Opens a disposable resource and guarantees it is disposed if opening fails.</summary>
internal static class SafeOpen
{
    /// <summary>
    /// Creates a resource, runs <paramref name="open"/> on it and returns it. If creation succeeds but opening
    /// throws or is cancelled, the resource is disposed before the exception propagates, so a failed attempt never
    /// leaves a zombie client behind.
    /// </summary>
    public static async Task<T> OpenAsync<T>(Func<T> create, Func<T, Task> open)
        where T : IAsyncDisposable
    {
        var resource = create();
        try
        {
            await open(resource).ConfigureAwait(false);
            return resource;
        }
        catch
        {
            await resource.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
