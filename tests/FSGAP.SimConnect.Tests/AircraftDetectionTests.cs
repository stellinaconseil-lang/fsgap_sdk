using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

public class AircraftDetectionTests
{
    [Fact]
    public void Raw_identity_is_trimmed_and_empty_values_become_null()
    {
        var descriptor = AircraftIdentityMapper.ToDescriptor(Identity.Of(" Test Airliner ", "  ", " TST-F-TEST-0001 ", "Test Livery "));

        Assert.Equal(
            new AircraftDescriptor { Title = "Test Airliner", Registration = null, LiveryFolder = "TST-F-TEST-0001", Livery = "Test Livery" },
            descriptor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_title_means_no_aircraft(string? title)
    {
        Assert.Null(AircraftIdentityMapper.ToDescriptor(Identity.Of(title, "F-TEST", "folder", "livery")));
    }

    [Fact]
    public void The_transport_reports_what_the_simulator_says_and_infers_nothing()
    {
        var descriptor = AircraftIdentityMapper.ToDescriptor(Identity.Of("Some A320 CFM title", "F-TEST"))!;

        Assert.Null(descriptor.Manufacturer);
        Assert.Null(descriptor.Model);
        Assert.Null(descriptor.IcaoType);
        Assert.Null(descriptor.PackagePath);
    }

    [Fact]
    public async Task Loaded_aircraft_is_published_after_connecting()
    {
        await using var h = new Harness();
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner", "F-TEST", "TST-F-TEST-0001", "Test Livery"));

        await h.Simulator.StartAsync();

        var aircraft = await WaitForAircraftAsync(h, a => a is not null);
        Assert.Equal("Test Airliner", aircraft!.Title);
        Assert.Equal("F-TEST", aircraft.Registration);
        Assert.Equal("TST-F-TEST-0001", aircraft.LiveryFolder);
        Assert.Equal("Test Livery", aircraft.Livery);
    }

    [Fact]
    public async Task Identical_reads_do_not_notify_again()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner", liveryFolder: "livery-a"));
        await h.Simulator.StartAsync();
        await WaitForAircraftAsync(h, a => a is not null);
        await using var watcher = h.Simulator.AircraftDetector.WatchAsync().GetAsyncEnumerator();
        Assert.True(await watcher.MoveNextAsync());

        var polls = new Polls(h, session);
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval);
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval);
        var pending = watcher.MoveNextAsync().AsTask();
        await Eventually.SettleAsync();

        Assert.False(pending.IsCompleted);
        session.Identity = Identity.Of("Test Airliner", liveryFolder: "livery-b");
        await polls.NextAsync(SimConnectSimulator.IdentityStableInterval);
        Assert.True(await pending.WaitAsync(Eventually.Timeout));
        Assert.Equal("livery-b", watcher.Current!.LiveryFolder);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("livery-folder")]
    [InlineData("registration")]
    public async Task Each_identity_change_is_published(string changed)
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner", "F-TEST", "livery-a", "Livery A"));
        await h.Simulator.StartAsync();
        var initial = await WaitForAircraftAsync(h, a => a is not null);

        session.Identity = changed switch
        {
            "title" => Identity.Of("Other Airliner", "F-TEST", "livery-a", "Livery A"),
            "livery-folder" => Identity.Of("Test Airliner", "F-TEST", "livery-b", "Livery A"),
            _ => Identity.Of("Test Airliner", "F-OTHR", "livery-a", "Livery A"),
        };
        await new Polls(h, session).NextAsync(SimConnectSimulator.IdentityFastInterval);

        var updated = await WaitForAircraftAsync(h, a => a is not null && a != initial);
        Assert.NotEqual(initial, updated);
    }

    [Fact]
    public async Task Unloading_the_aircraft_or_losing_the_connection_publishes_null()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await WaitForAircraftAsync(h, a => a is not null);

        session.Identity = Identity.Of(title: "");
        await new Polls(h, session).NextAsync(SimConnectSimulator.IdentityFastInterval);
        await WaitForAircraftAsync(h, a => a is null);

        session.Identity = Identity.Of("Test Airliner");
        await new Polls(h, session, alreadyDone: 2).NextAsync(SimConnectSimulator.IdentityFastInterval);
        await WaitForAircraftAsync(h, a => a is not null);
        session.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        Assert.Null(h.Simulator.AircraftDetector.Current);
    }

    [Fact]
    public async Task Polling_slows_down_once_the_aircraft_is_stable_and_speeds_up_on_change()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        var polls = new Polls(h, session);
        await Eventually.TrueAsync(() => session.IdentityReads == 1, "first read");

        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval); // read 2
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval); // read 3: stable from now on
        await h.Clock.WaitForTimersAsync(3);
        h.Clock.Advance(SimConnectSimulator.IdentityFastInterval);
        await Eventually.SettleAsync();
        Assert.Equal(3, session.IdentityReads); // no read at the fast interval anymore

        h.Clock.Advance(SimConnectSimulator.IdentityStableInterval - SimConnectSimulator.IdentityFastInterval);
        await Eventually.TrueAsync(() => session.IdentityReads == 4, "read at the stable interval");

        session.Identity = Identity.Of("Other Airliner");
        polls = new Polls(h, session, alreadyDone: 4);
        await polls.NextAsync(SimConnectSimulator.IdentityStableInterval); // read 5 sees the change
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval); // back to the fast interval
        Assert.Equal(6, session.IdentityReads);
    }

    [Fact]
    public async Task Identity_read_failures_are_logged_once_and_polling_continues()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await WaitForAircraftAsync(h, a => a is not null);
        var polls = new Polls(h, session);

        session.IdentityFailure = new InvalidOperationException("read failed");
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval);
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval);
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval);
        session.IdentityFailure = null;
        await polls.NextAsync(SimConnectSimulator.IdentityFastInterval);

        Assert.Equal(SimulatorConnectionState.Connected, h.Simulator.Status.State);
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("Could not read the aircraft identity", StringComparison.Ordinal));
        Assert.Single(h.Logger.Entries, e => e.Message.Contains("recovered", StringComparison.Ordinal));
        Assert.Equal("Test Airliner", h.Simulator.AircraftDetector.Current?.Title);
    }

    private static async Task<AircraftDescriptor?> WaitForAircraftAsync(Harness h, Func<AircraftDescriptor?, bool> predicate)
    {
        await Eventually.TrueAsync(() => predicate(h.Simulator.AircraftDetector.Current), "aircraft");
        return h.Simulator.AircraftDetector.Current;
    }

    /// <summary>Drives the identity polling loop one read at a time with the fake clock.</summary>
    private sealed class Polls(Harness h, FakeSession session, int alreadyDone = 1)
    {
        private int _done = alreadyDone;

        public async Task NextAsync(TimeSpan interval)
        {
            await Eventually.TrueAsync(() => session.IdentityReads >= _done, $"read {_done}");
            await h.Clock.WaitForTimersAsync(_done); // one delay timer per completed read
            h.Clock.Advance(interval);
            _done++;
            await Eventually.TrueAsync(() => session.IdentityReads >= _done, $"read {_done}");

            // The read's result is processed (published, logged) before the next delay timer is created.
            await h.Clock.WaitForTimersAsync(_done);
        }
    }
}
