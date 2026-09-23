using System.Reflection;
using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Tests;

public class FailureCatalogTests
{
    private static readonly FailureKey EngineFire = FailureKey.Parse("engine.fire");
    private static readonly FailureKey AdfFailure = FailureKey.Parse("navigation.adf");

    [Fact]
    public void A_provider_defines_its_own_keys()
    {
        // Keys are defined by the provider building the catalog, not by FSGAP.Abstractions.
        var catalog = new FailureCatalog(
        [
            new FailureDefinition { Key = FailureKey.Parse("test-provider.widget.jammed"), DisplayName = "Widget jammed" },
            new FailureDefinition { Key = EngineFire, DisplayName = "Engine fire", Category = FailureCategory.Fire },
        ]);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.Contains(FailureKey.Parse("test-provider.widget.jammed")));
        Assert.True(catalog.TryGet(EngineFire, out var definition));
        Assert.Equal(FailureCategory.Fire, definition.Category);
        Assert.False(catalog.Contains(AdfFailure));
        Assert.False(catalog.TryGet(AdfFailure, out _));
    }

    [Fact]
    public void Abstractions_hard_code_no_failure_key()
    {
        var hardCodedKeys = typeof(FailureKey).Assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Public | BindingFlags.Static))
            .Where(member => member switch
            {
                FieldInfo field => field.FieldType == typeof(FailureKey),
                PropertyInfo property => property.PropertyType == typeof(FailureKey),
                _ => false,
            })
            .Select(member => $"{member.DeclaringType!.Name}.{member.Name}");

        Assert.Empty(hardCodedKeys);
    }

    [Fact]
    public void Catalog_preserves_definition_order()
    {
        var catalog = new FailureCatalog(
        [
            new FailureDefinition { Key = AdfFailure, DisplayName = "ADF" },
            new FailureDefinition { Key = EngineFire, DisplayName = "Engine fire" },
        ]);

        Assert.Equal([AdfFailure, EngineFire], catalog.Select(d => d.Key));
    }

    [Fact]
    public void Duplicate_keys_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FailureCatalog(
        [
            new FailureDefinition { Key = EngineFire, DisplayName = "Engine fire" },
            new FailureDefinition { Key = FailureKey.Parse("engine.fire"), DisplayName = "Same key again" },
        ]));
    }

    [Fact]
    public void Null_definitions_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FailureCatalog([null!]));
        Assert.Throws<ArgumentNullException>(() => new FailureCatalog(null!));
    }

    [Fact]
    public void Empty_catalog_contains_nothing()
    {
        Assert.Empty(FailureCatalog.Empty);
        Assert.False(FailureCatalog.Empty.Contains(EngineFire));
    }

    [Fact]
    public void Definition_defaults_are_conservative()
    {
        var definition = new FailureDefinition { Key = AdfFailure, DisplayName = "ADF" };

        Assert.Equal(FailureOperations.None, definition.Operations);
        Assert.Equal(FailureCategory.Other, definition.Category);
        Assert.Equal([FailureTarget.Aircraft], definition.SupportedTargets);
        Assert.True(definition.Supports(FailureTarget.Aircraft));
        Assert.False(definition.Supports(FailureTarget.Engine(1)));
    }

    [Fact]
    public void Two_failures_of_the_same_category_keep_distinct_keys()
    {
        var catalog = new FailureCatalog(
        [
            new FailureDefinition { Key = FailureKey.Parse("hydraulic.pump.blue"), DisplayName = "Blue pump", Category = FailureCategory.Hydraulic },
            new FailureDefinition { Key = FailureKey.Parse("hydraulic.leak.blue"), DisplayName = "Blue leak", Category = FailureCategory.Hydraulic },
        ]);

        Assert.Equal(2, catalog.Count);
        Assert.Equal(2, catalog.Select(d => d.Key).Distinct().Count());
    }

    [Fact]
    public void Definition_validates_and_copies_its_values()
    {
        var targets = new List<FailureTarget> { FailureTarget.Engine(1) };
        var definition = new FailureDefinition { Key = EngineFire, DisplayName = "Engine fire", SupportedTargets = targets };

        targets.Add(FailureTarget.Engine(2));

        Assert.Single(definition.SupportedTargets);
        Assert.Throws<ArgumentException>(() => new FailureDefinition { Key = EngineFire, DisplayName = " " });
        Assert.Throws<ArgumentException>(() => new FailureDefinition { Key = EngineFire, DisplayName = "x", SupportedTargets = [] });
    }

    [Fact]
    public void Unclassified_active_failures_have_no_key()
    {
        var unmapped = new AircraftFailure { Description = "Something the provider cannot map" };
        var mapped = new AircraftFailure { Key = EngineFire, Target = FailureTarget.Engine(2), Category = FailureCategory.Fire };

        Assert.False(unmapped.IsClassified);
        Assert.Equal(FailureTarget.Aircraft, unmapped.Target);
        Assert.True(mapped.IsClassified);
        Assert.Equal(EngineFire, mapped.Key);
    }

    [Fact]
    public void Command_targets_the_aircraft_unless_told_otherwise()
    {
        Assert.Equal(FailureTarget.Aircraft, new FailureCommand(AdfFailure).Target);
        Assert.Equal(FailureTarget.Engine(2), new FailureCommand(EngineFire, FailureTarget.Engine(2)).Target);
        Assert.Equal(new FailureCommand(EngineFire, FailureTarget.Engine(2)), new FailureCommand(FailureKey.Parse("engine.fire"), FailureTarget.Engine(2)));
        Assert.Throws<ArgumentNullException>(() => new FailureCommand(null!));
    }
}
