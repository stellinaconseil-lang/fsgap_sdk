using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;

namespace FSGAP.Core.Tests;

public class AircraftSessionTests
{
    [Fact]
    public async Task Dispose_disposes_components_once()
    {
        var telemetry = new DisposableTelemetry();
        var session = new AircraftSession("test", new AircraftIdentity(), AircraftCapabilities.None, telemetry, UnsupportedFailureProvider.Instance);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, telemetry.DisposeCount);
    }

    [Fact]
    public void Exposes_what_it_was_given()
    {
        var identity = new AircraftIdentity { Model = "X" };
        var telemetry = new DisposableTelemetry();

        var session = new AircraftSession("test", identity, AircraftCapabilities.None, telemetry, UnsupportedFailureProvider.Instance);

        Assert.Equal("test", session.ProviderId);
        Assert.Same(identity, session.Identity);
        Assert.Same(AircraftCapabilities.None, session.Capabilities);
        Assert.Same(telemetry, session.Telemetry);
        Assert.Same(UnsupportedFailureProvider.Instance, session.Failures);
    }

    private sealed class DisposableTelemetry : ITelemetryProvider, IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AircraftTelemetry.Unavailable(DateTimeOffset.UnixEpoch));

        public IAsyncEnumerable<AircraftTelemetry> StreamAsync(TelemetryStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
