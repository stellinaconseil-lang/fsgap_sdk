namespace FSGAP.SimConnect.Tests.Fakes;

internal static class AsyncEnumerableExtensions
{
    public static async Task<T> FirstAsync<T>(this IAsyncEnumerable<T> source)
    {
        await foreach (var item in source)
        {
            return item;
        }

        throw new InvalidOperationException("The sequence is empty.");
    }
}
