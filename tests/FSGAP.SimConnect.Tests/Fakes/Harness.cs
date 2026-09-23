using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Simulator;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Tests.Fakes;

/// <summary>A transport wired to a fake native layer, a fake clock and a capturing logger.</summary>
internal sealed class Harness : IAsyncDisposable
{
    public Harness(TimeSpan? retryDelay = null)
    {
        Options = new FsgapOptions
        {
            ApplicationName = "FsgapTests",
            DataDirectory = Path.Combine(Path.GetTempPath(), "fsgap-simconnect-tests"),
            Connection = new SimulatorConnectionOptions { RetryDelay = retryDelay ?? TimeSpan.FromSeconds(5) },
        };
        Simulator = new SimConnectSimulator(Options, Factory, Logger, Clock);
    }

    public FsgapOptions Options { get; }

    public TestClock Clock { get; } = new();

    public FakeSessionFactory Factory { get; } = new();

    public ListLogger Logger { get; } = new();

    public SimConnectSimulator Simulator { get; }

    public TimeSpan RetryDelay => Options.Connection.RetryDelay;

    public Task<SimulatorConnectionStatus> WaitForAsync(SimulatorConnectionState state) =>
        Simulator.WaitForStateAsync(state).WaitAsync(Eventually.Timeout);

    /// <summary>Waits for the next retry timer to exist, then advances the clock by the retry delay.</summary>
    public async Task ElapseRetryAsync(int timersBefore)
    {
        await Clock.WaitForTimersAsync(timersBefore + 1);
        Clock.Advance(RetryDelay);
    }

    public ValueTask DisposeAsync() => Simulator.DisposeAsync();
}

/// <summary>Raw identity builder for tests.</summary>
internal static class Identity
{
    public static RawAircraftIdentity Of(string? title, string? atcId = null, string? liveryFolder = null, string? liveryName = null) =>
        new(title, atcId, liveryFolder, liveryName);
}
