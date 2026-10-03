using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Failures;
using FSGAP.Composition;
using FSGAP.Core.Resolution;
using FSGAP.Fenix;
using FSGAP.SimConnect;
using FSGAP.Synaptic;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Tests;

/// <summary>BLOCK 12.1: the internal composition root of the single FSGAP runtime.</summary>
public class CompositionTests
{
    [Fact]
    public async Task The_default_runtime_registers_fenix_then_synaptic_without_any_consumer_registration()
    {
        await using var rig = new RuntimeRig();

        Assert.Equal(["fenix", "synaptic"], rig.Runtime.ProviderIds);
        var registry = rig.Runtime.Sessions.GetType().GetField("_registry", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(rig.Runtime.Sessions) as AircraftProviderRegistry;
        Assert.IsType<FenixAircraftProvider>(registry!.Providers[0]);
        Assert.IsType<SynapticAircraftProvider>(registry.Providers[1]);
        Assert.Equal(FsgapComposition.BuiltInProviderIds, rig.Runtime.ProviderIds);
        Assert.Equal(2, rig.Runtime.InstalledAircraft.Count);
        Assert.IsType<FenixInstalledAircraftCatalog>(rig.Runtime.InstalledAircraft[0]);
        Assert.IsType<SynapticInstalledAircraftCatalog>(rig.Runtime.InstalledAircraft[1]);
    }

    [Fact]
    public async Task Each_aircraft_gets_the_session_of_its_provider()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();

        var fenix = await rig.LoadAsync(Aircraft.FenixA320);
        Assert.Equal((FsgapSessionStatus.Attached, "fenix"), (fenix.Status, fenix.Session!.ProviderId));

        var synaptic = await rig.LoadAsync(Aircraft.SynapticA220);
        Assert.Equal((FsgapSessionStatus.Attached, "synaptic"), (synaptic.Status, synaptic.Session!.ProviderId));

        var unsupported = await rig.LoadAsync(Aircraft.Unsupported);
        Assert.Equal(FsgapSessionStatus.NotSupported, unsupported.Status);
        Assert.Null(unsupported.Session);
        Assert.Null(rig.Runtime.CurrentSession);

        var none = await rig.LoadAsync(null);
        Assert.Equal(FsgapSessionState.None, none);
    }

    [Fact]
    public async Task Switching_aircraft_disposes_the_old_session_and_keeps_one_connection()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();
        var sessions = new List<IAircraftSession?>();

        foreach (var aircraft in new[] { Aircraft.FenixA320, Aircraft.SynapticA220, Aircraft.FenixA321, Aircraft.Unsupported, Aircraft.SynapticA220 })
        {
            sessions.Add((await rig.LoadAsync(aircraft)).Session);
        }

        Assert.Equal(["fenix", "synaptic", "fenix", null, "synaptic"], sessions.Select(s => s?.ProviderId));
        Assert.Equal(4, rig.Runtime.Sessions.SessionsOpened);
        Assert.Equal(3, rig.Runtime.Sessions.SessionsDisposed); // every replaced session, the current one still open
        Assert.Equal(4, sessions.Where(s => s is not null).Distinct().Count());
        Assert.Same(sessions[4], rig.Runtime.CurrentSession);
        Assert.Equal(1, rig.Simulator.Starts);
        Assert.Equal(2, rig.Simulator.Watchers); // the session manager and the open Synaptic session's control board; replaced sessions left nothing behind
        Assert.Empty(rig.Simulator.Writes); // switching never writes to any aircraft

        await rig.Runtime.DisposeAsync();
        Assert.Equal(4, rig.Runtime.Sessions.SessionsDisposed);
        Assert.Null(rig.Runtime.CurrentSession);
        Assert.Equal(0, rig.Simulator.Watchers);
    }

    [Fact]
    public async Task An_atc_id_change_keeps_the_same_session()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();
        var first = (await rig.LoadAsync(Aircraft.SynapticA220)).Session;

        var renamed = await rig.LoadAsync(Aircraft.SynapticA220 with { Registration = "C-FFCO" });

        Assert.Same(first, renamed.Session);
        Assert.Equal(1, rig.Runtime.Sessions.SessionsOpened);
        Assert.Equal(0, rig.Runtime.Sessions.SessionsDisposed);
    }

    [Fact]
    public async Task Capabilities_say_what_each_aircraft_supports()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();

        var fenix = (await rig.LoadAsync(Aircraft.FenixA320)).Session!;
        Assert.True(fenix.Capabilities.Failures.CanReadActiveFailures);
        Assert.True(fenix.Capabilities.Failures.CanTriggerAny);
        Assert.Same(DegradationCapabilities.None, fenix.Capabilities.Degradations);
        var fenixKeys = fenix.Capabilities.Failures.Catalog.Select(d => d.Key.Value).Order().ToArray();

        var synaptic = (await rig.LoadAsync(Aircraft.SynapticA220)).Session!;
        Assert.True(synaptic.Capabilities.Failures.CanReadActiveFailures);
        Assert.True(synaptic.Capabilities.Failures.CanTriggerAny);
        Assert.Equal(4, synaptic.Capabilities.Degradations.Catalog.Count);
        Assert.Equal(1, synaptic.Capabilities.Degradations.MaxActive);

        Assert.Equal(384, fenixKeys.Length);
        Assert.Equal(fenixKeys, synaptic.Capabilities.Failures.Catalog.Select(d => d.Key.Value).Order());
        Assert.Equal(384, fenix.Capabilities.Failures.Catalog.Count(d => fenix.Capabilities.Failures.CanTrigger(d.Key)));
        Assert.Equal(17, synaptic.Capabilities.Failures.Catalog.Count(d => synaptic.Capabilities.Failures.CanTrigger(d.Key)));
        Assert.Equal(367, synaptic.Capabilities.Failures.Catalog.Count(d => d.Operations == FailureOperations.None));
    }

    [Fact]
    public async Task Without_aircraft_commands_sessions_are_read_only()
    {
        await using var rig = new RuntimeRig(commands: false);
        await rig.Runtime.StartAsync();

        var fenix = (await rig.LoadAsync(Aircraft.FenixA320)).Session!;
        Assert.Same(FailureCapabilities.None, fenix.Capabilities.Failures);
        var synaptic = (await rig.LoadAsync(Aircraft.SynapticA220)).Session!;
        Assert.Same(FailureCapabilities.None, synaptic.Capabilities.Failures);
        Assert.Same(DegradationCapabilities.None, synaptic.Capabilities.Degradations);
    }

    [Fact]
    public async Task Two_providers_claiming_the_same_aircraft_equally_give_no_session()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(new Claimer("one"));
        registry.Register(new Claimer("two"));
        var simulator = new FakeSimulator();
        await using var manager = new AircraftSessionManager(registry, simulator, NullLogger.Instance);

        await manager.OnAircraftAsync(Aircraft.Unsupported, CancellationToken.None);

        Assert.Equal(FsgapSessionStatus.Ambiguous, manager.Current.Status);
        Assert.Null(manager.Current.Session);
        Assert.Contains("one, two", manager.Current.Detail);
    }

    [Fact]
    public async Task A_provider_failing_to_attach_gives_no_session_and_no_crash()
    {
        var registry = new AircraftProviderRegistry();
        registry.Register(new Claimer("broken", fail: true));
        await using var manager = new AircraftSessionManager(registry, new FakeSimulator(), NullLogger.Instance);

        await manager.OnAircraftAsync(Aircraft.Unsupported, CancellationToken.None);

        Assert.Equal(FsgapSessionStatus.AttachFailed, manager.Current.Status);
        Assert.Null(manager.Current.Session);
    }

    [Fact]
    public async Task The_public_runtime_routes_every_provider_through_one_simconnect_simulator()
    {
        var data = Path.Combine(Path.GetTempPath(), "fsgap-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        await using var runtime = new FsgapRuntime(new FsgapRuntimeOptions { Sdk = new FsgapOptions { ApplicationName = "OneConnection", DataDirectory = data } });

        var services = runtime.Services;
        var simulator = Assert.IsType<SimConnectSimulator>(services.Connection);
        Assert.Same(simulator, services.Reader);
        Assert.Same(simulator, services.Writer);
        Assert.Same(simulator, services.Liveries);
        Assert.Same(simulator, services.Airports);
        Assert.Same(simulator.AircraftDetector, services.Detector);
        Assert.Same(simulator.Telemetry, services.GenericTelemetry);
        Assert.Same(simulator.State, services.State);
        Assert.Equal(FsgapSessionStatus.NoAircraft, runtime.SessionState.Status); // not started: no connection attempted
    }

    [Fact]
    public void The_assembly_creates_the_simulator_in_one_place_only()
    {
        var creators = typeof(FsgapRuntime).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly))
            .Where(m => m.ReturnType == typeof(FsgapSimulatorServices) && m.Name.Contains("Simulator", StringComparison.Ordinal))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}");

        Assert.Equal(["FsgapRuntime.CreateSimulator"], creators);
    }

    private sealed class Claimer(string id, bool fail = false) : IAircraftProvider
    {
        public string ProviderId => id;

        public AircraftMatch Match(AircraftDescriptor aircraft) => AircraftMatch.Supported(new AircraftIdentity(), MatchSpecificity.Dedicated);

        public Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default) =>
            fail ? Task.FromException<IAircraftSession>(new InvalidOperationException("broken")) : throw new NotSupportedException();
    }
}
