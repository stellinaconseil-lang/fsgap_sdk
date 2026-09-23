using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace FSGAP.SimConnect.Tests;

public class ResourceAndRetryTests
{
    [Fact]
    public async Task A_failed_open_disposes_the_client()
    {
        var client = new TrackedDisposable();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SafeOpen.OpenAsync(() => client, _ => throw new InvalidOperationException("MSFS not running")));

        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task A_cancelled_open_disposes_the_client()
    {
        var client = new TrackedDisposable();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SafeOpen.OpenAsync(() => client, _ => Task.FromCanceled(new CancellationToken(true))));

        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task A_successful_open_keeps_the_client()
    {
        var client = new TrackedDisposable();

        var opened = await SafeOpen.OpenAsync(() => client, _ => Task.CompletedTask);

        Assert.Same(client, opened);
        Assert.Equal(0, client.DisposeCount);
    }

    [Fact]
    public async Task The_old_connection_is_disposed_before_a_new_one_is_opened()
    {
        await using var h = new Harness();
        var sessions = Enumerable.Range(0, 4).Select(_ => h.Factory.SimulatorPresent(Identity.Of("Test Airliner"))).ToArray();
        await h.Simulator.StartAsync();

        foreach (var session in sessions.Take(3))
        {
            await Eventually.TrueAsync(() => h.Factory.Sessions.Contains(session), "session opened");
            await h.WaitForAsync(SimulatorConnectionState.Connected);
            session.Drop();
            await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
            h.Clock.Advance(h.RetryDelay);
        }

        await Eventually.TrueAsync(() => h.Factory.Sessions.Contains(sessions[3]), "last session opened");
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.Equal(1, h.Factory.MaxLiveSessions);
        Assert.All(sessions.Take(3), s => Assert.Equal(1, s.DisposeCount));
        Assert.All(sessions.Take(3), s => Assert.False(s.HasHandlers));

        await h.Simulator.StopAsync();
        Assert.All(sessions, s => Assert.Equal(1, s.DisposeCount));
    }

    [Fact]
    public async Task Retry_respects_the_configured_delay()
    {
        await using var h = new Harness(retryDelay: TimeSpan.FromSeconds(7));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);

        h.Clock.Advance(TimeSpan.FromSeconds(7) - TimeSpan.FromMilliseconds(1));
        await Eventually.SettleAsync();
        Assert.Equal(1, h.Factory.Attempts);

        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually.TrueAsync(() => h.Factory.Attempts == 2, "second attempt");

        await h.ElapseRetryAsync(timersBefore: 1);
        await Eventually.TrueAsync(() => h.Factory.Attempts == 3, "third attempt");
    }

    [Fact]
    public async Task Waiting_for_the_simulator_does_not_flood_the_log()
    {
        await using var h = new Harness();
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.WaitingForSimulator);

        for (var timers = 1; timers <= 100; timers++)
        {
            await h.ElapseRetryAsync(timersBefore: timers - 1);
        }

        await Eventually.TrueAsync(() => h.Factory.Attempts >= 100, "100 attempts");
        var informative = h.Logger.Entries.Where(e => e.Level >= LogLevel.Information).ToArray();
        Assert.True(informative.Length <= 2, string.Join(Environment.NewLine, informative.Select(e => e.Message)));
    }

    private sealed class TrackedDisposable : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
