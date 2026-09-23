using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Simulator;

namespace FSGAP.Core.Tests;

public class SimulatorObservationTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Connection_lifecycle_distinguishes_every_situation()
    {
        await using var simulator = new FakeSimulator();
        var seen = new List<SimulatorConnectionState>();
        using var cts = new CancellationTokenSource();
        var watch = Task.Run(async () =>
        {
            await foreach (var status in simulator.WatchStatusAsync(cts.Token))
            {
                lock (seen)
                {
                    seen.Add(status.State);
                }
            }
        });

        await WaitUntilSeenAsync(seen, count: 1); // initial Disconnected
        await simulator.StartAsync();
        await WaitUntilSeenAsync(seen, count: 2);
        var script = new[]
        {
            SimulatorConnectionState.WaitingForSimulator, // MSFS absent
            SimulatorConnectionState.Connected,
            SimulatorConnectionState.Reconnecting, // connection lost
            SimulatorConnectionState.Connected,
            SimulatorConnectionState.Faulted, // terminal error
        };
        for (var i = 0; i < script.Length; i++)
        {
            simulator.MoveTo(script[i]);
            await WaitUntilSeenAsync(seen, count: 3 + i); // one state at a time, so none is skipped
        }

        await simulator.DisposeAsync();
        await watch.WaitAsync(Timeout);
        Assert.Equal(
            [
                SimulatorConnectionState.Disconnected, SimulatorConnectionState.Connecting, SimulatorConnectionState.WaitingForSimulator,
                SimulatorConnectionState.Connected, SimulatorConnectionState.Reconnecting, SimulatorConnectionState.Connected,
                SimulatorConnectionState.Faulted,
            ],
            seen);
    }

    [Fact]
    public async Task WaitForState_returns_immediately_when_already_there()
    {
        await using var simulator = new FakeSimulator();
        simulator.MoveTo(SimulatorConnectionState.Connected);

        var status = await simulator.WaitForStateAsync(SimulatorConnectionState.Connected).WaitAsync(Timeout);

        Assert.Equal(SimulatorConnectionState.Connected, status.State);
    }

    [Fact]
    public async Task WaitForState_waits_for_a_later_transition()
    {
        await using var simulator = new FakeSimulator();
        var waiting = simulator.WaitForStateAsync(SimulatorConnectionState.Connected);

        simulator.MoveTo(SimulatorConnectionState.WaitingForSimulator);
        Assert.False(waiting.IsCompleted);
        simulator.MoveTo(SimulatorConnectionState.Connected, "MSFS started");

        var status = await waiting.WaitAsync(Timeout);
        Assert.Equal("MSFS started", status.Detail);
    }

    [Fact]
    public async Task WaitForState_fails_when_the_connection_ends_first()
    {
        var simulator = new FakeSimulator();
        var waiting = simulator.WaitForStateAsync(SimulatorConnectionState.Connected);

        await simulator.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => waiting.WaitAsync(Timeout));
    }

    [Fact]
    public async Task WaitForAircraft_ignores_empty_detections()
    {
        await using var simulator = new FakeSimulator();
        IAircraftDetector detector = simulator;
        var waiting = detector.WaitForAircraftAsync();

        simulator.Load(null);
        Assert.False(waiting.IsCompleted);
        simulator.Load(new AircraftDescriptor { Title = "Test Airliner", LiveryFolder = "livery-a" });

        var aircraft = await waiting.WaitAsync(Timeout);
        Assert.Equal("livery-a", aircraft.LiveryFolder);
    }

    [Fact]
    public async Task WaitForAircraft_can_be_cancelled()
    {
        await using var simulator = new FakeSimulator();
        using var cts = new CancellationTokenSource();
        var waiting = ((IAircraftDetector)simulator).WaitForAircraftAsync(cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Aircraft_change_is_reported_once_per_different_descriptor()
    {
        await using var simulator = new FakeSimulator();
        IAircraftDetector detector = simulator;
        await using var watcher = detector.WatchAsync().GetAsyncEnumerator();
        Assert.True(await watcher.MoveNextAsync());
        Assert.Null(watcher.Current);

        simulator.Load(new AircraftDescriptor { Title = "Test Airliner", LiveryFolder = "livery-a" });
        simulator.Load(new AircraftDescriptor { Title = "Test Airliner", LiveryFolder = "livery-a" }); // same aircraft, polled again
        simulator.Load(new AircraftDescriptor { Title = "Test Airliner", LiveryFolder = "livery-b" });

        Assert.True(await watcher.MoveNextAsync().AsTask().WaitAsync(Timeout));
        Assert.Equal("livery-b", watcher.Current!.LiveryFolder);
        Assert.Equal("livery-b", detector.Current!.LiveryFolder);
    }

    [Fact]
    public async Task Crash_count_lets_a_late_observer_notice_a_crash()
    {
        await using var simulator = new FakeSimulator();
        ISimulatorStateProvider provider = simulator;
        var crashAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        simulator.SetState(new SimulatorState { Paused = TelemetryValue<bool>.Known(true, crashAt) });
        simulator.SetState(new SimulatorState { Paused = TelemetryValue<bool>.Known(false, crashAt), CrashCount = 1, LastCrashAt = crashAt });

        var observed = await provider.WatchAsync().FirstAsync().WaitAsync(Timeout);
        Assert.Equal(1, observed.CrashCount);
        Assert.Equal(crashAt, observed.LastCrashAt);
        Assert.False(observed.Paused.Value);
    }

    [Fact]
    public async Task Stop_resets_the_session_clock()
    {
        await using var simulator = new FakeSimulator { SessionElapsed = TimeSpan.FromMinutes(12) };

        await simulator.StopAsync();

        Assert.Equal(TimeSpan.Zero, simulator.SessionElapsed);
        Assert.Equal(SimulatorConnectionState.Disconnected, simulator.Status.State);
    }

    private static async Task WaitUntilSeenAsync(List<SimulatorConnectionState> seen, int count)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (true)
        {
            lock (seen)
            {
                if (seen.Count >= count)
                {
                    return;
                }
            }

            Assert.True(DateTime.UtcNow < deadline, $"Only {seen.Count} of {count} states were observed.");
            await Task.Delay(5);
        }
    }
}
