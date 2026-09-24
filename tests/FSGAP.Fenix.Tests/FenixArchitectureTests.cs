using System.Reflection;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Fenix.Tests;

/// <summary>What FSGAP.Fenix exposes, and what it must not contain yet (BLOCK 4 scope).</summary>
public class FenixArchitectureTests
{
    private static readonly Assembly Fenix = typeof(FenixAircraftProvider).Assembly;

    [Fact]
    public void Public_api_is_the_provider_and_the_catalog_only()
    {
        Assert.Equal(
            [nameof(FenixAircraftProvider), nameof(FenixInstalledAircraftCatalog)],
            Fenix.GetExportedTypes().Select(t => t.Name).Order());
    }

    [Fact]
    public void No_simulator_library_is_referenced()
    {
        Assert.DoesNotContain(Fenix.GetReferencedAssemblies(), a => a.Name!.Contains("SimConnect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_telemetry_failure_lvar_or_efb_implementation_yet()
    {
        var types = Fenix.GetTypes();
        string[] forbidden = ["Lvar", "Efb", "8083", "SaveManual", "Cockpit", "FireTest"];

        Assert.DoesNotContain(types, t => typeof(IFailureProvider).IsAssignableFrom(t) || typeof(ITelemetryProvider).IsAssignableFrom(t));
        Assert.DoesNotContain(
            types.SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => $"{t.FullName}.{m.Name}").Prepend(t.FullName!)),
            name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }
}
