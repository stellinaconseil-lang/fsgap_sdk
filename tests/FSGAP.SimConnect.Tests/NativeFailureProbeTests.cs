using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>BLOCK 10C.1 — RESEARCH ONLY: the diagnostic brake-failure transport stays an internal, allowlisted harness.</summary>
public class NativeFailureProbeTests
{
    [Fact]
    public void Only_the_three_documented_brake_failure_toggles_are_allowed()
    {
        Assert.Equal(
            ["TOGGLE_LEFT_BRAKE_FAILURE", "TOGGLE_RIGHT_BRAKE_FAILURE", "TOGGLE_TOTAL_BRAKE_FAILURE"],
            NativeFailureProbeEvents.ClientEventIds.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Client_event_ids_are_distinct_and_never_collide_with_the_system_event_subscriptions()
    {
        var ids = NativeFailureProbeEvents.ClientEventIds.Values.ToArray();

        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(1u, ids);
        Assert.DoesNotContain(2u, ids);
    }

    [Theory]
    [InlineData("TIRE_FAILURE")]
    [InlineData("TIRE_PRESSURE_FAILURE")]
    [InlineData("TOGGLE_ENGINE1_FAILURE")]
    [InlineData("TOGGLE_HYDRAULIC_FAILURE")]
    [InlineData("REPAIR_AND_REFUEL")]
    [InlineData("toggle_left_brake_failure")]
    [InlineData("")]
    public void Any_other_event_is_rejected(string eventName)
    {
        Assert.Throws<ArgumentException>(() => NativeFailureProbeEvents.IdOf(eventName));
    }

    [Fact]
    public async Task Transmission_needs_a_connection()
    {
        await using var h = new Harness();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Simulator.ExperimentalTransmitNativeFailureEventAsync(NativeFailureProbeEvents.ToggleLeftBrakeFailure, CancellationToken.None));
    }

    [Fact]
    public async Task A_disallowed_event_is_rejected_before_anything_reaches_the_session()
    {
        await using var h = new Harness();
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        await Assert.ThrowsAsync<ArgumentException>(
            () => h.Simulator.ExperimentalTransmitNativeFailureEventAsync("TIRE_FAILURE", CancellationToken.None));
    }

    [Fact]
    public async Task Transmission_refuses_a_session_that_is_not_the_native_one()
    {
        await using var h = new Harness();
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => h.Simulator.ExperimentalTransmitNativeFailureEventAsync(NativeFailureProbeEvents.ToggleTotalBrakeFailure, CancellationToken.None));
        Assert.Contains("SimConnect.NET session", error.Message);
    }

    [Fact]
    public void Accepted_means_no_failed_call_and_no_exception_packet()
    {
        var ok = new NativeFailureEventResult { EventName = "E", ClientEventId = 1, MapHResult = 0, TransmitHResult = 0 };

        Assert.True(ok.Accepted);
        Assert.True((ok with { MapHResult = null }).Accepted);
        Assert.False((ok with { MapHResult = unchecked((int)0x80004005) }).Accepted);
        Assert.False((ok with { TransmitHResult = unchecked((int)0x80004005) }).Accepted);
        Assert.False((ok with { Exceptions = ["exception 1 (send id 3, index 0)"] }).Accepted);
    }
}
