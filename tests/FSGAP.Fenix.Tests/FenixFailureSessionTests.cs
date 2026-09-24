using System.Reflection;
using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Sessions;
using FSGAP.Fenix.Failures;

namespace FSGAP.Fenix.Tests;

/// <summary>Fenix failures as a session exposes them: which aircraft, capabilities, lifecycle, isolation.</summary>
public class FenixFailureSessionTests
{
    private static readonly AircraftDescriptor A319 = new() { Title = "FenixA319 CFM WF SD", LiveryFolder = "ACA-C-GBIA-E270" };
    private static readonly AircraftDescriptor A321 = new() { Title = "FenixA321 IAE WF SC", LiveryFolder = "AEE-SX-DNH-7F2F" };
    private static readonly FailureCommand Cpc1 = new(FailureKey.Parse("air-conditioning.cpc.1"));

    private sealed class Rig
    {
        public Rig(AircraftDescriptor? loaded = null, bool withTelemetry = false)
        {
            Detector = new FakeDetector(loaded ?? A319);
            Provider = new FenixAircraftProvider(
                timeProvider: Clock,
                genericTelemetry: withTelemetry ? new StampedGenericTelemetry(Clock) : null,
                simulatorVariables: withTelemetry ? Reader : null,
                aircraftDetector: Detector,
                fenixOptions: new FenixOptions(),
                efbHttpClient: new HttpClient(Efb));
        }

        public FakeEfb Efb { get; } = new();

        public CountingClock Clock { get; } = new();

        public FakeVariableReader Reader { get; } = new FakeVariableReader().WithNominalCockpit();

        public FakeDetector Detector { get; }

        public FenixAircraftProvider Provider { get; }
    }

    [Fact]
    public async Task A_fenix_session_declares_the_normalized_catalog_and_active_failure_reading()
    {
        var rig = new Rig();

        await using var session = await rig.Provider.AttachAsync(A319);
        var failures = session.Capabilities.Failures;

        Assert.True(failures.CanReadActiveFailures);
        Assert.Equal(40, failures.Catalog.Count);
        Assert.True(failures.CanTrigger(Cpc1.Key) && failures.CanClear(Cpc1.Key));
        Assert.True(failures.CanTrigger(Cpc1));
        Assert.True(failures.CanTrigger(new FailureCommand(FailureKey.Parse("engine.1.surge"), FailureTarget.Engine(1))));
        Assert.False(failures.CanTrigger(new FailureCommand(FailureKey.Parse("engine.1.surge"))));
        Assert.False(failures.CanTrigger(FailureKey.Parse("engine.9.explodes")));
        Assert.Equal(0, rig.Efb.RequestCount); // declaring capabilities contacts nothing
    }

    [Fact]
    public async Task The_catalog_is_declared_even_while_the_efb_is_offline()
    {
        var rig = new Rig();
        rig.Efb.Unreachable = true;

        await using var session = await rig.Provider.AttachAsync(A319);

        Assert.Equal(40, session.Capabilities.Failures.Catalog.Count);
        Assert.Equal(FailureCommandStatus.Unavailable, (await session.Failures.TriggerAsync(Cpc1)).Status);
    }

    [Fact]
    public async Task Trigger_read_back_and_clear_through_a_session()
    {
        var rig = new Rig();
        await using var session = await rig.Provider.AttachAsync(A319);

        Assert.True((await session.Failures.TriggerAsync(Cpc1)).IsSuccess);
        Assert.Equal(Cpc1.Key, Assert.Single(await session.Failures.GetActiveFailuresAsync()).Key);
        Assert.True((await session.Failures.ClearAsync(Cpc1)).IsSuccess);
        Assert.Empty(await session.Failures.GetActiveFailuresAsync());
    }

    [Theory]
    [InlineData("PMDG 737-800")]
    [InlineData("Airbus A320 Neo Asobo")]
    [InlineData("iniBuilds A310-300")]
    [InlineData("Cessna Skyhawk G1000 Asobo")]
    public async Task No_efb_request_ever_leaves_for_another_aircraft(string title)
    {
        var aircraft = new AircraftDescriptor { Title = title };
        var rig = new Rig(aircraft);

        Assert.False(rig.Provider.Match(aircraft).IsSupported);
        await Assert.ThrowsAsync<NotSupportedException>(() => rig.Provider.AttachAsync(aircraft));
        await Wait.SettleAsync();

        Assert.Equal(0, rig.Efb.RequestCount);
    }

    [Fact]
    public async Task Without_fenix_options_failures_stay_unsupported_and_nothing_is_sent()
    {
        var efb = new FakeEfb();
        var provider = new FenixAircraftProvider(efbHttpClient: new HttpClient(efb));

        await using var session = await provider.AttachAsync(A319);

        Assert.Same(AircraftCapabilities.None, session.Capabilities);
        Assert.Equal(FailureCommandStatus.NotSupported, (await session.Failures.TriggerAsync(Cpc1)).Status);
        await Assert.ThrowsAsync<NotSupportedException>(() => session.Failures.GetActiveFailuresAsync());
        Assert.Equal(0, efb.RequestCount);
    }

    [Fact]
    public async Task After_an_aircraft_switch_the_old_session_sends_nothing_and_the_new_one_works()
    {
        var rig = new Rig(A319);
        var old = await rig.Provider.AttachAsync(A319);

        rig.Detector.Current = A321;
        var stale = await old.Failures.TriggerAsync(Cpc1);
        await Assert.ThrowsAsync<FailuresUnavailableException>(() => old.Failures.GetActiveFailuresAsync());
        Assert.Equal(FailureCommandStatus.Unavailable, stale.Status);
        Assert.Equal(0, rig.Efb.RequestCount);

        await using var current = await rig.Provider.AttachAsync(A321);
        Assert.True((await current.Failures.TriggerAsync(Cpc1)).IsSuccess);
        await old.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => old.Failures.TriggerAsync(Cpc1));
    }

    [Fact]
    public async Task Failures_do_not_depend_on_the_simulator_connection()
    {
        // The EFB is its own transport: with the simulator disconnected (no aircraft reported), commands still go.
        var rig = new Rig();
        await using var session = await rig.Provider.AttachAsync(A319);
        rig.Detector.Current = null;

        Assert.True((await session.Failures.TriggerAsync(Cpc1)).IsSuccess);
    }

    [Fact]
    public async Task Disposing_the_session_disposes_its_failure_provider()
    {
        var rig = new Rig();
        var session = await rig.Provider.AttachAsync(A319);

        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.Failures.TriggerAsync(Cpc1));
    }

    [Fact]
    public async Task An_unavailable_efb_does_not_disturb_the_telemetry()
    {
        var rig = new Rig(withTelemetry: true);
        rig.Efb.Unreachable = true;
        await using var session = await rig.Provider.AttachAsync(A319);

        Assert.Equal(FailureCommandStatus.Unavailable, (await session.Failures.TriggerAsync(Cpc1)).Status);
        await Assert.ThrowsAsync<FailuresUnavailableException>(() => session.Failures.GetActiveFailuresAsync());
        await Wait.UntilAsync(() => session.Telemetry.GetSnapshotAsync().GetAwaiter().GetResult().InertialReferences.Count == 3, "Fenix telemetry");

        var t = await session.Telemetry.GetSnapshotAsync();
        Assert.Equal(InertialReferenceMode.Navigation, t.InertialReferences[0].Mode.Value);
        Assert.True(session.Capabilities.Telemetry.InertialReferences && session.Capabilities.Telemetry.FlightState);
        Assert.True(rig.Reader.ReadCount > 0);
    }

    [Fact]
    public async Task One_shared_http_client_serves_every_session_of_a_provider()
    {
        var rig = new Rig();
        await using var a = await rig.Provider.AttachAsync(A319);
        await using var b = await rig.Provider.AttachAsync(A319);

        await a.Failures.TriggerAsync(Cpc1);
        await b.Failures.ClearAsync(Cpc1);

        Assert.Equal(2, rig.Efb.RequestCount);
    }

    [Fact]
    public void Invalid_fenix_options_are_rejected_on_construction()
    {
        Assert.ThrowsAny<ArgumentException>(() => new FenixAircraftProvider(fenixOptions: new FenixOptions { RequestTimeout = TimeSpan.Zero }));
        Assert.ThrowsAny<ArgumentException>(() => new FenixAircraftProvider(fenixOptions: new FenixOptions { RequestTimeout = TimeSpan.FromMinutes(5) }));
        Assert.ThrowsAny<ArgumentException>(() => new FenixAircraftProvider(fenixOptions: new FenixOptions { EfbBaseAddress = new Uri("ftp://127.0.0.1/") }));
        Assert.ThrowsAny<ArgumentException>(() => new FenixAircraftProvider(fenixOptions: new FenixOptions { ConfirmationReadbacks = -1 }));
        Assert.Equal(new Uri("http://127.0.0.1:8083/"), new FenixOptions().EfbBaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(3), new FenixOptions().RequestTimeout);
    }

    [Fact]
    public async Task A_custom_efb_address_is_used()
    {
        var efb = new FakeEfb();
        var provider = new FenixAircraftProvider(
            fenixOptions: new FenixOptions { EfbBaseAddress = new Uri("http://192.0.2.10:9000") },
            efbHttpClient: new HttpClient(efb));
        await using var session = await provider.AttachAsync(A319);

        await session.Failures.GetActiveFailuresAsync();

        Assert.Single(efb.Requests);
    }

    [Fact]
    public void Abstractions_and_core_contain_no_raw_fenix_failure_id_or_efb_detail()
    {
        Assembly[] neutral = [typeof(IAircraftProvider).Assembly, typeof(AircraftSession).Assembly];
        string[] efbDetails = ["saveManual", "fenix/failures", "8083"];

        foreach (var assembly in neutral)
        {
            Assert.All(FenixFailureCatalogData.Default.Raw, raw => Assert.False(
                FenixArchitectureTests.BinaryContains(assembly, raw.Id), $"{assembly.GetName().Name} contains {raw.Id}"));
            Assert.All(efbDetails, text => Assert.False(
                FenixArchitectureTests.BinaryContains(assembly, text), $"{assembly.GetName().Name} contains {text}"));
        }
    }

    [Fact]
    public void Public_failure_models_expose_no_vendor_identifier_member()
    {
        Type[] models = [typeof(FailureDefinition), typeof(AircraftFailure), typeof(FailureCommand), typeof(FailureCommandResult), typeof(FailureCapabilities)];
        string[] vendorWords = ["Fenix", "Vendor", "Raw", "Efb", "Native"];

        var members = models.SelectMany(t => t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Select(m => $"{t.Name}.{m.Name}"));

        Assert.DoesNotContain(members, m => vendorWords.Any(w => m.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }
}
