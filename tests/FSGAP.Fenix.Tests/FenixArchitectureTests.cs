using System.Reflection;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Fenix.Tests;

/// <summary>What FSGAP.Fenix exposes, and what it must not contain yet (BLOCK 6 scope: no failures, no EFB).</summary>
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
    public void No_failure_efb_or_diagnostic_probe_implementation_yet()
    {
        // BLOCK 6 legitimately reads cockpit variables (so "Cockpit" left this list), composed through Core's
        // TransformedTelemetryProvider (so Fenix still implements no ITelemetryProvider of its own). Failures and the
        // EFB stay BLOCK 7; the FIRE TEST probe is a diagnostic that is not ported.
        var types = Fenix.GetTypes();
        string[] forbidden = ["Lvar", "Efb", "8083", "SaveManual", "FireTest", "Failure", "Inject"];

        Assert.DoesNotContain(types, t => typeof(IFailureProvider).IsAssignableFrom(t) || typeof(ITelemetryProvider).IsAssignableFrom(t));
        Assert.DoesNotContain(
            types.SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => $"{t.FullName}.{m.Name}").Prepend(t.FullName!)),
            name => forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_binary_contains_no_efb_endpoint_no_http_and_no_failure_call()
    {
        // BLOCK 6 failure boundary, checked on the compiled string literals rather than on names only.
        // "https://" is not listed: the assembly metadata carries the repository URL. The absence of any HTTP assembly
        // reference is the real guard against a call.
        string[] forbidden = ["8083", "127.0.0.1", "localhost", "fenix/failures", "saveManual", "http://"];

        Assert.DoesNotContain(Fenix.GetReferencedAssemblies(), a => a.Name!.Contains("Http", StringComparison.OrdinalIgnoreCase));
        Assert.All(forbidden, text => Assert.False(BinaryContains(Fenix, text), $"FSGAP.Fenix contains '{text}'."));
    }

    [Fact]
    public void The_fenix_variable_names_live_in_this_assembly()
    {
        // Positive control for the binary scans (here and in the SimConnect architecture tests): the names are
        // found where they belong, so their absence elsewhere is meaningful.
        Assert.True(BinaryContains(Fenix, "L:S_OH_NAV_IR1_MODE"));
        Assert.True(BinaryContains(Fenix, "L:S_OH_FIRE_APU_BUTTON"));
    }

    [Fact]
    public void The_variable_contract_used_by_fenix_is_read_only()
    {
        var methods = typeof(ISimulatorVariableReader).GetMethods();

        Assert.Equal(["ReadAsync"], methods.Select(m => m.Name));
    }

    /// <summary>Searches an assembly file for a string, as UTF-16 (user strings) and UTF-8 (metadata).</summary>
    internal static bool BinaryContains(Assembly assembly, string text)
    {
        var bytes = File.ReadAllBytes(assembly.Location);
        return IndexOf(bytes, System.Text.Encoding.Unicode.GetBytes(text)) >= 0
            || IndexOf(bytes, System.Text.Encoding.UTF8.GetBytes(text)) >= 0;
    }

    private static int IndexOf(byte[] haystack, byte[] needle) => haystack.AsSpan().IndexOf(needle);
}
