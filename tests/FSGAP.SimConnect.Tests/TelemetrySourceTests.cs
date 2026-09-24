using FSGAP.Abstractions.Telemetry;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Telemetry;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>Aggregation, freshness and streaming of the telemetry snapshot, driven directly.</summary>
public class TelemetrySourceTests
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Before_any_read_every_section_is_unavailable()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);

        var snapshot = await source.GetSnapshotAsync();

        Assert.Equal(ValueState.Unavailable, snapshot.Flight.IndicatedAirspeedKnots.State);
        Assert.Empty(snapshot.Engines);
        Assert.Empty(snapshot.LandingGear.Units);
        Assert.Equal(ValueState.Unavailable, snapshot.Warnings.Stall.State);
    }

    [Fact]
    public async Task Each_group_replaces_its_own_sections_and_keeps_the_others()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);

        source.ApplySlow(new SlowGroupVars { Engine1N1Percent = 80.0 }, clock.GetUtcNow(), source.Generation);
        clock.Advance(TimeSpan.FromSeconds(1));
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 250.0 }, clock.GetUtcNow(), source.Generation);
        source.ApplyNormal(new NormalGroupVars { FlapsHandlePercent = 0.0 }, clock.GetUtcNow(), source.Generation);

        var snapshot = await source.GetSnapshotAsync();
        Assert.Equal(80.0, snapshot.Engines[0].N1Percent.Value);
        Assert.Equal(TestClock.Start, snapshot.Engines[0].N1Percent.ObservedAt);
        Assert.Equal(250.0, snapshot.Flight.IndicatedAirspeedKnots.Value);
        Assert.Equal(TestClock.Start.AddSeconds(1), snapshot.Flight.IndicatedAirspeedKnots.ObservedAt);
        Assert.True(snapshot.FlightControls.FlapsHandlePercent.IsKnown);
    }

    [Fact]
    public async Task A_published_snapshot_is_never_modified_afterwards()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 100.0 }, clock.GetUtcNow(), source.Generation);
        var first = await source.GetSnapshotAsync();

        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 200.0 }, clock.GetUtcNow(), source.Generation);

        Assert.Equal(100.0, first.Flight.IndicatedAirspeedKnots.Value);
        Assert.Equal(200.0, (await source.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.Value);
    }

    [Fact]
    public async Task A_value_is_fresh_at_exactly_stale_after_and_unknown_just_past_it()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 250.0 }, clock.GetUtcNow(), source.Generation);

        clock.Advance(StaleAfter);
        Assert.True((await source.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.IsKnown);

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal(ValueState.Unknown, (await source.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.State);
    }

    [Fact]
    public async Task A_stalled_group_expires_while_the_others_keep_arriving()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        source.ApplySlow(new SlowGroupVars { Engine1N1Percent = 80.0 }, clock.GetUtcNow(), source.Generation);

        for (var i = 0; i < 16; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 250.0 }, clock.GetUtcNow(), source.Generation);
        }

        var snapshot = await source.GetSnapshotAsync();
        Assert.Equal(ValueState.Unknown, snapshot.Engines[0].N1Percent.State);
        Assert.True(snapshot.Flight.IndicatedAirspeedKnots.IsKnown);
    }

    [Fact]
    public async Task Reset_drops_everything_and_ignores_reads_issued_before_it()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 250.0 }, clock.GetUtcNow(), source.Generation);
        var before = source.Generation;

        source.Reset();
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 999.0 }, clock.GetUtcNow(), before);

        var snapshot = await source.GetSnapshotAsync();
        Assert.Equal(ValueState.Unavailable, snapshot.Flight.IndicatedAirspeedKnots.State);

        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 120.0 }, clock.GetUtcNow(), source.Generation);
        Assert.Equal(120.0, (await source.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.Value);
    }

    [Fact]
    public async Task Expire_now_publishes_the_aged_snapshot_to_streams()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 250.0 }, clock.GetUtcNow(), source.Generation);
        using var cts = new CancellationTokenSource(Eventually.Timeout);
        await using var stream = source.StreamAsync(cancellationToken: cts.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        Assert.True(stream.Current.Flight.IndicatedAirspeedKnots.IsKnown);

        clock.Advance(StaleAfter + TimeSpan.FromSeconds(1));
        source.ExpireNow();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(ValueState.Unknown, stream.Current.Flight.IndicatedAirspeedKnots.State);
    }

    [Fact]
    public async Task A_stream_starts_with_the_current_snapshot()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 250.0 }, clock.GetUtcNow(), source.Generation);

        var first = await source.StreamAsync().FirstAsync().WaitAsync(Eventually.Timeout);

        Assert.Equal(250.0, first.Flight.IndicatedAirspeedKnots.Value);
    }

    [Fact]
    public async Task A_stream_delivers_at_most_one_snapshot_per_interval_and_it_is_the_latest()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        using var cts = new CancellationTokenSource(Eventually.Timeout);
        await using var stream = source.StreamAsync(new TelemetryStreamOptions { Interval = TimeSpan.FromSeconds(2) }, cts.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());
        var timers = clock.TimersCreated;

        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 1.0 }, clock.GetUtcNow(), source.Generation);
        var next = stream.MoveNextAsync().AsTask();
        await clock.WaitForTimersAsync(timers + 1);
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 2.0 }, clock.GetUtcNow(), source.Generation);
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 3.0 }, clock.GetUtcNow(), source.Generation);
        await Eventually.SettleAsync();
        Assert.False(next.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.True(await next.WaitAsync(Eventually.Timeout));
        Assert.Equal(3.0, stream.Current.Flight.IndicatedAirspeedKnots.Value);
    }

    [Fact]
    public void A_non_positive_stream_interval_is_rejected_on_the_call()
    {
        var source = new TelemetrySource(new TestClock(), StaleAfter);

        Assert.Throws<ArgumentOutOfRangeException>(() => source.StreamAsync(new TelemetryStreamOptions { Interval = TimeSpan.Zero }));
    }

    [Fact]
    public async Task Several_streams_are_independent_and_a_throwing_consumer_ends_only_its_own()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        using var cts = new CancellationTokenSource(Eventually.Timeout);
        var options = new TelemetryStreamOptions { Interval = TimeSpan.FromTicks(1) };

        var failing = Task.Run(async () =>
        {
            await foreach (var _ in source.StreamAsync(options, cts.Token))
            {
                throw new InvalidOperationException("consumer bug");
            }
        });
        await using var healthy = source.StreamAsync(options, cts.Token).GetAsyncEnumerator();
        Assert.True(await healthy.MoveNextAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing);

        clock.Advance(TimeSpan.FromSeconds(1));
        source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = 180.0 }, clock.GetUtcNow(), source.Generation);

        Assert.True(await healthy.MoveNextAsync());
        Assert.Equal(180.0, healthy.Current.Flight.IndicatedAirspeedKnots.Value);
    }

    [Fact]
    public async Task A_slow_consumer_does_not_block_publication_and_then_sees_the_latest()
    {
        var clock = new TestClock();
        var source = new TelemetrySource(clock, StaleAfter);
        using var cts = new CancellationTokenSource(Eventually.Timeout);
        await using var slow = source.StreamAsync(new TelemetryStreamOptions { Interval = TimeSpan.FromTicks(1) }, cts.Token).GetAsyncEnumerator();
        Assert.True(await slow.MoveNextAsync());

        for (var i = 1; i <= 500; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(10));
            source.ApplyFast(new FastGroupVars { IndicatedAirspeedKnots = i }, clock.GetUtcNow(), source.Generation);
        }

        Assert.True(await slow.MoveNextAsync());
        Assert.Equal(500.0, slow.Current.Flight.IndicatedAirspeedKnots.Value);
    }

    [Fact]
    public async Task Cancelling_ends_the_stream()
    {
        var source = new TelemetrySource(new TestClock(), StaleAfter);
        using var cts = new CancellationTokenSource();
        await using var stream = source.StreamAsync(cancellationToken: cts.Token).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());

        var next = stream.MoveNextAsync().AsTask();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => next.WaitAsync(Eventually.Timeout));
    }

    [Fact]
    public async Task Complete_ends_every_stream()
    {
        var source = new TelemetrySource(new TestClock(), StaleAfter);
        await using var stream = source.StreamAsync().GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync());

        source.Complete();

        Assert.False(await stream.MoveNextAsync().AsTask().WaitAsync(Eventually.Timeout));
    }
}
