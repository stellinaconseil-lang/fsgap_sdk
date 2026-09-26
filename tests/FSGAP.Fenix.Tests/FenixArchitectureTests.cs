using System.Reflection;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;

namespace FSGAP.Fenix.Tests;

/// <summary>What FSGAP.Fenix exposes, and where its EFB and failure code may live (BLOCK 7 scope).</summary>
public class FenixArchitectureTests
{
    private static readonly Assembly Fenix = typeof(FenixAircraftProvider).Assembly;

    [Fact]
    public void Public_api_is_the_provider_the_catalog_and_the_fenix_options_only()
    {
        // BLOCK 7 adds FenixOptions (EFB address and timeout), the one Fenix setting an integrator must be able to set.
        Assert.Equal(
            [nameof(FenixAircraftProvider), nameof(FenixInstalledAircraftCatalog), nameof(FenixOptions)],
            Fenix.GetExportedTypes().Select(t => t.Name).Order());
    }

    [Fact]
    public void No_simulator_library_is_referenced()
    {
        Assert.DoesNotContain(Fenix.GetReferencedAssemblies(), a => a.Name!.Contains("SimConnect", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Failure_and_efb_code_is_confined_to_the_failures_namespace_and_no_probe_exists()
    {
        // BLOCK 7 guard (replaces the BLOCK 6 "no failure yet" guard; tightened rather than dropped):
        // - exactly one failure provider, internal, in FSGAP.Fenix.Failures; still no ITelemetryProvider of Fenix's own;
        // - EFB-related names only in FSGAP.Fenix.Failures, FenixOptions (its settings) and FenixAircraftProvider (the
        //   composition root that creates the session's provider);
        // - still never an LVAR helper, an injector or the FIRE TEST probe.
        const string failuresNamespace = "FSGAP.Fenix.Failures";
        var types = Fenix.GetTypes();
        var failureProviders = types.Where(t => !t.IsInterface && typeof(IFailureProvider).IsAssignableFrom(t)).ToArray();

        Assert.Single(failureProviders);
        Assert.Equal(failuresNamespace, failureProviders[0].Namespace);
        Assert.False(failureProviders[0].IsVisible);
        Assert.DoesNotContain(types, t => typeof(ITelemetryProvider).IsAssignableFrom(t));

        var names = types
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Select(m => (Owner: Outermost(t), Name: $"{t.FullName}.{m.Name}"))
                .Prepend((Owner: Outermost(t), Name: t.FullName!)))
            .ToArray();
        string[] forbiddenEverywhere = ["Lvar", "FireTest", "Inject", "8083"];
        Assert.DoesNotContain(names, n => forbiddenEverywhere.Any(f => n.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));

        string[] efbWords = ["Efb", "SaveManual"];
        Type[] allowedOutsideNamespace = [typeof(FenixOptions), typeof(FenixAircraftProvider)];
        Assert.DoesNotContain(
            names,
            n => efbWords.Any(w => n.Name.Contains(w, StringComparison.OrdinalIgnoreCase))
                && n.Owner.Namespace != failuresNamespace
                && !allowedOutsideNamespace.Contains(n.Owner));
    }

    [Fact]
    public void Only_the_efb_client_and_the_composition_root_hold_an_http_client()
    {
        var holders = Fenix.GetTypes()
            .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Any(f => f.FieldType == typeof(HttpClient)))
            .Select(t => Outermost(t).Name)
            .ToHashSet();

        Assert.Subset(new HashSet<string> { "FenixEfbClient", nameof(FenixAircraftProvider) }, holders);
        Assert.Contains("FenixEfbClient", holders);
    }

    [Fact]
    public void The_only_efb_endpoints_are_the_two_audited_ones()
    {
        // Positive control, and a guard against unverified EFB endpoints (the /arrival list was never understood) and
        // against the slow "localhost" address (IPv6 first, about 2 s lost per call in the audited applications).
        Assert.True(BinaryContains(Fenix, "fenix/failures/saveManual"));
        Assert.True(BinaryContains(Fenix, "fenix/failures/manual"));
        Assert.False(BinaryContains(Fenix, "fenix/failures/arrival"));
        Assert.False(BinaryContains(Fenix, "localhost"));
    }

    private static Type Outermost(Type type) => type.DeclaringType is { } outer ? Outermost(outer) : type;

    [Fact]
    public void The_fenix_variable_names_live_in_this_assembly()
    {
        // Positive control for the binary scans (here and in the SimConnect architecture tests): the names are
        // found where they belong, so their absence elsewhere is meaningful.
        Assert.True(BinaryContains(Fenix, "L:S_OH_NAV_IR1_MODE"));
        Assert.True(BinaryContains(Fenix, "L:S_OH_FIRE_APU_BUTTON"));
    }

    [Fact]
    public void No_synaptic_a220_code_exists_in_the_fenix_provider()
    {
        // BLOCK 10A scope guard: the Synaptic A220 audit is discovery only. No Synaptic variable, detection or overlay
        // exists in any packaged assembly; the read-only harness lives in the sample.
        Assert.False(BinaryContains(Fenix, "A22X"));
        Assert.DoesNotContain(Fenix.GetTypes(), t => t.FullName!.Contains("Synaptic", StringComparison.OrdinalIgnoreCase));
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
