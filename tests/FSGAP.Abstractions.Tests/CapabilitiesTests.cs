using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Tests;

public class CapabilitiesTests
{
    private static readonly FailureKey EngineFire = FailureKey.Parse("engine.fire");
    private static readonly FailureKey ApuFire = FailureKey.Parse("apu.fire");
    private static readonly FailureKey AdfFailure = FailureKey.Parse("navigation.adf");
    private static readonly FailureKey NotInCatalog = FailureKey.Parse("hydraulic.pump.blue");

    private static readonly FailureCapabilities Failures = new()
    {
        CanReadActiveFailures = true,
        Catalog = new FailureCatalog(
        [
            new FailureDefinition
            {
                Key = EngineFire,
                DisplayName = "Engine fire",
                Category = FailureCategory.Fire,
                SupportedTargets = [FailureTarget.Engine(1), FailureTarget.Engine(2)],
                Operations = FailureOperations.Trigger | FailureOperations.Clear,
            },
            new FailureDefinition { Key = ApuFire, DisplayName = "APU fire", Operations = FailureOperations.Trigger },
            new FailureDefinition { Key = AdfFailure, DisplayName = "ADF (report only)" },
        ]),
    };

    [Fact]
    public void None_supports_nothing()
    {
        var capabilities = AircraftCapabilities.None;

        Assert.False(capabilities.Telemetry.Apu);
        Assert.False(capabilities.Telemetry.Engines);
        Assert.False(capabilities.Telemetry.FlightState);
        Assert.False(capabilities.Telemetry.LandingGear);
        Assert.False(capabilities.Telemetry.Warnings);
        Assert.False(capabilities.Telemetry.Pressurization);
        Assert.False(capabilities.Telemetry.Environment);
        Assert.False(capabilities.Failures.CanReadActiveFailures);
        Assert.False(capabilities.Failures.CanTriggerAny);
        Assert.False(capabilities.Failures.CanClearAny);
        Assert.Empty(capabilities.Failures.Catalog);
        Assert.False(capabilities.Failures.CanTrigger(EngineFire));
        Assert.False(capabilities.Failures.CanClear(EngineFire));
    }

    [Fact]
    public void Declared_telemetry_capability_is_exposed_and_others_stay_unsupported()
    {
        var capabilities = new AircraftCapabilities
        {
            Telemetry = new TelemetryCapabilities { Apu = true, Engines = true, LandingGear = true },
        };

        Assert.True(capabilities.Telemetry.Apu);
        Assert.True(capabilities.Telemetry.Engines);
        Assert.True(capabilities.Telemetry.LandingGear);
        Assert.False(capabilities.Telemetry.InertialReferences);
        Assert.False(capabilities.Telemetry.Warnings);
    }

    [Fact]
    public void Failure_capabilities_answer_per_key_and_per_operation()
    {
        Assert.True(Failures.CanTrigger(EngineFire));
        Assert.True(Failures.CanClear(EngineFire));
        Assert.True(Failures.CanTrigger(ApuFire));
        Assert.False(Failures.CanClear(ApuFire));
        Assert.False(Failures.CanTrigger(AdfFailure));
        Assert.False(Failures.CanTrigger(NotInCatalog));
        Assert.True(Failures.CanTriggerAny);
        Assert.True(Failures.CanClearAny);
    }

    [Fact]
    public void Commands_are_checked_against_the_supported_targets()
    {
        Assert.True(Failures.CanTrigger(new FailureCommand(EngineFire, FailureTarget.Engine(2))));
        Assert.False(Failures.CanTrigger(new FailureCommand(EngineFire, FailureTarget.Engine(3))));
        Assert.False(Failures.CanTrigger(new FailureCommand(EngineFire)));
        Assert.True(Failures.CanTrigger(new FailureCommand(ApuFire)));
        Assert.False(Failures.CanClear(new FailureCommand(ApuFire)));
        Assert.False(Failures.CanTrigger(new FailureCommand(NotInCatalog)));
    }

    [Fact]
    public void Catalog_lists_known_failures_even_when_not_commandable()
    {
        Assert.True(Failures.Catalog.TryGet(AdfFailure, out var adf));
        Assert.Equal(FailureOperations.None, adf.Operations);
    }
}
