using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

public class ConcurrencyTests
{
    [Fact]
    public async Task A_blocked_consumer_does_not_block_native_callbacks_or_other_consumers()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        using var release = new ManualResetEventSlim();
        var consumerReached = false;
        var consumerRanInsideNativeCallback = true;
        var blocked = Task.Run(async () =>
        {
            await foreach (var state in h.Simulator.State.WatchAsync())
            {
                if (state.CrashCount > 0)
                {
                    consumerRanInsideNativeCallback = FakeSession.InNativeCallback;
                    Volatile.Write(ref consumerReached, true);
                    release.Wait(); // a consumer that never returns promptly
                    return;
                }
            }
        });

        session.RaiseCrash();
        await Eventually.TrueAsync(() => Volatile.Read(ref consumerReached), "consumer blocked");
        for (var i = 0; i < 50; i++)
        {
            session.RaiseCrash(); // must return immediately although a consumer is stuck
        }

        Assert.False(consumerRanInsideNativeCallback);
        var latest = await h.Simulator.State.WatchAsync().FirstAsync().WaitAsync(Eventually.Timeout);
        Assert.Equal(51, latest.CrashCount);
        release.Set();
        await blocked.WaitAsync(Eventually.Timeout);
    }

    [Fact]
    public async Task A_throwing_consumer_does_not_stop_the_transport()
    {
        await using var h = new Harness();
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        var faulty = Task.Run(async () =>
        {
            await foreach (var status in h.Simulator.WatchStatusAsync())
            {
                if (status.State == SimulatorConnectionState.Connected)
                {
                    throw new InvalidOperationException("consumer bug");
                }
            }
        });

        await h.Simulator.StartAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => faulty.WaitAsync(Eventually.Timeout));

        first.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        h.Clock.Advance(h.RetryDelay);
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.Equal(2, h.Factory.Sessions.Count);
    }

    [Fact]
    public async Task Every_observer_ends_up_with_the_latest_state()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        var observers = Enumerable.Range(0, 5).Select(_ => h.Simulator.State.WatchAsync().GetAsyncEnumerator()).ToArray();
        foreach (var observer in observers)
        {
            Assert.True(await observer.MoveNextAsync());
        }

        for (var i = 0; i < 20; i++)
        {
            session.RaiseCrash();
        }

        foreach (var observer in observers)
        {
            Assert.True(await observer.MoveNextAsync().AsTask().WaitAsync(Eventually.Timeout));
            while (observer.Current.CrashCount < 20)
            {
                Assert.True(await observer.MoveNextAsync().AsTask().WaitAsync(Eventually.Timeout));
            }

            Assert.Equal(20, observer.Current.CrashCount);
            await observer.DisposeAsync();
        }
    }
}
