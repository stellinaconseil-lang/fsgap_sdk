using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// Exercises the real SimConnect.NET library and native SimConnect.dll. Passes with or without MSFS: the connection
/// must settle into Connected (MSFS running) or WaitingForSimulator (MSFS absent), never Faulted, and stop cleanly.
/// </summary>
public class RealLibrarySmokeTests
{
    [Fact]
    public async Task Real_transport_settles_and_stops_cleanly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // SimConnect only exists on Windows.
        }

        await using var simulator = new SimConnectSimulator(new FsgapOptions
        {
            ApplicationName = "FSGAP smoke test",
            DataDirectory = Path.Combine(Path.GetTempPath(), "fsgap-smoke"),
        });

        await simulator.StartAsync();
        await Eventually.TrueAsync(
            () => simulator.Status.State is SimulatorConnectionState.Connected or SimulatorConnectionState.WaitingForSimulator
                or SimulatorConnectionState.Faulted,
            "the connection to settle");

        Assert.NotEqual(SimulatorConnectionState.Faulted, simulator.Status.State);
        await simulator.StopAsync().WaitAsync(Eventually.Timeout);
        Assert.Equal(SimulatorConnectionState.Disconnected, simulator.Status.State);
    }
}
