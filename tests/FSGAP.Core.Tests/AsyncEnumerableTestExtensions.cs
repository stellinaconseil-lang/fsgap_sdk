namespace FSGAP.Core.Tests;

/// <summary>Minimal async LINQ for tests (the BCL has none, and System.Linq.Async is not worth a dependency here).</summary>
internal static class AsyncEnumerableTestExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var items = new List<T>();
        await foreach (var item in source)
        {
            items.Add(item);
        }

        return items;
    }

    public static async Task<T> FirstAsync<T>(this IAsyncEnumerable<T> source)
    {
        await foreach (var item in source)
        {
            return item;
        }

        throw new InvalidOperationException("The sequence is empty.");
    }
}
