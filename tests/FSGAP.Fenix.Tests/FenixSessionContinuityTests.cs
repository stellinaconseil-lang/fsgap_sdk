using FSGAP.Abstractions;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Configuration;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Variables;

namespace FSGAP.Fenix.Tests;

/// <summary>
/// BLOCK 10B.3B: the simulator's ATC ID changes several times right after a load (G-EUYY → I-RQUV → G-EUYY seen live on
/// a Fenix). The same loaded Fenix must keep its session — polling, failure provider — while its identity follows
/// the Fenix registration rules; a different aircraft must still end it.
/// </summary>
public class FenixSessionContinuityTests
{
    private static AircraftDescriptor A320(string? atcId) =>
        new() { Title = "FenixA320 CFM WF SL", LiveryFolder = "BAW-G-EUYY-30DB", Livery = "British Airways", Registration = atcId };

    private static readonly AircraftDescriptor A321 = new() { Title = "FenixA321 IAE WF SC", LiveryFolder = "AEE-SX-DNH-7F2F", Registration = "SX-DNH" };

    private sealed class Rig
    {
        public Rig(AircraftDescriptor loaded)
        {
            Detector = new FakeDetector(loaded);
            Provider = new FenixAircraftProvider(
                timeProvider: Clock,
                genericTelemetry: new StampedGenericTelemetry(Clock),
                simulatorVariables: Reader,
                aircraftDetector: Detector,
                telemetryOptions: new TelemetryOptions(),
                fenixOptions: new FenixOptions(),
                efbHttpClient: new HttpClient(Efb));
        }

        public CountingClock Clock { get; } = new();

        public FakeVariableReader Reader { get; } = new FakeVariableReader().WithNominalCockpit();

        public FakeDetector Detector { get; }

        public FakeEfb Efb { get; } = new();

        public FenixAircraftProvider Provider { get; }

        public int CockpitReads => Reader.ReadsOf(FenixVariables.Cockpit);

        public async Task SecondsAsync(int seconds)
        {
            for (var i = 0; i < seconds; i++)
            {
                Clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(25);
            }
        }
    }

    [Fact]
    public async Task Atc_id_churn_keeps_the_fenix_session_and_its_identity_follows_the_atc_id()
    {
        // No installed-livery catalog: the Fenix registration is the ATC ID (existing Fenix precedence).
        var rig = new Rig(A320(null));
        await using var session = await rig.Provider.AttachAsync(A320(null));
        await Ready(session);
        var failures = session.Failures;
        Assert.Null(session.Identity.Registration);

        foreach (var atcId in new[] { "G-EUYY", "I-RQUV", "G-EUYY" })
        {
            rig.Detector.Current = A320(atcId);
            var before = rig.CockpitReads;
            await rig.SecondsAsync(3);
            await Wait.UntilAsync(() => rig.CockpitReads > before, $"polling continues after ATC ID {atcId}");
            var t = await session.Telemetry.GetSnapshotAsync();

            Assert.Equal(3, t.InertialReferences.Count); // not treated as a replaced aircraft
            Assert.True(t.InertialReferences[0].Mode.IsKnown);
            Assert.Equal(atcId, session.Identity.Registration);
            Assert.Same(failures, session.Failures);
        }

        Assert.Equal(0, rig.Efb.RequestCount); // no EFB traffic from a metadata update
    }

    [Fact]
    public async Task Failure_commands_stay_available_after_an_atc_id_change()
    {
        var rig = new Rig(A320("G-EUYY"));
        await using var session = await rig.Provider.AttachAsync(A320("G-EUYY"));
        rig.Detector.Current = A320("I-RQUV");

        var active = await session.Failures.GetActiveFailuresAsync();

        Assert.NotNull(active);
        Assert.Equal(1, rig.Efb.RequestCount);
    }

    [Fact]
    public async Task A_different_fenix_still_ends_the_session()
    {
        var rig = new Rig(A320("G-EUYY"));
        await using var session = await rig.Provider.AttachAsync(A320("G-EUYY"));
        await Ready(session);

        rig.Detector.Current = A321;
        await rig.SecondsAsync(2);
        var reads = rig.CockpitReads;
        await rig.SecondsAsync(4);

        Assert.Empty((await session.Telemetry.GetSnapshotAsync()).InertialReferences);
        Assert.Equal(reads, rig.CockpitReads);
        Assert.Equal("G-EUYY", session.Identity.Registration); // the replaced session keeps its last identity
    }

    [Fact]
    public async Task A_livery_change_is_a_different_aircraft()
    {
        var rig = new Rig(A320("G-EUYY"));
        await using var session = await rig.Provider.AttachAsync(A320("G-EUYY"));
        await Ready(session);

        rig.Detector.Current = A320("G-EUYY") with { LiveryFolder = "AFR-F-HTST", Livery = "Air France" };
        await rig.SecondsAsync(2);

        Assert.Empty((await session.Telemetry.GetSnapshotAsync()).InertialReferences);
    }

    private static Task Ready(IAircraftSession session) =>
        Wait.UntilAsync(() => session.Telemetry.GetSnapshotAsync().GetAwaiter().GetResult().InertialReferences.Count == 3, "Fenix state applied");
}
