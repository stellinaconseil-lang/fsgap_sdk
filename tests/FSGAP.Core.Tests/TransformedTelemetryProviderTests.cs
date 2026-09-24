using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Telemetry;

namespace FSGAP.Core.Tests;

public class TransformedTelemetryProviderTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static AircraftTelemetry WithAirspeed(double knots) => AircraftTelemetry.Unavailable(At) with
    {
        Flight = new FlightStateTelemetry { IndicatedAirspeedKnots = TelemetryValue<double>.Known(knots, At) },
    };

    private static AircraftTelemetry HideAirspeed(AircraftTelemetry t) => t with
    {
        Flight = t.Flight with { IndicatedAirspeedKnots = TelemetryValue<double>.Unavailable },
    };

    [Fact]
    public async Task Snapshot_is_transformed_and_the_source_snapshot_is_untouched()
    {
        var source = new ListProvider(WithAirspeed(250.0));
        var provider = new TransformedTelemetryProvider(source, HideAirspeed);

        var snapshot = await provider.GetSnapshotAsync();

        Assert.Equal(ValueState.Unavailable, snapshot.Flight.IndicatedAirspeedKnots.State);
        Assert.Equal(250.0, source.Snapshots[0].Flight.IndicatedAirspeedKnots.Value);
    }

    [Fact]
    public async Task Every_streamed_snapshot_is_transformed_and_options_reach_the_source()
    {
        var source = new ListProvider(WithAirspeed(100.0), WithAirspeed(200.0));
        var provider = new TransformedTelemetryProvider(source, t => t with { Timestamp = t.Timestamp.AddHours(1) });
        var options = new TelemetryStreamOptions { Interval = TimeSpan.FromSeconds(3) };

        var items = await provider.StreamAsync(options).ToListAsync();

        Assert.Equal([100.0, 200.0], items.Select(i => i.Flight.IndicatedAirspeedKnots.Value));
        Assert.All(items, i => Assert.Equal(At.AddHours(1), i.Timestamp));
        Assert.Same(options, source.LastOptions);
    }

    [Fact]
    public void Arguments_are_required()
    {
        Assert.Throws<ArgumentNullException>(() => new TransformedTelemetryProvider(null!, t => t));
        Assert.Throws<ArgumentNullException>(() => new TransformedTelemetryProvider(new ListProvider(), null!));
    }

    private sealed class ListProvider(params AircraftTelemetry[] snapshots) : ITelemetryProvider
    {
        public IReadOnlyList<AircraftTelemetry> Snapshots { get; } = snapshots;

        public TelemetryStreamOptions? LastOptions { get; private set; }

        public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshots[0]);

        public async IAsyncEnumerable<AircraftTelemetry> StreamAsync(
            TelemetryStreamOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            foreach (var snapshot in Snapshots)
            {
                await Task.Yield();
                yield return snapshot;
            }
        }
    }
}
