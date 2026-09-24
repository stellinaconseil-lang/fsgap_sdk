using System.Reflection;
using FSGAP.Abstractions.Geography;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary><see cref="IAirportService"/> on the transport: one session, cache, errors, lifecycle.</summary>
public class AirportServiceTests
{
    private static readonly GeoPosition Parked = new(43.664477, 7.226887);

    private static readonly RawAirport[] Bubble =
    [
        new("LFMN", "LF", 43.665278, 7.215, 4.9),
        new("LF5QW", "", 43.689335, 7.241638, 35.4),
        new("LFMD", "LF", 43.546389, 6.954167, 2.4),
    ];

    private static async Task<(Harness H, FakeSession Session)> ConnectedAsync(bool pollTelemetry = false)
    {
        var h = new Harness(pollTelemetry: pollTelemetry);
        var session = h.Factory.SimulatorPresent(Identity.Of("Cessna Skyhawk G1000 Asobo"));
        session.Airports = Bubble;
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        return (h, session);
    }

    [Fact]
    public async Task Nearby_and_nearest_come_from_the_connected_session()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        IAirportService airports = h.Simulator;

        var nearby = await airports.FindNearbyAirportsAsync(Parked);
        var nearest = await airports.FindNearestAirportAsync(Parked);

        Assert.Equal(["LFMN", "LF5QW", "LFMD"], nearby.Select(a => a.Icao));
        Assert.Equal("LFMN", nearest!.Icao);
        Assert.InRange(nearest.DistanceNauticalMiles, 0.5, 0.6);
        Assert.Single(h.Factory.Sessions);
        Assert.Equal(1, h.Factory.Attempts);
    }

    [Fact]
    public async Task No_airport_in_range_is_an_empty_answer_not_an_error()
    {
        var (h, _) = await ConnectedAsync();
        await using var __ = h;
        var farAway = new GeoPosition(-33.9, 151.2);

        Assert.Empty(await h.Simulator.FindNearbyAirportsAsync(farAway));
        Assert.Null(await h.Simulator.FindNearestAirportAsync(farAway));
    }

    [Fact]
    public async Task Without_a_connection_the_simulator_is_reported_unavailable()
    {
        await using var h = new Harness();

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => h.Simulator.FindNearestAirportAsync(Parked));

        Assert.Equal(SimulatorServiceError.SimulatorUnavailable, error.Error);
        Assert.Equal(0, h.Factory.Attempts);
    }

    [Fact]
    public async Task A_failed_query_is_reported_as_such_and_is_not_cached()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.AirportFailure = new FormatException("layout mismatch");

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => h.Simulator.FindNearestAirportAsync(Parked));
        session.AirportFailure = null;
        var retry = await h.Simulator.FindNearestAirportAsync(Parked);

        Assert.Equal(SimulatorServiceError.QueryFailed, error.Error);
        Assert.Contains("layout mismatch", error.Message, StringComparison.Ordinal);
        Assert.Equal("LFMN", retry!.Icao);
        Assert.Equal(2, session.AirportRequests);
    }

    [Theory]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(InvalidOperationException))]
    public async Task Timeouts_and_rejections_are_query_failures(Type exceptionType)
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.AirportFailure = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => h.Simulator.FindNearbyAirportsAsync(Parked));

        Assert.Equal(SimulatorServiceError.QueryFailed, error.Error);
    }

    [Fact]
    public async Task The_list_is_reused_for_ten_seconds_then_requested_again()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;

        await h.Simulator.FindNearestAirportAsync(Parked);
        await h.Simulator.FindNearbyAirportsAsync(new GeoPosition(43.55, 6.95));
        h.Clock.Advance(SimConnectSimulator.AirportListCacheLifetime);
        await h.Simulator.FindNearestAirportAsync(Parked);
        Assert.Equal(1, session.AirportRequests);

        h.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await h.Simulator.FindNearestAirportAsync(Parked);
        Assert.Equal(2, session.AirportRequests);
    }

    [Fact]
    public async Task Concurrent_searches_share_one_native_request()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.AirportGate = new TaskCompletionSource();

        var searches = Enumerable.Range(0, 8).Select(_ => h.Simulator.FindNearestAirportAsync(Parked)).ToArray();
        await Eventually.TrueAsync(() => session.AirportRequests == 1, "the shared request");
        session.AirportGate.SetResult();
        var results = await Task.WhenAll(searches).WaitAsync(Eventually.Timeout);

        Assert.All(results, r => Assert.Equal("LFMN", r!.Icao));
        Assert.Equal(1, session.AirportRequests);
    }

    [Fact]
    public async Task A_caller_cancelling_does_not_cancel_the_shared_request_for_the_others()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.AirportGate = new TaskCompletionSource();
        using var cts = new CancellationTokenSource();

        var cancelled = h.Simulator.FindNearestAirportAsync(Parked, cancellationToken: cts.Token);
        var other = h.Simulator.FindNearestAirportAsync(Parked);
        await Eventually.TrueAsync(() => session.AirportRequests == 1, "the shared request");
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(Eventually.Timeout));
        session.AirportGate.SetResult();

        Assert.Equal("LFMN", (await other.WaitAsync(Eventually.Timeout))!.Icao);
    }

    [Fact]
    public async Task A_connection_loss_during_a_search_is_reported_unavailable()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;
        session.AirportGate = new TaskCompletionSource();

        var search = h.Simulator.FindNearestAirportAsync(Parked);
        await Eventually.TrueAsync(() => session.AirportRequests == 1, "the request");
        session.Drop();

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => search.WaitAsync(Eventually.Timeout));
        Assert.Equal(SimulatorServiceError.SimulatorUnavailable, error.Error);
    }

    [Fact]
    public async Task After_a_reconnection_the_new_session_answers_without_the_old_cache()
    {
        var h = new Harness();
        await using var _ = h;
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        var second = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        first.Airports = Bubble;
        second.Airports = [new RawAirport("LFMD", "LF", 43.546389, 6.954167, 2.4)];
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        Assert.Equal("LFMN", (await h.Simulator.FindNearestAirportAsync(Parked))!.Icao);
        var timers = h.Clock.TimersCreated;

        first.Drop();
        await h.WaitForAsync(SimulatorConnectionState.Reconnecting);
        await Assert.ThrowsAsync<SimulatorServiceException>(() => h.Simulator.FindNearestAirportAsync(Parked));
        await h.ElapseRetryAsync(timers);
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        Assert.Equal("LFMD", (await h.Simulator.FindNearestAirportAsync(Parked))!.Icao);
        Assert.Equal(1, second.AirportRequests);
        Assert.Equal(1, h.Factory.MaxLiveSessions);
    }

    [Fact]
    public async Task Stop_during_a_search_reports_unavailable_and_dispose_refuses_new_searches()
    {
        var (h, session) = await ConnectedAsync();
        session.AirportGate = new TaskCompletionSource();

        var search = h.Simulator.FindNearestAirportAsync(Parked);
        await Eventually.TrueAsync(() => session.AirportRequests == 1, "the request");
        await h.Simulator.StopAsync();

        var error = await Assert.ThrowsAsync<SimulatorServiceException>(() => search.WaitAsync(Eventually.Timeout));
        Assert.Equal(SimulatorServiceError.SimulatorUnavailable, error.Error);
        await h.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Simulator.FindNearestAirportAsync(Parked));
    }

    [Fact]
    public async Task A_search_does_not_disturb_the_telemetry_on_the_same_session()
    {
        var (h, session) = await ConnectedAsync(pollTelemetry: true);
        await using var _ = h;
        session.SetGroup(new FastGroupVars { IndicatedAirspeedKnots = 250, LatitudeDegrees = Parked.LatitudeDegrees, LongitudeDegrees = Parked.LongitudeDegrees });
        session.AirportGate = new TaskCompletionSource();

        var search = h.Simulator.FindNearestAirportAsync(Parked);
        await Eventually.TrueAsync(
            () => h.Simulator.Telemetry.GetSnapshotAsync().GetAwaiter().GetResult().Flight.IndicatedAirspeedKnots.IsKnown || AdvanceOnce(h),
            "telemetry while the search is pending");

        var t = await h.Simulator.Telemetry.GetSnapshotAsync();
        Assert.Equal(250.0, t.Flight.IndicatedAirspeedKnots.Value);
        Assert.False(search.IsCompleted); // the telemetry flowed while the search was pending
        session.AirportGate.SetResult();
        Assert.Equal("LFMN", (await search.WaitAsync(Eventually.Timeout))!.Icao);
        Assert.Single(h.Factory.Sessions);
    }

    [Fact]
    public async Task The_telemetry_position_feeds_the_search()
    {
        var (h, session) = await ConnectedAsync(pollTelemetry: true);
        await using var _ = h;
        session.SetGroup(new FastGroupVars { LatitudeDegrees = 43.5479, LongitudeDegrees = 6.9533 });
        AircraftTelemetry t;
        do
        {
            h.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(20);
            t = await h.Simulator.Telemetry.GetSnapshotAsync();
        }
        while (!t.Flight.LatitudeDegrees.IsKnown);

        var position = GeoPosition.TryFrom(t.Flight.LatitudeDegrees.Value, t.Flight.LongitudeDegrees.Value)!.Value;

        Assert.Equal("LFMD", (await h.Simulator.FindNearestAirportAsync(position))!.Icao);
    }

    [Fact]
    public async Task Invalid_options_are_rejected_before_any_request()
    {
        var (h, session) = await ConnectedAsync();
        await using var _ = h;

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => h.Simulator.FindNearbyAirportsAsync(Parked, new AirportSearchOptions { MaxResults = 0 }));
        Assert.Equal(0, session.AirportRequests);
    }

    [Fact]
    public async Task Identity_telemetry_variables_and_airports_all_use_the_one_native_session()
    {
        var (h, session) = await ConnectedAsync(pollTelemetry: true);
        await using var _ = h;

        await h.Simulator.ReadAsync([new Abstractions.Simulator.SimulatorVariable("L:ANY", "number")]); // the aircraft-provider path
        await h.Simulator.FindNearestAirportAsync(Parked);
        await Eventually.TrueAsync(() => session.GroupReads<FastGroupVars>() > 0 || AdvanceOnce(h), "a telemetry read");

        Assert.True(session.IdentityReads > 0);
        Assert.True(session.GroupReads<FastGroupVars>() > 0);
        Assert.Single(session.VariableReads);
        Assert.Equal(1, session.AirportRequests);
        Assert.Same(session, Assert.Single(h.Factory.Sessions));
        Assert.Equal(1, h.Factory.Attempts);
    }

    private static bool AdvanceOnce(Harness h)
    {
        h.Clock.Advance(TimeSpan.FromSeconds(1));
        return false;
    }

    // ---- reflection boundary ---------------------------------------------------------------------------------------

    [Fact]
    public void The_pinned_simconnect_net_internals_are_present()
    {
        var resolution = FacilityInterop.Resolve(typeof(global::SimConnect.NET.SimConnectClient).Assembly, typeof(global::SimConnect.NET.SimConnectClient));

        Assert.Null(resolution.Error);
        Assert.Null(FacilityInterop.Compatibility);
        Assert.NotNull(resolution.RequestList);
        Assert.Equal(typeof(Task<int>), resolution.InvokeNative!.ReturnType);
        Assert.Equal([typeof(Func<IntPtr, int>), typeof(CancellationToken)], resolution.InvokeNative.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void The_referenced_simconnect_net_is_the_pinned_version()
    {
        // Upgrading SimConnect.NET must be deliberate: this fails first, pointing at FacilityInterop.
        var version = typeof(global::SimConnect.NET.SimConnectClient).Assembly.GetName().Version!;

        Assert.Equal(FacilityInterop.PinnedLibraryVersion, version.ToString(3));
    }

    [Fact]
    public void An_incompatible_library_gives_a_clear_error_not_a_null_reference()
    {
        var resolution = FacilityInterop.Resolve(typeof(object).Assembly, typeof(object));

        Assert.NotNull(resolution.Error);
        Assert.Contains("not compatible with the FSGAP airport service", resolution.Error, StringComparison.Ordinal);
        Assert.Contains(FacilityInterop.PinnedLibraryVersion, resolution.Error, StringComparison.Ordinal);
        Assert.Null(resolution.RequestList);
    }

    [Fact]
    public void Reflection_into_simconnect_net_internals_lives_in_facility_interop_only()
    {
        var transport = typeof(SimConnectSimulator).Assembly;
        var holders = transport.GetTypes()
            .Where(t => t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Any(f => f.IsLiteral && f.GetRawConstantValue() is string s && (s.Contains("SimConnectNative", StringComparison.Ordinal) || s.Contains("InvokeNative", StringComparison.Ordinal))))
            .Select(t => t.Name);

        Assert.Equal([nameof(FacilityInterop)], holders);
    }
}
