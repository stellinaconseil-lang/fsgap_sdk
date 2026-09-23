using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Failures;
using FSGAP.Core.Telemetry;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Core.Tests;

public class NullObjectProvidersTests
{
    private static readonly FailureCommand EngineFire = new(FailureType.EngineFire, FailureTarget.Engine(1));

    [Fact]
    public async Task Unavailable_telemetry_snapshot_is_timestamped_and_empty()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 4, 3, 2, 1, TimeSpan.Zero));

        var snapshot = await new UnavailableTelemetryProvider(clock).GetSnapshotAsync();

        Assert.Equal(clock.GetUtcNow(), snapshot.Timestamp);
        Assert.Equal(ValueState.Unavailable, snapshot.Flight.OnGround.State);
        Assert.Empty(snapshot.Engines);
    }

    [Fact]
    public async Task Unsupported_failures_cannot_be_read()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => UnsupportedFailureProvider.Instance.GetActiveFailuresAsync());
    }

    [Fact]
    public async Task Unsupported_failures_cannot_be_triggered_or_cleared()
    {
        var trigger = await UnsupportedFailureProvider.Instance.TriggerAsync(EngineFire);
        var clear = await UnsupportedFailureProvider.Instance.ClearAsync(EngineFire);

        Assert.Equal(FailureCommandStatus.NotSupported, trigger.Status);
        Assert.Equal(FailureCommandStatus.NotSupported, clear.Status);
    }
}
