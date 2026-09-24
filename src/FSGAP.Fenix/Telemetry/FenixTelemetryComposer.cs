using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Telemetry;

namespace FSGAP.Fenix.Telemetry;

/// <summary>
/// Builds the Fenix session's snapshot: generic snapshot, then <see cref="FenixGenericTelemetryPolicy"/>, then the
/// Fenix-specific sections laid over it.
/// </summary>
/// <remarks>
/// <para>
/// This is the BLOCK 5 composition point grown by one step, not a second telemetry chain: the session's provider is
/// still a <see cref="TransformedTelemetryProvider"/> over the simulator's generic telemetry, and this is its
/// transformation.
/// </para>
/// <para>
/// <b>Priority, field by field where both sources exist:</b> a Fenix value that is not Unavailable wins; otherwise
/// the generic value (after the mask) stays; otherwise Unavailable. Sections only Fenix feeds (inertial
/// references, fuel pumps, hydraulic systems) are simply replaced by the Fenix sections. Everything else in the
/// generic snapshot is left untouched.
/// </para>
/// <para>
/// Every Fenix value carries its own receive time, and the composed snapshot goes through
/// <see cref="TelemetryFreshness"/> at the generic snapshot's timestamp, so a Fenix value that stopped arriving
/// turns Unknown exactly like a generic one.
/// </para>
/// </remarks>
internal static class FenixTelemetryComposer
{
    /// <summary>Composes the session snapshot.</summary>
    /// <param name="generic">Generic snapshot for the loaded aircraft.</param>
    /// <param name="fenix">Latest Fenix-specific state.</param>
    /// <param name="aircraftReplaced">The simulator now reports another aircraft: publish nothing from this session.</param>
    /// <param name="staleAfter">Freshness limit.</param>
    internal static AircraftTelemetry Compose(AircraftTelemetry generic, FenixSystemState fenix, bool aircraftReplaced, TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(generic);
        ArgumentNullException.ThrowIfNull(fenix);
        if (aircraftReplaced)
        {
            return AircraftTelemetry.Unavailable(generic.Timestamp);
        }

        var masked = FenixGenericTelemetryPolicy.Apply(generic);
        var composed = masked with
        {
            InertialReferences = fenix.InertialReferences,
            FuelPumps = fenix.FuelPumps,
            HydraulicSystems = fenix.HydraulicSystems,
            Engines = MergeEngines(masked.Engines, fenix.EngineFirePanels),
            Apu = masked.Apu with { FireHandlePulled = Prefer(fenix.ApuFireHandlePulled, masked.Apu.FireHandlePulled) },
        };
        return TelemetryFreshness.ExpireStaleValues(composed, staleAfter);
    }

    /// <summary>The Fenix value when it is not Unavailable, the generic one otherwise.</summary>
    internal static TelemetryValue<T> Prefer<T>(TelemetryValue<T> fenix, TelemetryValue<T> generic)
        where T : struct =>
        fenix.State == ValueState.Unavailable ? generic : fenix;

    /// <summary>
    /// Adds the fire panel to the generic engines, by index. An engine the generic groups have not reported yet
    /// still gets an entry carrying only its fire panel state.
    /// </summary>
    private static IReadOnlyList<EngineTelemetry> MergeEngines(IReadOnlyList<EngineTelemetry> generic, IReadOnlyList<EngineFirePanel> panels)
    {
        if (panels.Count == 0)
        {
            return generic;
        }

        var byIndex = generic.ToDictionary(e => e.Index);
        foreach (var panel in panels)
        {
            var engine = byIndex.TryGetValue(panel.Index, out var existing) ? existing : new EngineTelemetry { Index = panel.Index };
            byIndex[panel.Index] = engine with
            {
                FireHandlePulled = Prefer(panel.HandlePulled, engine.FireHandlePulled),
                FireWarningLit = Prefer(panel.WarningLit, engine.FireWarningLit),
            };
        }

        return byIndex.Values.OrderBy(e => e.Index).ToArray();
    }
}
