using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Telemetry;

namespace FSGAP.Synaptic.Telemetry;

/// <summary>
/// Builds a Synaptic session's snapshot: generic snapshot, then <see cref="SynapticGenericTelemetryPolicy"/>, then the
/// overlay values, then freshness. The same composition pattern as every FSGAP provider: the session's telemetry is a
/// Core <see cref="TransformedTelemetryProvider"/> over the simulator's generic telemetry, with this as its transformation.
/// </summary>
internal static class SynapticTelemetryComposer
{
    /// <summary>Composes the session snapshot.</summary>
    /// <param name="generic">Generic snapshot for the loaded aircraft.</param>
    /// <param name="overlay">Latest overlay state.</param>
    /// <param name="aircraftReplaced">The simulator now reports another aircraft: publish nothing from this session.</param>
    /// <param name="staleAfter">Freshness limit.</param>
    internal static AircraftTelemetry Compose(AircraftTelemetry generic, SynapticSystemState overlay, bool aircraftReplaced, TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(generic);
        ArgumentNullException.ThrowIfNull(overlay);
        if (aircraftReplaced)
        {
            return AircraftTelemetry.Unavailable(generic.Timestamp);
        }

        var masked = SynapticGenericTelemetryPolicy.Apply(generic);
        var composed = masked with
        {
            FuelPumps = overlay.FuelPumps,
            Apu = masked.Apu with
            {
                MasterSwitchOn = Prefer(overlay.ApuMasterSwitchOn, masked.Apu.MasterSwitchOn),
                BleedOn = Prefer(overlay.ApuBleedSelectedOn, masked.Apu.BleedOn),
            },
            Engines = MergeFirePushbuttons(masked.Engines, overlay.EngineFirePushbuttons),
        };
        return TelemetryFreshness.ExpireStaleValues(composed, staleAfter);
    }

    /// <summary>The overlay value when it is not Unavailable, the generic one otherwise.</summary>
    internal static TelemetryValue<T> Prefer<T>(TelemetryValue<T> overlay, TelemetryValue<T> generic)
        where T : struct =>
        overlay.State == ValueState.Unavailable ? generic : overlay;

    /// <summary>
    /// Adds the fire pushbutton state to the generic engines, by index. An engine the generic groups have not reported
    /// yet still gets an entry carrying only its pushbutton. Fire detection and fire lights stay as they are
    /// (Unavailable): a pressed pushbutton is a crew action, not a fire.
    /// </summary>
    private static IReadOnlyList<EngineTelemetry> MergeFirePushbuttons(IReadOnlyList<EngineTelemetry> generic, IReadOnlyList<(int Index, TelemetryValue<bool> Pressed)> pushbuttons)
    {
        if (pushbuttons.Count == 0)
        {
            return generic;
        }

        var byIndex = generic.ToDictionary(e => e.Index);
        foreach (var (index, pressed) in pushbuttons)
        {
            var engine = byIndex.TryGetValue(index, out var existing) ? existing : new EngineTelemetry { Index = index };
            byIndex[index] = engine with { FireHandlePulled = Prefer(pressed, engine.FireHandlePulled) };
        }

        return byIndex.Values.OrderBy(e => e.Index).ToArray();
    }
}
