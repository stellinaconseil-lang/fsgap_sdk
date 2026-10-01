using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Degradations;
using FSGAP.Core.Failures;
using FSGAP.Core.Sessions;

namespace FSGAP.Core.Tests;

/// <summary>BLOCK 11.0: the unsupported degradation provider and the session's degradation slot.</summary>
public class DegradationSessionTests
{
    private static readonly DegradationKey Key = DegradationKey.Parse("electrical.generator.1.forced-off");

    [Fact]
    public async Task Unsupported_degradations_answer_not_supported_and_cannot_be_read()
    {
        var provider = UnsupportedDegradationProvider.Instance;

        var apply = await provider.ApplyAsync(Key);
        var restore = await provider.RestoreAsync(Key);

        Assert.Equal((DegradationCommandStatus.NotSupported, DegradationState.Unavailable), (apply.Status, apply.State));
        Assert.Equal(DegradationCommandStatus.NotSupported, restore.Status);
        await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetStateAsync(Key));
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.ApplyAsync(null!));
    }

    [Fact]
    public void Existing_constructors_give_a_session_without_degradations()
    {
        var telemetry = new Telemetry();

        var fixedIdentity = new AircraftSession("test", new AircraftIdentity(), AircraftCapabilities.None, telemetry, UnsupportedFailureProvider.Instance);
        var dynamicIdentity = new AircraftSession("test", () => new AircraftIdentity(), AircraftCapabilities.None, telemetry, UnsupportedFailureProvider.Instance);

        Assert.Same(UnsupportedDegradationProvider.Instance, fixedIdentity.Degradations);
        Assert.Same(UnsupportedDegradationProvider.Instance, dynamicIdentity.Degradations);
        Assert.Same(DegradationCapabilities.None, fixedIdentity.Capabilities.Degradations);
    }

    [Fact]
    public async Task Degradations_are_disposed_once_and_before_the_telemetry()
    {
        var order = new List<string>();
        var telemetry = new Telemetry(order);
        var degradations = new Degradations(order);
        var session = new AircraftSession("test", () => new AircraftIdentity(), AircraftCapabilities.None, telemetry, UnsupportedFailureProvider.Instance, degradations);

        Assert.Same(degradations, session.Degradations);
        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(["degradations", "telemetry"], order);
    }

    [Fact]
    public async Task One_object_serving_telemetry_and_degradations_is_disposed_once()
    {
        var order = new List<string>();
        var both = new Both(order);
        var session = new AircraftSession("test", () => new AircraftIdentity(), AircraftCapabilities.None, both, UnsupportedFailureProvider.Instance, both);

        await session.DisposeAsync();

        Assert.Equal(["both"], order);
    }

    [Fact]
    public void A_null_degradation_provider_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AircraftSession("test", () => new AircraftIdentity(), AircraftCapabilities.None, new Telemetry(), UnsupportedFailureProvider.Instance, null!));
    }

    private class Telemetry(List<string>? order = null) : ITelemetryProvider, IAsyncDisposable
    {
        public virtual ValueTask DisposeAsync()
        {
            order?.Add("telemetry");
            return ValueTask.CompletedTask;
        }

        public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(AircraftTelemetry.Unavailable(DateTimeOffset.UnixEpoch));

        public IAsyncEnumerable<AircraftTelemetry> StreamAsync(TelemetryStreamOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private class Degradations(List<string> order) : IDegradationProvider, IAsyncDisposable
    {
        public virtual ValueTask DisposeAsync()
        {
            order.Add("degradations");
            return ValueTask.CompletedTask;
        }

        public Task<DegradationState> GetStateAsync(DegradationKey key, CancellationToken cancellationToken = default) => Task.FromResult(DegradationState.Normal);

        public Task<DegradationCommandResult> ApplyAsync(DegradationKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DegradationCommandResult(DegradationCommandStatus.Succeeded, DegradationState.Applied));

        public Task<DegradationCommandResult> RestoreAsync(DegradationKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DegradationCommandResult(DegradationCommandStatus.Succeeded, DegradationState.Normal));
    }

    private sealed class Both(List<string> order) : Telemetry(order), IDegradationProvider
    {
        private readonly List<string> _order = order;

        public override ValueTask DisposeAsync()
        {
            _order.Add("both");
            return ValueTask.CompletedTask;
        }

        public Task<DegradationState> GetStateAsync(DegradationKey key, CancellationToken cancellationToken = default) => Task.FromResult(DegradationState.Normal);

        public Task<DegradationCommandResult> ApplyAsync(DegradationKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DegradationCommandResult(DegradationCommandStatus.Succeeded, DegradationState.Applied));

        public Task<DegradationCommandResult> RestoreAsync(DegradationKey key, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DegradationCommandResult(DegradationCommandStatus.Succeeded, DegradationState.Normal));
    }
}
