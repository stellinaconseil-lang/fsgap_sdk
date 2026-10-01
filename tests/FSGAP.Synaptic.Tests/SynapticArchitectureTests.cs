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
    public void No_failure_provider_and_the_only_write_path_is_the_bounded_variable_writer()
    {
        var types = Synaptic.GetTypes();

        Assert.DoesNotContain(types, t => !t.IsInterface && typeof(IFailureProvider).IsAssignableFrom(t));
        // BLOCK 11.0: writes exist only for the qualified controlled degradations, through ISimulatorVariableWriter.
        Assert.Equal(
            ["SynapticAircraftProvider", "SynapticDegradationProvider"],
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
        // BLOCK 10C.3 evidence: Hyd 1 SOV unresolved, circuit breakers unmapped, probe heat a test pulse, fire latched.
        Assert.False(BinaryContains(Synaptic, "Hyd 1 SOV"));
        Assert.False(BinaryContains(Synaptic, "Circuit Breaker"));
        Assert.False(BinaryContains(Synaptic, "L:A22X Probe Heat"));
        var written = FSGAP.Synaptic.Degradations.SynapticDegradationControls.Controls.Select(c => c.Variable.Name).ToArray();
        Assert.Equal(["L:A22X L Gen Off", "L:A22X ACMP 3A", "L:A22X L Pack Off", "L:A22X PFCC 1 Off"], written);
        Assert.DoesNotContain(written, n => n.Contains("Fire", StringComparison.Ordinal));
    }

    private static bool BinaryContains(Assembly assembly, string text)
    {
        var bytes = File.ReadAllBytes(assembly.Location);
        return bytes.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes(text)) >= 0
            || bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(text)) >= 0;
    }
}
