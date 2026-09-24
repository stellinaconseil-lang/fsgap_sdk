using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Core.Telemetry;

/// <summary>
/// A telemetry provider that applies a pure transformation to every snapshot of another provider. This is how an
/// aircraft integration composes on top of the simulator's generic telemetry: mask the generic values it knows to be
/// wrong for its aircraft (and, later, overlay its own), without the simulator transport knowing the aircraft exists.
/// </summary>
/// <remarks>
/// <para>
/// The transformation receives an immutable snapshot and must return a new one; it runs on the consumer's side of
/// the stream, once per delivered snapshot, so a slow transformation slows only its own consumer.
/// </para>
/// <para>
/// Not disposable and owns nothing: the source provider's lifetime is the source's owner's business.
/// </para>
/// </remarks>
public sealed class TransformedTelemetryProvider : ITelemetryProvider
{
    private readonly ITelemetryProvider _source;
    private readonly Func<AircraftTelemetry, AircraftTelemetry> _transform;

    /// <summary>Creates the provider.</summary>
    /// <param name="source">Provider whose snapshots are transformed.</param>
    /// <param name="transform">Pure function from one snapshot to another.</param>
    public TransformedTelemetryProvider(ITelemetryProvider source, Func<AircraftTelemetry, AircraftTelemetry> transform)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(transform);
        _source = source;
        _transform = transform;
    }

    /// <inheritdoc />
    public async Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        _transform(await _source.GetSnapshotAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public IAsyncEnumerable<AircraftTelemetry> StreamAsync(
        TelemetryStreamOptions? options = null,
        CancellationToken cancellationToken = default) =>
        TransformAsync(_source.StreamAsync(options, cancellationToken), cancellationToken);

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
