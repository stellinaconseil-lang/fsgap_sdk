using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Degradations;

namespace FSGAP.Fenix.Tests;

/// <summary>
/// BLOCK 11.0: controlled degradations are a separate capability. Fenix keeps its true failures exactly as in 0.10 and
/// offers no degradation; it never writes a simulator variable, even when its reader can also write.
/// </summary>
public class FenixDegradationNonRegressionTests
{
    private static readonly AircraftDescriptor A320 = new() { Title = "FenixA320 IAE WF", LiveryFolder = "BAW-G-EUYA-0002" };

    /// <summary>A reader that, like the real transport, can also write; it records any write.</summary>
    private sealed class ReaderThatCanWrite(FakeVariableReader inner) : ISimulatorVariableReader, ISimulatorVariableWriter
    {
        public int Writes;

        public Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(variables, cancellationToken);

        public Task WriteAsync(SimulatorVariable variable, double value, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Writes);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_fenix_session_keeps_its_failures_offers_no_degradation_and_never_writes()
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader().WithNominalCockpit();
        var transport = new ReaderThatCanWrite(reader);
        var provider = new FenixAircraftProvider(
            timeProvider: clock,
            genericTelemetry: new StampedGenericTelemetry(clock),
            simulatorVariables: transport,
            aircraftDetector: new FakeDetector(A320),
            fenixOptions: new FenixOptions(),
            efbHttpClient: new HttpClient(new FakeEfb()));

        var session = await provider.AttachAsync(A320);

        // True failures, unchanged.
        Assert.True(session.Capabilities.Failures.CanReadActiveFailures);
        Assert.Equal(384, session.Capabilities.Failures.Catalog.Count);
        Assert.True(session.Capabilities.Failures.CanTriggerAny);
        Assert.True(session.Capabilities.Failures.CanClearAny);

        // No controlled degradation.
        Assert.Same(DegradationCapabilities.None, session.Capabilities.Degradations);
        Assert.Equal(0, session.Capabilities.Degradations.MaxActive);
        Assert.Same(UnsupportedDegradationProvider.Instance, session.Degradations);
        var key = DegradationKey.Parse("electrical.generator.1.forced-off");
        Assert.Equal(DegradationCommandStatus.NotSupported, (await session.Degradations.ApplyAsync(key)).Status);
        Assert.Equal(DegradationCommandStatus.NotSupported, (await session.Degradations.RestoreAsync(key)).Status);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.Degradations.GetStateAsync(key));

        await Wait.UntilAsync(() => reader.ReadCount > 0, "Fenix reads");
        await session.DisposeAsync();

        Assert.Equal(0, Volatile.Read(ref transport.Writes));
    }

    [Fact]
    public void The_fenix_provider_has_no_write_path()
    {
        var fenix = typeof(FenixAircraftProvider).Assembly;

        Assert.False(FenixArchitectureTests.BinaryContains(fenix, nameof(ISimulatorVariableWriter)));
        Assert.False(FenixArchitectureTests.BinaryContains(fenix, nameof(IDegradationProvider)));
    }
}
