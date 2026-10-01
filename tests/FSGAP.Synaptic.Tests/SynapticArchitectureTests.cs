using System.Reflection;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Synaptic.Tests;

/// <summary>What FSGAP.Synaptic exposes and depends on (BLOCK 10B.2 scope).</summary>
public class SynapticArchitectureTests
{
    private static readonly Assembly Synaptic = typeof(SynapticAircraftProvider).Assembly;

    [Fact]
    public void Public_api_is_the_provider_and_the_catalog_only()
    {
        Assert.Equal(
            [nameof(SynapticAircraftProvider), nameof(SynapticInstalledAircraftCatalog)],
            Synaptic.GetExportedTypes().Select(t => t.Name).Order());
    }

    [Fact]
    public void Only_the_vendor_neutral_assemblies_are_referenced()
    {
        var references = Synaptic.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.Contains("FSGAP.Abstractions", references);
        Assert.Contains("FSGAP.Core", references);
        Assert.DoesNotContain(references, r => r.Contains("SimConnect", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Fenix", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void One_failure_provider_and_the_only_write_path_is_the_bounded_variable_writer_on_the_board()
    {
        var types = Synaptic.GetTypes();

        // BLOCK 11.2: one failure provider, on the same board as the degradations.
        Assert.Equal(["SynapticFailureProvider"], types.Where(t => !t.IsInterface && typeof(IFailureProvider).IsAssignableFrom(t)).Select(t => t.Name));
        // Writes go only through ISimulatorVariableWriter, held by the composition root and the one control board.
        Assert.Equal(
            ["SynapticAircraftProvider", "SynapticControlBoard"],
            types.Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Any(f => f.FieldType == typeof(ISimulatorVariableWriter) || Nullable.GetUnderlyingType(f.FieldType) == typeof(ISimulatorVariableWriter)))
                .Select(t => t.Name).Order());
        Assert.Equal(["WriteAsync"], typeof(ISimulatorVariableWriter).GetMethods().Select(m => m.Name));
        Assert.DoesNotContain(types, t => typeof(HttpClient).IsAssignableFrom(t)
            || t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Any(f => f.FieldType == typeof(HttpClient)));
        Assert.Equal(["ReadAsync"], typeof(ISimulatorVariableReader).GetMethods().Select(m => m.Name));
    }

    [Fact]
    public void No_ai_spawn_probe_event_or_efb_code_exists()
    {
        string[] forbidden = ["AICreate", "AIRemove", "TransmitClientEvent", "SetDataOnSimObject", "H:A22X", "K:", "Efb", "Wasm", "8083"];
        var names = Synaptic.GetTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => $"{t.FullName}.{m.Name}")
                .Prepend(t.FullName!))
            .ToArray();

        Assert.DoesNotContain(names, n => forbidden.Any(f => n.Contains(f, StringComparison.OrdinalIgnoreCase)));
        Assert.All(forbidden, f => Assert.False(BinaryContains(Synaptic, f), f));
    }

    [Fact]
    public void The_synaptic_variable_names_live_in_this_assembly()
    {
        // Positive control for the binary scans: the A22X names are found here, so their absence from Abstractions,
        // Core, SimConnect and Fenix (checked by those projects' tests) is meaningful.
        Assert.True(BinaryContains(Synaptic, "L:A22X L Boost Pump"));
        Assert.True(BinaryContains(Synaptic, "L:A22X APU Bleed Off"));
        Assert.True(BinaryContains(Synaptic, "L:A22X PFCC 1 Off"));
        Assert.True(BinaryContains(Synaptic, "L:A22X ACMP 3A"));
    }

    [Fact]
    public void Excluded_controls_are_never_written()
    {
        // Circuit breakers unmapped, probe heat a test pulse, fire and generator disconnect latched, RAT / oxygen /
        // emergency depressurization / alternate gear not reversible or unsafe: none is in the written registry.
        Assert.False(BinaryContains(Synaptic, "Circuit Breaker"));
        Assert.False(BinaryContains(Synaptic, "L:A22X Probe Heat"));
        var written = FSGAP.Synaptic.Systems.SynapticControls.All.Select(c => c.Variable.Name).ToArray();
        string[] never = ["Fire", "Gen Disc", "RAT", "Oxygen", "Emergency Depress", "Alternate Gear", "Circuit Breaker", "Probe Heat", "Evac", "Ditching"];
        Assert.All(never, n => Assert.DoesNotContain(written, w => w.Contains(n, StringComparison.Ordinal)));
        Assert.All(written, w => Assert.StartsWith("L:A22X ", w));

        // The public degradations are still exactly the four live-qualified controls, taken from the same registry.
        Assert.Equal(
            ["L:A22X L Gen Off", "L:A22X ACMP 3A", "L:A22X L Pack Off", "L:A22X PFCC 1 Off"],
            FSGAP.Synaptic.Degradations.SynapticDegradationControls.Controls.Select(c => c.Variable.Name));
    }

    [Fact]
    public void No_native_simulator_failure_event_is_used()
    {
        // BLOCK 11.2: Synaptic failures are A22X recipes only. The native toggles stay research evidence elsewhere.
        foreach (var native in new[] { "TOGGLE_LEFT_BRAKE_FAILURE", "TOGGLE_RIGHT_BRAKE_FAILURE", "TOGGLE_TOTAL_BRAKE_FAILURE", "TOGGLE_ENGINE1_FAILURE", "TOGGLE_ENGINE2_FAILURE", "TOGGLE_", "_FAILURE", "ENGINE_MASTER" })
        {
            Assert.False(BinaryContains(Synaptic, native), native);
        }
    }

    [Fact]
    public void The_raw_a22x_names_that_are_written_live_only_in_the_control_registry()
    {
        var registry = FSGAP.Synaptic.Systems.SynapticControls.All.Select(c => c.Variable.Name).ToArray();
        Assert.Equal(registry.Length, registry.Distinct().Count());

        // Failure recipes and degradations reference registry instances, never their own copies.
        Assert.All(FSGAP.Synaptic.Failures.SynapticFailureCatalog.Recipes.SelectMany(r => r.Controls), c => Assert.Contains(c, FSGAP.Synaptic.Systems.SynapticControls.All));
        Assert.All(FSGAP.Synaptic.Degradations.SynapticDegradationControls.Controls, d => Assert.Contains(d.Control, FSGAP.Synaptic.Systems.SynapticControls.All));
    }

    private static bool BinaryContains(Assembly assembly, string text)
    {
        var bytes = File.ReadAllBytes(assembly.Location);
        return bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(text)) >= 0
            || bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(text)) >= 0;
    }
}
