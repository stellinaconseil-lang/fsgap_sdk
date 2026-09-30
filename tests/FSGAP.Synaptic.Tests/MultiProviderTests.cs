using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Failures;
using FSGAP.Core.Resolution;
using FSGAP.Fenix;

namespace FSGAP.Synaptic.Tests;

/// <summary>
/// Fenix and Synaptic side by side in one registry, on one simulator connection: runtime resolution by descriptor,
/// explicit conflicts, and switching aircraft without leaking a session, a read or a failure provider.
/// </summary>
public class MultiProviderTests
{
    private static bool IsSynaptic(SimulatorVariable v) => v.Name.StartsWith("L:A22X ", StringComparison.Ordinal);

    private static AircraftProviderRegistry Registry(params IAircraftProvider[] providers)
    {
        var registry = new AircraftProviderRegistry();
        foreach (var provider in providers)
        {
            registry.Register(provider);
        }

        return registry;
    }

    public static TheoryData<string, AircraftDescriptor, string?> Resolutions() => new()
    {
        { "Fenix A319", Descriptors.FenixA319, "fenix" },
        { "Fenix A320", Descriptors.FenixA320, "fenix" },
        { "Fenix A321", Descriptors.FenixA321, "fenix" },
        { "Synaptic A220 cabin", Descriptors.Delta, "synaptic" },
        { "Synaptic A220 no cabin", Descriptors.AirFrance, "synaptic" },
        { "iniBuilds A380", Descriptors.IniBuildsA380, null },
        { "Cessna", Descriptors.Cessna, null },
        { "Asobo passive A220", new AircraftDescriptor { Title = "Asobo PassiveAircraft A220-300", Model = "A220-300", Manufacturer = "223" }, null },
        { "FSLTL AI A220", new AircraftDescriptor { Title = "FSLTL A220-300 Delta", Model = "A220-300", Manufacturer = "223" }, null },
    };

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void Each_aircraft_resolves_to_its_provider_whatever_the_registration_order(string why, AircraftDescriptor aircraft, string? expected)
    {
        foreach (var registry in new[]
                 {
                     Registry(new FenixAircraftProvider(), new SynapticAircraftProvider()),
                     Registry(new SynapticAircraftProvider(), new FenixAircraftProvider()),
                 })
        {
            var resolution = registry.Resolve(aircraft);

            if (expected is null)
            {
                Assert.Equal(ProviderResolutionStatus.NotSupported, resolution.Status);
                Assert.Empty(resolution.Candidates);
            }
            else
            {
                Assert.True(resolution.IsResolved, why);
                Assert.Equal(expected, resolution.Selected.Provider.ProviderId);
                Assert.Single(resolution.Candidates);
            }
        }
    }

    [Fact]
    public void Two_dedicated_providers_for_the_same_aircraft_are_an_explicit_conflict_never_a_silent_pick()
    {
        foreach (var registry in new[]
                 {
                     Registry(new FenixAircraftProvider(), new SynapticAircraftProvider(), new ClaimsEverythingA220()),
                     Registry(new ClaimsEverythingA220(), new SynapticAircraftProvider(), new FenixAircraftProvider()),
                 })
        {
            var resolution = registry.Resolve(Descriptors.Delta);

            Assert.Equal(ProviderResolutionStatus.Ambiguous, resolution.Status);
            Assert.Null(resolution.Selected);
            Assert.Equal(["other-a220", "synaptic"], resolution.Candidates.Select(c => c.Provider.ProviderId).Order());
        }

        // The conflict is limited to the contested aircraft.
        Assert.Equal("fenix", Registry(new FenixAircraftProvider(), new SynapticAircraftProvider(), new ClaimsEverythingA220()).Resolve(Descriptors.FenixA320).Selected?.Provider.ProviderId);
    }

    [Fact]
    public void A_dedicated_provider_beats_a_generic_one_in_either_order()
    {
        foreach (var registry in new[]
                 {
                     Registry(new GenericForAll(), new SynapticAircraftProvider()),
                     Registry(new SynapticAircraftProvider(), new GenericForAll()),
                 })
        {
            Assert.Equal("synaptic", registry.Resolve(Descriptors.AirBaltic).Selected?.Provider.ProviderId);
        }
    }

    [Fact]
    public async Task Switching_fenix_synaptic_fenix_unsupported_fenix_disposes_each_session_and_leaks_nothing()
    {
        // One connection: the two providers share the same reader, detector and generic telemetry.
        var clock = new CountingClock();
        var reader = new FakeVariableReader();
        reader.Values["L:A22X L Boost Pump"] = 2;
        var detector = new FakeDetector(null);
        var generic = new StampedGenericTelemetry(clock);
        var efb = new CountingHandler();
        var registry = Registry(
            new FenixAircraftProvider(null, clock, null, generic, reader, detector, fenixOptions: new FenixOptions(), efbHttpClient: new HttpClient(efb)),
            new SynapticAircraftProvider(null, clock, null, generic, reader, detector));

        var host = new SessionHost(registry, detector);

        // 1. Fenix A320.
        var fenix1 = await host.LoadAsync(Descriptors.FenixA320);
        Assert.Equal("fenix", fenix1!.ProviderId);
        Assert.True(fenix1.Capabilities.Failures.CanReadActiveFailures);
        Assert.NotSame(UnsupportedFailureProvider.Instance, fenix1.Failures);
        await Wait.UntilAsync(() => reader.ReadCount > 0, "Fenix reads");
        Assert.Equal(0, reader.ReadsWhere(IsSynaptic));

        // 2. Synaptic A220: the Fenix session is disposed; no Fenix failure provider, no Fenix capability.
        var synaptic = await host.LoadAsync(Descriptors.Delta);
        Assert.Contains(fenix1, host.Disposed);
        Assert.Equal("synaptic", synaptic!.ProviderId);
        Assert.Equal("N324DU", synaptic.Identity.Registration);
        Assert.Same(FailureCapabilities.None, synaptic.Capabilities.Failures);
        Assert.Same(UnsupportedFailureProvider.Instance, synaptic.Failures);
        await Assert.ThrowsAsync<NotSupportedException>(() => synaptic.Failures.GetActiveFailuresAsync()); // CanReadActiveFailures is false
        var efbCallsDuringSynaptic = efb.Calls;
        await Wait.UntilAsync(() => reader.ReadsWhere(IsSynaptic) > 0, "Synaptic reads");
        Assert.True((await synaptic.Telemetry.GetSnapshotAsync()).FuelPumps.Count == 2);
        var fenixReadsBefore = reader.ReadCount - reader.ReadsWhere(IsSynaptic);
        clock.Advance(TimeSpan.FromSeconds(4));
        await Wait.SettleAsync();
        Assert.Equal(fenixReadsBefore, reader.ReadCount - reader.ReadsWhere(IsSynaptic)); // the disposed Fenix session reads nothing
        Assert.Equal(efbCallsDuringSynaptic, efb.Calls);

        // 3. Fenix again: the Synaptic session is disposed and never reads again.
        var fenix2 = await host.LoadAsync(Descriptors.FenixA321);
        Assert.Contains(synaptic, host.Disposed);
        Assert.Equal("fenix", fenix2!.ProviderId);
        Assert.Equal("F-GMZC", fenix2.Identity.Registration);
        Assert.Null(fenix2.Identity.RegistrationSource); // Fenix does not state one: nothing leaked from the A220
        var synapticReads = reader.ReadsWhere(IsSynaptic);
        clock.Advance(TimeSpan.FromSeconds(10));
        await Wait.SettleAsync();
        Assert.Equal(synapticReads, reader.ReadsWhere(IsSynaptic));
        Assert.DoesNotContain((await fenix2.Telemetry.GetSnapshotAsync()).FuelPumps, p => p.Id is "left" or "right"); // Fenix pumps only, no A220 overlay

        // 4. Unsupported aircraft: no session at all.
        Assert.Null(await host.LoadAsync(Descriptors.IniBuildsA380));
        Assert.Contains(fenix2, host.Disposed);
        var readsWithoutSession = reader.ReadCount;
        clock.Advance(TimeSpan.FromSeconds(10));
        await Wait.SettleAsync();
        Assert.Equal(readsWithoutSession, reader.ReadCount);

        // 5. Fenix once more.
        var fenix3 = await host.LoadAsync(Descriptors.FenixA319);
        Assert.Equal("fenix", fenix3!.ProviderId);
        Assert.True(fenix3.Capabilities.Failures.CanReadActiveFailures);
        Assert.Equal(synapticReads, reader.ReadsWhere(IsSynaptic));

        await host.DisposeAsync();
        Assert.Equal(4, host.Disposed.Count);
        Assert.Equal(host.Disposed.Count, host.Disposed.Distinct().Count());
    }

    /// <summary>Minimal host loop: one session at a time, disposed before the next aircraft is attached.</summary>
    private sealed class SessionHost(AircraftProviderRegistry registry, FakeDetector detector) : IAsyncDisposable
    {
        private IAircraftSession? _current;

        public List<IAircraftSession> Disposed { get; } = [];

        public async Task<IAircraftSession?> LoadAsync(AircraftDescriptor aircraft)
        {
            detector.Current = aircraft;
            await DisposeCurrentAsync();
            var resolution = registry.Resolve(aircraft);
            _current = resolution.IsResolved ? await resolution.Selected.Provider.AttachAsync(aircraft) : null;
            return _current;
        }

        public ValueTask DisposeAsync() => new(DisposeCurrentAsync());

        private async Task DisposeCurrentAsync()
        {
            if (_current is { } session)
            {
                _current = null;
                await session.DisposeAsync();
                Disposed.Add(session);
            }
        }
    }

    /// <summary>A hypothetical second A220 provider that also claims a dedicated match.</summary>
    private sealed class ClaimsEverythingA220 : IAircraftProvider
    {
        public string ProviderId => "other-a220";

        public AircraftMatch Match(AircraftDescriptor aircraft) =>
            aircraft.Model == "A220-300"
                ? AircraftMatch.Supported(new AircraftIdentity { Developer = "Other", Manufacturer = "Airbus", Family = "A220", Model = "A220-300" }, MatchSpecificity.Dedicated)
                : AircraftMatch.NotSupported;

        public Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A generic provider accepting any aircraft.</summary>
    private sealed class GenericForAll : IAircraftProvider
    {
        public string ProviderId => "generic";

        public AircraftMatch Match(AircraftDescriptor aircraft) =>
            AircraftMatch.Supported(new AircraftIdentity { Developer = "Any", Manufacturer = "Any", Family = "Any", Model = "Any" }, MatchSpecificity.Generic);

        public Task<IAircraftSession> AttachAsync(AircraftDescriptor aircraft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>Counts EFB requests and answers an empty failure list.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }
}
