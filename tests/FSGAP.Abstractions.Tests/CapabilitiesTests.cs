using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Tests;

public class CapabilitiesTests
{
    [Fact]
    public void None_supports_nothing()
    {
        var capabilities = AircraftCapabilities.None;

        Assert.False(capabilities.Telemetry.Apu);
        Assert.False(capabilities.Telemetry.Engines);
        Assert.False(capabilities.Telemetry.FlightState);
        Assert.False(capabilities.Failures.CanReadActiveFailures);
        Assert.False(capabilities.Failures.CanTriggerAny);
        Assert.False(capabilities.Failures.CanClearAny);
        Assert.All(Enum.GetValues<FailureType>(), type =>
        {
            Assert.False(capabilities.Failures.CanTrigger(type));
            Assert.False(capabilities.Failures.CanClear(type));
        });
    }

    [Fact]
    public void Declared_telemetry_capability_is_exposed_and_others_stay_unsupported()
    {
        var capabilities = new AircraftCapabilities
        {
            Telemetry = new TelemetryCapabilities { Apu = true, Engines = true },
        };

        Assert.True(capabilities.Telemetry.Apu);
        Assert.True(capabilities.Telemetry.Engines);
        Assert.False(capabilities.Telemetry.InertialReferences);
        Assert.False(capabilities.Telemetry.Hydraulics);
    }

    [Fact]
    public void Failure_capabilities_are_per_type_and_per_operation()
    {
        var failures = new FailureCapabilities
        {
            CanReadActiveFailures = true,
            TriggerableTypes = new HashSet<FailureType> { FailureType.EngineFire, FailureType.ApuFire },
            ClearableTypes = new HashSet<FailureType> { FailureType.EngineFire },
        };

        Assert.True(failures.CanTrigger(FailureType.EngineFire));
        Assert.True(failures.CanTrigger(FailureType.ApuFire));
        Assert.False(failures.CanTrigger(FailureType.HydraulicSystemFailure));
        Assert.True(failures.CanClear(FailureType.EngineFire));
        Assert.False(failures.CanClear(FailureType.ApuFire));
    }

    [Fact]
    public void Failure_type_sets_are_copied_on_assignment()
    {
        var source = new HashSet<FailureType> { FailureType.EngineFire };
        var failures = new FailureCapabilities { TriggerableTypes = source };

        source.Add(FailureType.ApuFire);

        Assert.False(failures.CanTrigger(FailureType.ApuFire));
        Assert.Single(failures.TriggerableTypes);
    }
}
