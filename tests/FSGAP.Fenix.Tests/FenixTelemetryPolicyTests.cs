using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Telemetry;

namespace FSGAP.Fenix.Tests;

/// <summary>The Fenix session over generic telemetry: what is kept, what is masked (BLOCK 5).</summary>
public class FenixTelemetryPolicyTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly AircraftDescriptor Fenix = new() { Title = "FenixA320 CFM WF SL", Registration = "F-HTST" };

    /// <summary>A generic snapshot as the SimConnect transport would publish it for any aircraft.</summary>
    private static AircraftTelemetry Generic() => AircraftTelemetry.Unavailable(At) with
    {
        Flight = new FlightStateTelemetry
        {
            IndicatedAirspeedKnots = TelemetryValue<double>.Known(250.0, At),
            OnGround = TelemetryValue<bool>.Known(false, At),
        },
        Warnings = new WarningsTelemetry { Overspeed = TelemetryValue<bool>.Known(false, At) },
        Engines = [new EngineTelemetry { Index = 1, N1Percent = TelemetryValue<double>.Known(82.0, At) }],
        LandingGear = new LandingGearTelemetry { HandleDown = TelemetryValue<bool>.Known(false, At) },
        FlightControls = new FlightControlsTelemetry
        {
            FlapsHandlePercent = TelemetryValue<double>.Known(0.0, At),
            SpeedBrakeDeploymentPercent = TelemetryValue<double>.Known(37.0, At),
        },
    };

    [Fact]
    public void Speed_brake_is_masked_as_unavailable_and_everything_else_generic_is_kept()
    {
        var generic = Generic();

        var fenix = FenixGenericTelemetryPolicy.Apply(generic);

        Assert.Equal(ValueState.Unavailable, fenix.FlightControls.SpeedBrakeDeploymentPercent.State);
        Assert.Equal(generic.Flight, fenix.Flight);
        Assert.Equal(generic.Warnings, fenix.Warnings);
        Assert.Same(generic.Engines, fenix.Engines);
        Assert.Equal(generic.LandingGear, fenix.LandingGear);
        Assert.Equal(generic.FlightControls.FlapsHandlePercent, fenix.FlightControls.FlapsHandlePercent);
    }

    [Fact]
    public void Apu_bleed_is_masked_until_verified_on_fenix_and_the_0_9_generic_sections_are_kept()
    {
        var generic = Generic() with
        {
            Apu = new ApuTelemetry { BleedOn = TelemetryValue<bool>.Known(true, At) },
            Pressurization = new PressurizationTelemetry { CabinAltitudeFeet = TelemetryValue<double>.Known(7000.0, At) },
            Environment = new EnvironmentTelemetry { Precipitation = TelemetryValue<PrecipitationType>.Known(PrecipitationType.Rain, At) },
            LandingGear = new LandingGearTelemetry { BrakeLeftPercent = TelemetryValue<double>.Known(60.0, At), SteeringInputPercent = TelemetryValue<double>.Known(-99.99, At) },
            FlightControls = new FlightControlsTelemetry { RudderDeflectionPercent = TelemetryValue<double>.Known(2.0, At) },
        };

        var fenix = FenixGenericTelemetryPolicy.Apply(generic);

        Assert.Equal(ValueState.Unavailable, fenix.Apu.BleedOn.State);
        Assert.Equal(generic.Pressurization, fenix.Pressurization);
        Assert.Equal(generic.Environment, fenix.Environment);
        Assert.Equal(generic.LandingGear, fenix.LandingGear);
        Assert.Equal(2.0, fenix.FlightControls.RudderDeflectionPercent.Value);
        Assert.False(FenixGenericTelemetryPolicy.GenericSections.Apu);
    }

    [Fact]
    public void Union_covers_every_telemetry_capability_flag()
    {
        // Guards against a new TelemetryCapabilities flag silently dropped by the union (0.9.0 added two).
        var flags = typeof(TelemetryCapabilities).GetProperties().Where(p => p.PropertyType == typeof(bool)).ToArray();
        Assert.NotEmpty(flags);
        foreach (var flag in flags)
        {
            var one = new TelemetryCapabilities();
            flag.SetValue(one, true);
            Assert.True((bool)flag.GetValue(FenixGenericTelemetryPolicy.Union(one, TelemetryCapabilities.None))!, flag.Name);
            Assert.True((bool)flag.GetValue(FenixGenericTelemetryPolicy.Union(TelemetryCapabilities.None, one))!, flag.Name);
        }
    }

    [Fact]
    public void The_generic_snapshot_itself_is_not_modified()
    {
        var generic = Generic();

        FenixGenericTelemetryPolicy.Apply(generic);

        Assert.Equal(37.0, generic.FlightControls.SpeedBrakeDeploymentPercent.Value);
    }

    [Fact]
    public async Task A_session_over_generic_telemetry_exposes_it_masked_and_declares_the_generic_sections()
    {
        var generic = new StubTelemetry(Generic());
        var provider = new FenixAircraftProvider(genericTelemetry: generic);

        await using var session = await provider.AttachAsync(Fenix);
        var snapshot = await session.Telemetry.GetSnapshotAsync();
        var streamed = await FirstAsync(session.Telemetry.StreamAsync());

        Assert.Equal(250.0, snapshot.Flight.IndicatedAirspeedKnots.Value);
        Assert.Equal(ValueState.Unavailable, snapshot.FlightControls.SpeedBrakeDeploymentPercent.State);
        Assert.Equal(ValueState.Unavailable, streamed.FlightControls.SpeedBrakeDeploymentPercent.State);
        Assert.Equal(37.0, (await generic.GetSnapshotAsync()).FlightControls.SpeedBrakeDeploymentPercent.Value);

        var declared = session.Capabilities.Telemetry;
        Assert.True(declared.FlightState && declared.Warnings && declared.Engines && declared.LandingGear && declared.FlightControls);
        Assert.True(declared.Pressurization && declared.Environment);
        Assert.False(declared.Apu || declared.InertialReferences || declared.FuelPumps || declared.Electrical || declared.Hydraulics || declared.Fire);
        Assert.Same(FailureCapabilities.None, session.Capabilities.Failures);
    }

    [Fact]
    public async Task Without_generic_telemetry_a_session_still_has_none()
    {
        await using var session = await new FenixAircraftProvider().AttachAsync(Fenix);

        Assert.Same(AircraftCapabilities.None, session.Capabilities);
        Assert.Equal(ValueState.Unavailable, (await session.Telemetry.GetSnapshotAsync()).Flight.IndicatedAirspeedKnots.State);
    }

    private static async Task<T> FirstAsync<T>(IAsyncEnumerable<T> source)
    {
        await foreach (var item in source)
        {
            return item;
        }

        throw new InvalidOperationException("The sequence is empty.");
    }

    private sealed class StubTelemetry(AircraftTelemetry snapshot) : ITelemetryProvider
    {
        public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(snapshot);

        public async IAsyncEnumerable<AircraftTelemetry> StreamAsync(
            TelemetryStreamOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return snapshot;
        }
    }
}
