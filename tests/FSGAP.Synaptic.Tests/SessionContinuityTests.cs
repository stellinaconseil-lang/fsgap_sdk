using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Core.Resolution;
using FSGAP.Core.Sessions;
using FSGAP.Fenix;

namespace FSGAP.Synaptic.Tests;

/// <summary>
/// BLOCK 10B.3B: ATC ID changes after a load must not replace a session; real aircraft changes still must. Replays
/// the 15 descriptor emissions of the BLOCK 10B.3 live run through a continuity-aware host.
/// </summary>
public sealed class SessionContinuityTests : IDisposable
{
    private readonly TempDirectory _data = new();

    public void Dispose() => _data.Dispose();

    private static bool IsSynaptic(SimulatorVariable v) => v.Name.StartsWith("L:A22X ", StringComparison.Ordinal);

    [Fact]
    public async Task Atc_id_churn_keeps_the_synaptic_session_the_overlay_and_the_derived_registration()
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        reader.Values["L:A22X L Boost Pump"] = 2;
        var detector = new FakeDetector(Descriptors.AirFrance);
        var catalog = new SynapticInstalledAircraftCatalog(new FsgapOptions { ApplicationName = "tests", DataDirectory = _data.Path }, new FakeLiveryService(), packageRoots: []);
        var provider = new SynapticAircraftProvider(catalog, clock, null, new StampedGenericTelemetry(clock), reader, detector);

        await using var session = await provider.AttachAsync(Descriptors.AirFrance);
        await Wait.UntilAsync(() => reader.ReadCount == 1 && clock.TimersCreated == 1, "the first read");

        var cycle = 1;
        foreach (var atcId in new[] { "C-FFCO", null, "I-OVTU", "I-FZRQ", null })
        {
            detector.Current = Descriptors.AirFrance with { Registration = atcId };
            clock.Advance(TimeSpan.FromSeconds(2));
            cycle++;
            var expected = cycle;
            await Wait.UntilAsync(() => reader.ReadsWhere(IsSynaptic) == expected && clock.TimersCreated == expected, $"read {expected} after ATC ID '{atcId}'");
            var t = await session.Telemetry.GetSnapshotAsync();

            Assert.Equal(FuelPumpMode.On, t.FuelPumps[0].Mode.Value); // still published: not a replaced aircraft
            Assert.Equal("F-HZUF", session.Identity.Registration);
            Assert.Equal(RegistrationSource.Derived, session.Identity.RegistrationSource);
        }

        Assert.Equal(cycle, clock.TimersCreated); // one delay per cycle: a single polling loop, never restarted
        Assert.Equal("F-HZUF", (await catalog.FindByLiveryFolderAsync("AIR FRANCE F-HZUF"))?.Identity.Registration);
    }

    [Fact]
    public async Task A_corroborating_atc_id_may_upgrade_the_same_session_registration_to_observed()
    {
        var detector = new FakeDetector(Descriptors.AirFrance);
        var provider = new SynapticAircraftProvider(aircraftDetector: detector);
        await using var session = await provider.AttachAsync(Descriptors.AirFrance);
        Assert.Equal(RegistrationSource.Derived, session.Identity.RegistrationSource);

        detector.Current = Descriptors.AirFrance with { Registration = "F-HZUF" };

        Assert.Equal("F-HZUF", session.Identity.Registration);
        Assert.Equal(RegistrationSource.Observed, session.Identity.RegistrationSource);
    }

    [Fact]
    public async Task The_live_emission_sequence_opens_one_session_per_real_change_only()
    {
        // The 15 emissions of BLOCK 10B.3 (18:50–19:17): 7 real aircraft/livery changes, 8 ATC-ID-only updates.
        var af = Descriptors.A220("A220-300 - No Cabin", "AIR FRANCE F-HZUF", "Air France A220-300");
        var afCabin = Descriptors.A220("A220-300", "AIR FRANCE F-HZUF", "Air France A220-300");
        var baltic = Descriptors.A220("A220-300", "AIR BALTIC YL-CSM", "Air Baltic A220-300");
        var delta = Descriptors.A220("A220-300", "DELTA N324DU", "Delta A220-300");
        var fenixHouse = new AircraftDescriptor { Title = "FenixA320 CFM SL", LiveryFolder = "FNX_320_FENIX_CFM_SL", Livery = "Fenix House Colors" };
        var fenixBaw = new AircraftDescriptor { Title = "FenixA320 IAE SL", LiveryFolder = "BAW-G-EUYY-30DB", Livery = "British Airways" };
        AircraftDescriptor[] emissions =
        [
            af,
            baltic with { Registration = "C-FFCO" },
            delta with { Registration = "C-FFCO" },
            delta with { Registration = "I-OVTU" },
            delta with { Registration = "C-FFCO" },
            delta,
            fenixHouse with { Registration = "G-FENX" },
            fenixBaw with { Registration = "G-EUYY" },
            fenixBaw with { Registration = "I-RQUV" },
            fenixBaw with { Registration = "G-EUYY" },
            delta with { Registration = "C-FFCO" },
            afCabin with { Registration = "C-FFCO" },
            afCabin with { Registration = "I-FZRQ" },
            afCabin with { Registration = "C-FFCO" },
            afCabin,
        ];
        var detector = new FakeDetector(null);
        var host = new ContinuityHost(Registry(detector), detector);

        foreach (var emission in emissions)
        {
            await host.ObserveAsync(emission);
        }

        Assert.Equal(7, host.Opened.Count);
        Assert.Equal(8, host.MetadataUpdates);
        Assert.Equal(["synaptic", "synaptic", "synaptic", "fenix", "fenix", "synaptic", "synaptic"], host.Opened.Select(s => s.ProviderId));
        Assert.Equal("F-HZUF", host.Current!.Identity.Registration);
        await host.DisposeAsync();
        Assert.Equal(7, host.Disposed.Count);
    }

    [Fact]
    public async Task Real_changes_still_replace_the_session()
    {
        var a319 = new AircraftDescriptor { Title = "FenixA319 CFM WF SD", LiveryFolder = "ACA-C-GBIA-E270", Registration = "C-GBIA" };
        var a320 = new AircraftDescriptor { Title = "FenixA320 CFM WF SL", LiveryFolder = "AFR-F-HTST" };
        var a321 = new AircraftDescriptor { Title = "FenixA321 IAE WF SC", LiveryFolder = "AEE-SX-DNH-7F2F", Registration = "SX-DNH" };
        var detector = new FakeDetector(null);
        var host = new ContinuityHost(Registry(detector), detector);

        AircraftDescriptor?[] sequence = [a319, a320, a321, Descriptors.Delta, a320, Descriptors.Cessna, Descriptors.AirFrance, null, a319];
        string?[] expectedProvider = ["fenix", "fenix", "fenix", "synaptic", "fenix", null, "synaptic", null, "fenix"];
        for (var i = 0; i < sequence.Length; i++)
        {
            var previous = host.Current;
            await host.ObserveAsync(sequence[i]);

            Assert.Equal(expectedProvider[i], host.Current?.ProviderId);
            if (previous is not null)
            {
                Assert.Contains(previous, host.Disposed);
            }
        }

        Assert.Equal(7, host.Opened.Count);
        Assert.Equal(0, host.MetadataUpdates);
    }

    [Fact]
    public void Continuity_does_not_change_provider_conflicts()
    {
        var registry = Registry(new FakeDetector(null));
        registry.Register(new OtherA220());

        Assert.Equal(ProviderResolutionStatus.Ambiguous, registry.Resolve(Descriptors.Delta).Status);
        Assert.Equal(ProviderResolutionStatus.Ambiguous, registry.Resolve(Descriptors.Delta with { Registration = "I-OVTU" }).Status);
    }

    private static AircraftProviderRegistry Registry(FakeDetector detector)
    {
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        var generic = new StampedGenericTelemetry(clock);
        var registry = new AircraftProviderRegistry();
        registry.Register(new FenixAircraftProvider(null, clock, null, generic, reader, detector));
        registry.Register(new SynapticAircraftProvider(null, clock, null, generic, reader, detector));
        return registry;
    }

    /// <summary>The host rule this block introduces: same loaded aircraft → keep the session; otherwise replace it.</summary>
    private sealed class ContinuityHost(AircraftProviderRegistry registry, FakeDetector detector) : IAsyncDisposable
    {
        private AircraftDescriptor? _last;

        public IAircraftSession? Current { get; private set; }

        public List<IAircraftSession> Opened { get; } = [];

        public List<IAircraftSession> Disposed { get; } = [];

        public int MetadataUpdates { get; private set; }

        public async Task ObserveAsync(AircraftDescriptor? aircraft)
        {
            detector.Current = aircraft;
            var previous = _last;
            _last = aircraft;
            if (aircraft is not null && previous is not null && AircraftContinuity.IsSameLoadedAircraft(previous, aircraft))
            {
                MetadataUpdates++;
                return;
            }

            await DisposeCurrentAsync();
            if (aircraft is not null && registry.Resolve(aircraft) is { IsResolved: true } resolution)
            {
                Current = await resolution.Selected.Provider.AttachAsync(aircraft);
                Opened.Add(Current);
            }
        }

        public ValueTask DisposeAsync() => new(DisposeCurrentAsync());

        private async Task DisposeCurrentAsync()
        {
            if (Current is { } session)
            {
                Current = null;
                await session.DisposeAsync();
                Disposed.Add(session);
            }
        }
    }

    private sealed class OtherA220 : IAircraftProvider
    {
        public string ProviderId => "other-a220";

        public AircraftMatch Match(AircraftDescriptor aircraft) =>
            aircraft.Model == "A220-300"
                ? AircraftMatch.Supported(new AircraftIdentity { Model = "A220-300" }, MatchSpecificity.Dedicated)
                : AircraftMatch.NotSupported;

        public Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
