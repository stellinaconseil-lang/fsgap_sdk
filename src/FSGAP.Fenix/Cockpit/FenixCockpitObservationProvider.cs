using FSGAP.Abstractions.Cockpit;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Fenix.Cockpit;

/// <summary>
/// Reads the Fenix cockpit observations (<see cref="FenixCockpitObservations"/>) on the simulator connection the
/// runtime already owns, through an <see cref="ISimulatorVariableReader"/>. No connection of its own, no polling loop:
/// each <see cref="GetSnapshotAsync"/> is ONE batched read, at whatever cadence the consumer calls it.
/// </summary>
/// <remarks>
/// Lifetime = the session. It is created by <c>FenixAircraftProvider.AttachAsync</c> only for a recognized Fenix
/// aircraft, so no Fenix cockpit variable is read otherwise. When another aircraft is loaded (continuity edge) or the
/// simulator is away, every key comes back <see cref="CockpitObservationState.Unknown"/> — never a stale value, and
/// the call never throws for those ordinary cases.
/// </remarks>
internal sealed class FenixCockpitObservationProvider : ICockpitObservationProvider
{
    private readonly ISimulatorVariableReader _reader;
    private readonly Func<bool> _aircraftReplaced;
    private readonly TimeProvider _time;

    internal FenixCockpitObservationProvider(ISimulatorVariableReader reader, Func<bool> aircraftReplaced, TimeProvider time)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _aircraftReplaced = aircraftReplaced ?? throw new ArgumentNullException(nameof(aircraftReplaced));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public async Task<CockpitObservationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();

        // A different aircraft is loaded: publish nothing of this Fenix session for it.
        if (_aircraftReplaced())
        {
            return UnknownSnapshot(now);
        }

        IReadOnlyList<double> values;
        try
        {
            values = await _reader.ReadAsync(FenixCockpitObservations.Variables, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Simulator away / read failed: keys are Unknown, never stale, and the caller is not disturbed.
            return UnknownSnapshot(now);
        }

        var controls = FenixCockpitObservations.Controls;
        var values2 = new Dictionary<CockpitObservationKey, CockpitObservationValue>(controls.Count);
        for (var i = 0; i < controls.Count; i++)
        {
            var control = controls[i];
            var raw = values[i];
            values2[control.Key] = control.Kind == CockpitObservationValueKind.Boolean
                ? CockpitObservationValue.Boolean(raw != 0)
                : CockpitObservationValue.Integer((long)Math.Round(raw, MidpointRounding.AwayFromZero));
        }

        return new CockpitObservationSnapshot { Timestamp = now, Values = values2 };
    }

    private static CockpitObservationSnapshot UnknownSnapshot(DateTimeOffset now)
    {
        var controls = FenixCockpitObservations.Controls;
        var values = new Dictionary<CockpitObservationKey, CockpitObservationValue>(controls.Count);
        foreach (var control in controls)
        {
            values[control.Key] = CockpitObservationValue.Unknown(control.Kind);
        }

        return new CockpitObservationSnapshot { Timestamp = now, Values = values };
    }
}
