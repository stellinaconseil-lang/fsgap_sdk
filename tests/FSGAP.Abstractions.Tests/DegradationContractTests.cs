using FSGAP.Abstractions.Capabilities;
using FSGAP.Abstractions.Degradations;
using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Abstractions.Tests;

/// <summary>BLOCK 11.0: the controlled-degradation contract, distinct from failures.</summary>
public class DegradationContractTests
{
    private static DegradationDescriptor Descriptor(string key, DegradationOperations operations) => new()
    {
        Key = DegradationKey.Parse(key),
        DisplayName = "Generator 1 forced off",
        Description = "A control forced off; not a failure.",
        Category = DegradationCategory.Electrical,
        Operations = operations,
    };

    [Theory]
    [InlineData("electrical.generator.1.forced-off")]
    [InlineData("hydraulic.system-3.electric-pump-a.forced-off")]
    [InlineData("air-conditioning.pack.1.forced-off")]
    [InlineData("flight-controls.pfcc.1.forced-off")]
    public void Degradation_keys_use_the_normalized_dotted_format(string text)
    {
        Assert.Equal(text, DegradationKey.Parse(text).Value);
        Assert.Equal(text, DegradationKey.Parse(text).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("generator")]
    [InlineData("L:A22X L Gen Off")]
    [InlineData("Electrical.Generator.1")]
    [InlineData("F_ELEC_GEN1")]
    [InlineData("electrical..generator")]
    public void Vendor_style_or_malformed_keys_are_rejected(string text)
    {
        Assert.False(DegradationKey.TryParse(text, out _));
        Assert.Throws<FormatException>(() => DegradationKey.Parse(text));
    }

    [Fact]
    public void A_degradation_key_is_a_distinct_type_from_a_failure_key()
    {
        var degradation = DegradationKey.Parse("electrical.generator.1");
        var failure = FailureKey.Parse("electrical.generator.1");

        Assert.NotEqual<object>(degradation, failure);
        Assert.Equal(DegradationKey.Parse("electrical.generator.1"), degradation);
    }

    [Fact]
    public void Descriptors_need_a_display_name_and_a_description()
    {
        Assert.Throws<ArgumentException>(() => Descriptor("a.b", DegradationOperations.None) with { DisplayName = " " });
        Assert.Throws<ArgumentException>(() => Descriptor("a.b", DegradationOperations.None) with { Description = "" });
    }

    [Fact]
    public void The_catalog_indexes_by_key_and_refuses_duplicates_and_nulls()
    {
        var catalog = new DegradationCatalog([Descriptor("a.b", DegradationOperations.Apply), Descriptor("a.c", DegradationOperations.None)]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.Contains(DegradationKey.Parse("a.b")));
        Assert.True(catalog.TryGet(DegradationKey.Parse("a.c"), out var c));
        Assert.Equal(DegradationOperations.None, c.Operations);
        Assert.Equal(["a.b", "a.c"], catalog.Select(d => d.Key.Value));
        Assert.Throws<ArgumentException>(() => new DegradationCatalog([Descriptor("a.b", 0), Descriptor("a.b", 0)]));
        Assert.Throws<ArgumentException>(() => new DegradationCatalog([null!]));
        Assert.Empty(DegradationCatalog.Empty);
    }

    [Fact]
    public void Capabilities_default_to_nothing_and_are_independent_from_failures()
    {
        Assert.Same(DegradationCapabilities.None, AircraftCapabilities.None.Degradations);
        Assert.Same(DegradationCapabilities.None, new AircraftCapabilities().Degradations);
        Assert.Empty(DegradationCapabilities.None.Catalog);
        Assert.Equal(0, DegradationCapabilities.None.MaxActive);
        Assert.False(DegradationCapabilities.None.CanApply(DegradationKey.Parse("a.b")));

        var withFailures = new AircraftCapabilities { Failures = new FailureCapabilities { CanReadActiveFailures = true } };
        Assert.Same(DegradationCapabilities.None, withFailures.Degradations);
    }

    [Fact]
    public void Capabilities_follow_each_descriptor_operations()
    {
        var capabilities = new DegradationCapabilities
        {
            Catalog = new DegradationCatalog([
                Descriptor("a.full", DegradationOperations.Apply | DegradationOperations.Restore | DegradationOperations.ReadState),
                Descriptor("a.read", DegradationOperations.ReadState),
            ]),
            MaxActive = 1,
        };

        Assert.True(capabilities.CanApply(DegradationKey.Parse("a.full")));
        Assert.True(capabilities.CanRestore(DegradationKey.Parse("a.full")));
        Assert.True(capabilities.CanReadState(DegradationKey.Parse("a.read")));
        Assert.False(capabilities.CanApply(DegradationKey.Parse("a.read")));
        Assert.False(capabilities.CanApply(DegradationKey.Parse("a.unknown")));
    }

    [Fact]
    public void Only_a_confirmed_command_is_a_success()
    {
        Assert.True(new DegradationCommandResult(DegradationCommandStatus.Succeeded, DegradationState.Applied).IsSuccess);
        Assert.All(
            [DegradationCommandStatus.NotSupported, DegradationCommandStatus.Rejected, DegradationCommandStatus.Unavailable, DegradationCommandStatus.Unconfirmed],
            s => Assert.False(new DegradationCommandResult(s, DegradationState.Unknown).IsSuccess));
    }

    [Fact]
    public void States_distinguish_what_this_session_applied_from_what_it_found()
    {
        Assert.Equal(
            ["Unknown", "Normal", "Applied", "PreExisting", "Unavailable"],
            Enum.GetNames<DegradationState>());
    }

    [Fact]
    public void The_session_exposes_failures_and_degradations_separately()
    {
        Assert.Equal(typeof(IFailureProvider), typeof(IAircraftSession).GetProperty(nameof(IAircraftSession.Failures))!.PropertyType);
        Assert.Equal(typeof(IDegradationProvider), typeof(IAircraftSession).GetProperty(nameof(IAircraftSession.Degradations))!.PropertyType);
        Assert.False(typeof(IFailureProvider).IsAssignableFrom(typeof(IDegradationProvider)));
    }

    [Fact]
    public void The_writer_contract_is_one_bounded_method()
    {
        var method = Assert.Single(typeof(ISimulatorVariableWriter).GetMethods());

        Assert.Equal("WriteAsync", method.Name);
        Assert.Equal([typeof(SimulatorVariable), typeof(double), typeof(CancellationToken)], method.GetParameters().Select(p => p.ParameterType));
        Assert.False(typeof(ISimulatorVariableReader).IsAssignableFrom(typeof(ISimulatorVariableWriter)));
    }
}
