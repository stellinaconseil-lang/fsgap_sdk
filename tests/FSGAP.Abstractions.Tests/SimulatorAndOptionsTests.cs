using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Abstractions.Tests;

public class SimulatorAndOptionsTests
{
    [Fact]
    public void Initial_connection_status_is_disconnected()
    {
        Assert.Equal(SimulatorConnectionState.Disconnected, default(SimulatorConnectionState));
        Assert.Equal(SimulatorConnectionState.Disconnected, SimulatorConnectionStatus.Initial.State);
        Assert.Null(SimulatorConnectionStatus.Initial.Detail);
    }

    [Fact]
    public void Initial_simulator_state_does_not_claim_the_simulation_is_running()
    {
        var state = SimulatorState.Initial;

        Assert.Equal(ValueState.Unavailable, state.Paused.State);
        Assert.Equal(0, state.CrashCount);
        Assert.Null(state.LastCrashAt);
    }

    [Fact]
    public void Options_have_the_observed_defaults()
    {
        var options = ValidOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), options.Connection.RetryDelay);
        Assert.Equal(TimeSpan.FromSeconds(15), options.Telemetry.StaleAfter);
        options.Validate();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Options_require_an_application_name(string name)
    {
        Assert.ThrowsAny<ArgumentException>(() => (ValidOptions() with { ApplicationName = name }).Validate());
    }

    [Fact]
    public void Options_require_an_absolute_data_directory()
    {
        Assert.ThrowsAny<ArgumentException>(() => (ValidOptions() with { DataDirectory = "relative/cache" }).Validate());
        Assert.ThrowsAny<ArgumentException>(() => (ValidOptions() with { DataDirectory = "" }).Validate());
    }

    [Fact]
    public void Options_reject_non_positive_durations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (ValidOptions() with { Connection = new SimulatorConnectionOptions { RetryDelay = TimeSpan.Zero } }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (ValidOptions() with { Telemetry = new TelemetryOptions { StaleAfter = TimeSpan.FromSeconds(-1) } }).Validate());
    }

    private static FsgapOptions ValidOptions() => new()
    {
        ApplicationName = "TestApp",
        DataDirectory = Path.Combine(Path.GetTempPath(), "fsgap-tests"),
    };
}
