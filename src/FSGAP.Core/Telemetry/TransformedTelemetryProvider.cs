using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Core.Telemetry;

/// <summary>
/// A telemetry provider that applies a pure transformation to every snapshot of another provider. This is how an
/// aircraft integration composes on top of the simulator's generic telemetry: mask the generic values it knows to be
/// wrong for its aircraft and overlay its own values, without the simulator transport knowing the aircraft exists.
/// </summary>
/// <remarks>
/// <para>
/// The transformation receives an immutable snapshot and must return a new one; it runs on the consumer's side of
/// the stream, once per delivered snapshot, so a slow transformation slows only its own consumer.
/// </para>
/// <para>
/// The source provider is never disposed here: its lifetime is its owner's business. What the transformation itself
/// depends on (for example an aircraft integration's own polling) can be handed over as <c>ownedResource</c>, and is
/// disposed with this provider, which is how a session stops it when the session ends.
/// </para>
/// </remarks>
public sealed class TransformedTelemetryProvider : ITelemetryProvider, IAsyncDisposable
{
    private readonly ITelemetryProvider _source;
    private readonly Func<AircraftTelemetry, AircraftTelemetry> _transform;
    private readonly IAsyncDisposable? _ownedResource;
    private int _disposed;

    /// <summary>Creates the provider.</summary>
    /// <param name="source">Provider whose snapshots are transformed.</param>
    /// <param name="transform">Pure function from one snapshot to another.</param>
    /// <param name="ownedResource">Optional resource the transformation depends on, disposed with this provider.</param>
    public TransformedTelemetryProvider(
        ITelemetryProvider source,
        Func<AircraftTelemetry, AircraftTelemetry> transform,
        IAsyncDisposable? ownedResource = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transform);
        _source = source;
        _transform = transform;
        _ownedResource = ownedResource;
    }

    /// <inheritdoc />
    public async Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        _transform(await _source.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public IAsyncEnumerable<AircraftTelemetry> StreamAsync(
        TelemetryStreamOptions? options = null,
        CancellationToken cancellationToken = default) =>
        TransformAsync(_source.StreamAsync(options, cancellationToken), cancellationToken);

    /// <summary>Disposes the owned resource, once. The source provider is left alone.</summary>
    public ValueTask DisposeAsync() =>
        Interlocked.Exchange(ref _disposed, 1) == 0 && _ownedResource is not null
            ? _ownedResource.DisposeAsync()
            : ValueTask.CompletedTask;

    private async IAsyncEnumerable<AircraftTelemetry> TransformAsync(
        IAsyncEnumerable<AircraftTelemetry> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var snapshot in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return _transform(snapshot);
        }
    }
}
