using FSGAP.Core.Observation;

namespace FSGAP.Core.Tests;

public class ObservableStateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Watcher_receives_the_current_value_then_changes()
    {
        var state = new ObservableState<int>(1);
        await using var watcher = state.WatchAsync().GetAsyncEnumerator();

        Assert.Equal(1, await NextAsync(watcher));
        Assert.True(state.Set(2));
        Assert.Equal(2, await NextAsync(watcher));
        Assert.Equal(2, state.Current);
    }

    [Fact]
    public async Task Setting_an_equal_value_notifies_nobody()
    {
        var state = new ObservableState<string>("a");
        await using var watcher = state.WatchAsync().GetAsyncEnumerator();
        Assert.Equal("a", await NextAsync(watcher));

        Assert.False(state.Set("a"));
        var pending = watcher.MoveNextAsync().AsTask();

        Assert.False(pending.IsCompleted);
        state.Set("b");
        Assert.True(await pending.WaitAsync(Timeout));
        Assert.Equal("b", watcher.Current);
    }

    [Fact]
    public async Task Every_watcher_sees_each_change()
    {
        var state = new ObservableState<int>(0);
        await using var first = state.WatchAsync().GetAsyncEnumerator();
        await using var second = state.WatchAsync().GetAsyncEnumerator();
        await NextAsync(first);
        await NextAsync(second);

        state.Set(7);

        Assert.Equal(7, await NextAsync(first));
        Assert.Equal(7, await NextAsync(second));
    }

    [Fact]
    public async Task Slow_watcher_skips_intermediate_values_but_gets_the_latest()
    {
        var state = new ObservableState<int>(0);
        await using var watcher = state.WatchAsync().GetAsyncEnumerator();
        await NextAsync(watcher);

        for (var i = 1; i <= 100; i++)
        {
            state.Set(i);
        }

        Assert.Equal(100, await NextAsync(watcher));
    }

    [Fact]
    public async Task Complete_ends_current_and_future_watchers()
    {
        var state = new ObservableState<int>(3);
        await using var watcher = state.WatchAsync().GetAsyncEnumerator();
        await NextAsync(watcher);

        state.Complete();

        Assert.False(await watcher.MoveNextAsync().AsTask().WaitAsync(Timeout));
        var late = await state.WatchAsync().ToListAsync().WaitAsync(Timeout);
        Assert.Equal([3], late);
        Assert.Throws<InvalidOperationException>(() => state.Set(4));
    }

    [Fact]
    public async Task Cancellation_ends_the_watch()
    {
        var state = new ObservableState<int>(0);
        using var cts = new CancellationTokenSource();
        await using var watcher = state.WatchAsync(cts.Token).GetAsyncEnumerator();
        await NextAsync(watcher);

        var pending = watcher.MoveNextAsync().AsTask();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Timeout));
    }

    [Fact]
    public void Custom_comparer_decides_what_a_change_is()
    {
        var state = new ObservableState<string>("abc", StringComparer.OrdinalIgnoreCase);

        Assert.False(state.Set("ABC"));
        Assert.Equal("abc", state.Current);
        Assert.True(state.Set("abd"));
    }

    private static async Task<T> NextAsync<T>(IAsyncEnumerator<T> enumerator)
    {
        Assert.True(await enumerator.MoveNextAsync().AsTask().WaitAsync(Timeout));
        return enumerator.Current;
    }
}
