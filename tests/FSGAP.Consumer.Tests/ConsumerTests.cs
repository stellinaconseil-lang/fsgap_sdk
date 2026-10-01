using System.Reflection;
using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Degradations;
using FSGAP.Core.Failures;
using FSGAP.Core.Telemetry;

namespace FSGAP.Consumer.Tests;

/// <summary>
/// BLOCK 12.1: what an application (FSHANGAR, FLIPPP) writes. One reference (FSGAP), one runtime, normalized sessions;
/// no provider type, no vendor branch, no registration code.
/// </summary>
public class ConsumerTests
{
    private static FsgapRuntimeOptions Options(string name) => new()
    {
        Sdk = new FsgapOptions
        {
            ApplicationName = name,
            DataDirectory = Path.Combine(Path.GetTempPath(), "fsgap-consumer-tests", Guid.NewGuid().ToString("N")),
        },
    };

    [Fact]
    public async Task An_application_creates_one_runtime_and_needs_no_provider_registration()
    {
        await using var runtime = new FsgapRuntime(Options("ConsumerApp"));

        Assert.Contains("fenix", runtime.ProviderIds);
        Assert.Contains("synaptic", runtime.ProviderIds);
        Assert.Equal(FsgapSessionStatus.NoAircraft, runtime.SessionState.Status);
        Assert.Null(runtime.CurrentSession);
        Assert.Equal(2, runtime.InstalledAircraft.Count);
        Assert.All(runtime.InstalledAircraft, catalog => Assert.IsAssignableFrom<IInstalledAircraftCatalog>(catalog));
        Assert.NotNull(runtime.GenericTelemetry);
        Assert.NotNull(runtime.LoadedAircraft);
        Assert.NotNull(runtime.Airports);
        Assert.NotNull(runtime.SimulatorState);
    }

    [Fact]
    public async Task Session_changes_are_observed_without_knowing_the_provider()
    {
        await using var runtime = new FsgapRuntime(Options("ConsumerWatch"));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await foreach (var state in runtime.WatchSessionAsync(cancel.Token))
        {
            Assert.Equal(FsgapSessionStatus.NoAircraft, state.Status);
            break;
        }
    }

    [Fact]
    public async Task The_same_consumer_code_drives_any_session_through_capabilities()
    {
        // The same code for every aircraft: what it can do comes from the capabilities, never from the vendor.
        var report = await UseAsync(new ReadOnlySession());

        Assert.Equal("read-only: telemetry ok, 0 failure keys (0 executable), 0 degradations", report);
    }

    [Fact]
    public void This_project_references_the_top_level_package_only()
    {
        var project = File.ReadAllText(FindProjectFile());
        var references = System.Text.RegularExpressions.Regex.Matches(project, "<ProjectReference Include=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();

        Assert.Equal([@"..\..\src\FSGAP\FSGAP.csproj"], references);
        Assert.DoesNotContain("FSGAP.Fenix", project, StringComparison.Ordinal);
        Assert.DoesNotContain("FSGAP.Synaptic", project, StringComparison.Ordinal);
        Assert.DoesNotContain("FSGAP.SimConnect", project, StringComparison.Ordinal);
    }

    [Fact]
    public void The_consumer_code_compiles_without_any_provider_assembly()
    {
        var referenced = typeof(ConsumerTests).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.Contains("FSGAP", referenced);
        Assert.DoesNotContain("FSGAP.Fenix", referenced);
        Assert.DoesNotContain("FSGAP.Synaptic", referenced);
        Assert.DoesNotContain("FSGAP.SimConnect", referenced);
    }

    [Fact]
    public void The_top_level_public_api_is_the_runtime_its_options_and_its_session_state()
    {
        var fsgap = typeof(FsgapRuntime).Assembly;

        Assert.Equal(
            ["FsgapRuntime", "FsgapRuntimeOptions", "FsgapSessionState", "FsgapSessionStatus"],
            fsgap.GetExportedTypes().Select(t => t.Name).Order());
    }

    [Fact]
    public void The_top_level_public_api_exposes_no_provider_or_transport_type()
    {
        string[] forbidden = ["FSGAP.Fenix", "FSGAP.Synaptic", "FSGAP.SimConnect", "SimConnect.NET"];
        var exposed = typeof(FsgapRuntime).Assembly.GetExportedTypes()
            .SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(Signature)
            .Select(t => t.Assembly.GetName().Name!)
            .Distinct()
            .ToArray();

        Assert.DoesNotContain(exposed, name => forbidden.Contains(name));
    }

    private static async Task<string> UseAsync(IAircraftSession session)
    {
        var identity = session.Identity;
        var telemetry = await session.Telemetry.GetSnapshotAsync();
        var failures = session.Capabilities.Failures;
        var executable = failures.Catalog.Count(d => failures.CanTrigger(d.Key));
        if (failures.CanTriggerAny)
        {
            await session.Failures.TriggerAsync(new FailureCommand(failures.Catalog.First(d => failures.CanTrigger(d.Key)).Key));
        }

        if (failures.CanReadActiveFailures)
        {
            _ = await session.Failures.GetActiveFailuresAsync();
        }

        var degradations = session.Capabilities.Degradations;
        foreach (var d in degradations.Catalog.Where(d => degradations.CanReadState(d.Key)))
        {
            _ = await session.Degradations.GetStateAsync(d.Key);
        }

        return $"{identity.Model ?? "read-only"}: telemetry {(telemetry is null ? "missing" : "ok")}, {failures.Catalog.Count} failure keys ({executable} executable), {degradations.Catalog.Count} degradations";
    }

    private static IEnumerable<Type> Signature(MemberInfo member) => member switch
    {
        PropertyInfo p => Flatten(p.PropertyType),
        MethodInfo m => Flatten(m.ReturnType).Concat(m.GetParameters().SelectMany(p => Flatten(p.ParameterType))),
        ConstructorInfo c => c.GetParameters().SelectMany(p => Flatten(p.ParameterType)),
        FieldInfo f => Flatten(f.FieldType),
        _ => [],
    };

    private static IEnumerable<Type> Flatten(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten).Prepend(type.GetGenericTypeDefinition())
        : type.HasElementType ? Flatten(type.GetElementType()!)
        : [type];

    private static string FindProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FSGAP.Consumer.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new FileNotFoundException("FSGAP.Consumer.Tests.csproj"), "FSGAP.Consumer.Tests.csproj");
    }

    /// <summary>An application-side session double (no simulator): the normalized surface only.</summary>
    private sealed class ReadOnlySession : IAircraftSession
    {
        public string ProviderId => "any";

        public AircraftIdentity Identity { get; } = new() { Model = "read-only" };

        public AircraftCapabilities Capabilities => AircraftCapabilities.None;

        public ITelemetryProvider Telemetry { get; } = new UnavailableTelemetryProvider();

        public IFailureProvider Failures => UnsupportedFailureProvider.Instance;

        public IDegradationProvider Degradations => UnsupportedDegradationProvider.Instance;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
