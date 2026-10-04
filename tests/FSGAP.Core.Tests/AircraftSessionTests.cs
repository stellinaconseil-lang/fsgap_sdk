using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Cockpit;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Cockpit;
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

    [Fact]
    public void A_session_with_no_cockpit_provider_exposes_the_unsupported_one()
    {
        var session = new AircraftSession("test", new AircraftIdentity(), AircraftCapabilities.None, new DisposableTelemetry(), UnsupportedFailureProvider.Instance);

        Assert.Same(UnsupportedCockpitObservationProvider.Instance, session.CockpitObservations);
    }

    [Fact]
    public async Task Dispose_also_disposes_the_cockpit_observation_provider_once()
    {
        var cockpit = new DisposableCockpit();
        var session = new AircraftSession(
            "test",
            () => new AircraftIdentity(),
            AircraftCapabilities.None,
            new DisposableTelemetry(),
            UnsupportedFailureProvider.Instance)
        {
            CockpitObservations = cockpit,
        };

        Assert.Same(cockpit, session.CockpitObservations);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, cockpit.DisposeCount);
    }

    private sealed class DisposableCockpit : ICockpitObservationProvider, IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;

        public Task<CockpitObservationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CockpitObservationSnapshot.Empty(DateTimeOffset.UnixEpoch));
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
