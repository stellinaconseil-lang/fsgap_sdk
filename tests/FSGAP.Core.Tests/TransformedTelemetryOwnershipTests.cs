using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;
using FSGAP.Core.Telemetry;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Core.Tests;

/// <summary>The owned resource of a <see cref="TransformedTelemetryProvider"/> (BLOCK 6).</summary>
public class TransformedTelemetryOwnershipTests
{
    private sealed class Resource : IAsyncDisposable
    {
        public int Disposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DisposableSource : ITelemetryProvider, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AircraftTelemetry.Unavailable(DateTimeOffset.UnixEpoch));

        public IAsyncEnumerable<AircraftTelemetry> StreamAsync(TelemetryStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task The_owned_resource_is_disposed_once_and_the_source_never()
    {
        var resource = new Resource();
        var source = new DisposableSource();
        var provider = new TransformedTelemetryProvider(source, t => t, resource);

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        Assert.Equal(1, resource.Disposals);
        Assert.False(source.Disposed);
    }

    [Fact]
    public async Task Disposing_a_session_stops_what_its_telemetry_owns()
    {
        var resource = new Resource();
        var telemetry = new TransformedTelemetryProvider(new UnavailableTelemetryProvider(new FakeTimeProvider()), t => t, resource);
        var session = new AircraftSession("test", new AircraftIdentity(), AircraftCapabilities.None, telemetry, UnsupportedFailureProvider.Instance);

        await session.DisposeAsync();

        Assert.Equal(1, resource.Disposals);
    }

    [Fact]
    public async Task Without_an_owned_resource_disposal_is_a_no_op()
    {
        var provider = new TransformedTelemetryProvider(new UnavailableTelemetryProvider(new FakeTimeProvider()), t => t);

        await provider.DisposeAsync();

        Assert.Equal(ValueState.Unavailable, (await provider.GetSnapshotAsync()).Flight.OnGround.State);
    }
}
