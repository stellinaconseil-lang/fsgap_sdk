using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Telemetry;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Core.Tests;

public class PollingTelemetryStreamTests
{
    private static readonly TelemetryStreamOptions EverySecond = new() { Interval = TimeSpan.FromSeconds(1) };

    [Fact]
    public async Task Emits_immediately_then_once_per_interval()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var reads = 0;
        Task<AircraftTelemetry> Read(CancellationToken _)
        {
            reads++;
            return Task.FromResult(AircraftTelemetry.Unavailable(clock.GetUtcNow()));
        }

        await using var stream = PollingTelemetryStream.Create(Read, EverySecond, clock).GetAsyncEnumerator();

        Assert.True(await stream.MoveNextAsync());
        Assert.Equal(1, reads);

        var next = stream.MoveNextAsync();       // now waiting for the interval
        Assert.False(next.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(next.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(1));

        Assert.True(await next);
        Assert.Equal(2, reads);
        Assert.Equal(clock.GetUtcNow(), stream.Current.Timestamp);
    }

    [Fact]
    public async Task Cancellation_ends_the_stream()
    {
        var clock = new FakeTimeProvider();
        using var cts = new CancellationTokenSource();
        var stream = new UnavailableTelemetryProvider(clock).StreamAsync(EverySecond, cts.Token);
        await using var enumerator = stream.GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());

        var next = enumerator.MoveNextAsync();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await next);
    }

    [Fact]
    public void Non_positive_interval_is_rejected()
    {
        var options = new TelemetryStreamOptions { Interval = TimeSpan.Zero };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PollingTelemetryStream.Create(_ => Task.FromResult(AircraftTelemetry.Unavailable(default)), options));
    }
}
