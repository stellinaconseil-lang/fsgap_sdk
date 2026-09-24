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
