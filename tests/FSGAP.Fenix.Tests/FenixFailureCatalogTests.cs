using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FSGAP.Abstractions.Failures;
using FSGAP.Fenix.Failures;

namespace FSGAP.Fenix.Tests;

/// <summary>The embedded Fenix failure catalog: since 0.12.0-preview.4 every raw entry has a normalized key.</summary>
public class FenixFailureCatalogTests
{
    private static readonly FenixFailureCatalogData Data = FenixFailureCatalogData.Default;

    /// <summary>The ids consumers use today (audit §7.3 and the BLOCK 7 inventory).</summary>
    private static readonly string[] UsedIdList =
    [
        // FLIPPP scenario catalog (24)
        "F_ELEC_STATIC_INVERTER", "F_FMGC_1", "F_MCDU_1_RECOVERABLE", "F_DISPLAY_DU_ECAM_LOWER", "F_NAV_ADF1", "F_ICE_AOA_STBY",
        "F_FUEL_FQI2", "F_NAV_GPS1", "F_HYD_PUMP_BLUE", "F_ELEC_DRIVE_FAILURE_L", "F_FIRE_LAVATORY_SMOKE", "F_NAV_ILS1_LOC",
        "F_PNEUMATIC_PACK_1_OVERHEAT", "F_HYD_LOW_BLUE", "F_BRAKE_WHEEL_1", "F_ICE_PITOT_FO", "F_ELEC_DRIVE_FAILURE_R",
        "F_HYD_PUMP_YELLOW", "F_HYD_LEAK_BLUE", "F_ENGINE_1_SURGE", "F_OH_FIRE_ENG1_LOOP_A", "F_GEAR_TYRE_PSI_MAIN_1",
        "F_ELEC_BUS_AC_1", "F_VIB_N1_ENG_1",

        // FSHANGAR live verification, tests and fire-probe trials
        "F_PNEUMATIC_CPC_1", "F_GEAR_TYRE_PSI_RIGHT_1", "F_PNEUMATIC_BLEED_VALVE_1", "F_ELEC_BUS_DC_1", "F_ELEC_BUS_DC_2",
        "F_ENG1_EIU", "F_FIRE_FDU1",

        // fenixhangarweb component aliases and tests
        "F_ELEC_BUS_AC_ESSENTIAL", "F_ELEC_BUS_BAT", "F_FUEL_PUMP_LEFT_1", "F_FUEL_PUMP_RIGHT_1", "F_HYD_LOW_GREEN",
        "F_HYD_LEAK_GREEN", "F_DOOR_FWD_ENTRY_LEFT", "F_DOOR_AFT_ENTRY_LEFT", "F_PNEUMATIC_PACK_1_REG_FAULT",
    ];

    public static TheoryData<string> UsedIds() => new(UsedIdList);

    [Fact]
    public void The_raw_catalog_is_the_complete_efb_list()
    {
        Assert.Equal(384, Data.Raw.Count);
        Assert.Equal(384, Data.Raw.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(19, Data.Raw.Select(r => r.Ata).Distinct().Count());
        Assert.All(Data.Raw, r => Assert.False(string.IsNullOrWhiteSpace(r.Title)));
        Assert.Equal(2, Data.Raw.Count(r => !r.Id.StartsWith("F_", StringComparison.Ordinal))); // the two B_INT_SFCDC ids
    }

    [Fact]
    public void Every_raw_failure_is_normalized()
    {
        Assert.Equal(384, Data.Mapped.Count);
        Assert.Equal(384, Data.Catalog.Count);
        Assert.Equal(Data.Raw.Select(r => r.Id).Order(StringComparer.Ordinal), Data.Mapped.Select(m => m.Raw.Id).Order(StringComparer.Ordinal));
        Assert.Equal(384, Data.Mapped.Select(m => m.Definition.Key).Distinct().Count());
        Assert.Subset(Data.Mapped.Select(m => m.Raw.Id).ToHashSet(), UsedIdList.ToHashSet());
    }

    [Fact]
    public void The_original_40_mappings_are_unchanged()
    {
        // 0.12.0-preview.3 mapping, verbatim: raw id -> key, display name and target are a compatibility gate.
        using var original = JsonDocument.Parse(File.ReadAllText(Contract("fenix-failure-mapping.original-40.json")));
        var entries = original.RootElement.GetProperty("failures").EnumerateArray().ToArray();

        Assert.Equal(40, entries.Length);
        Assert.Equal(UsedIdList.Order(StringComparer.Ordinal), entries.Select(e => e.GetProperty("fenixId").GetString()!).Order(StringComparer.Ordinal));
        foreach (var entry in entries)
        {
            Assert.True(Data.TryGetMapped(entry.GetProperty("fenixId").GetString()!, out var mapped));
            var definition = mapped.Definition;
            Assert.Equal(entry.GetProperty("key").GetString(), definition.Key.Value);
            Assert.Equal(entry.GetProperty("displayName").GetString(), definition.DisplayName);
            Assert.Equal(FenixFailureCatalogData.CategoryOf(mapped.Raw.Ata), definition.Category);
            Assert.Equal(FailureOperations.Trigger | FailureOperations.Clear, definition.Operations);
            var target = entry.GetProperty("target");
            var kind = target.GetProperty("kind").GetString();
            var expected = kind switch
            {
                "aircraft" => FailureTarget.Aircraft,
                "engine" => FailureTarget.Engine(target.GetProperty("index").GetInt32()),
                "fuelPump" => FailureTarget.FuelPump(target.GetProperty("id").GetString()!),
                "hydraulicSystem" => FailureTarget.HydraulicSystem(target.GetProperty("id").GetString()!),
                "electricalBus" => FailureTarget.ElectricalBus(target.GetProperty("id").GetString()!),
                _ => throw new InvalidOperationException(kind),
            };
            Assert.Equal([expected], definition.SupportedTargets);
        }
    }

    [Fact]
    public void The_failure_key_contract_snapshot_is_exactly_the_catalog()
    {
        // Renaming or removing a key fails here; adding one is a deliberate edit of the snapshot.
        var snapshot = File.ReadAllLines(Contract("failure-keys.txt")).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray();

        Assert.Equal(384, snapshot.Length);
        Assert.Equal(snapshot.Order(StringComparer.Ordinal), Data.Catalog.Select(d => d.Key.Value).Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(UsedIds))]
    public void Every_id_in_use_has_a_key(string fenixId)
    {
        Assert.True(Data.TryGetMapped(fenixId, out var mapped));
        Assert.True(Data.Catalog.Contains(mapped.Definition.Key));
    }

    [Fact]
    public void Keys_and_raw_ids_are_unique_and_one_to_one()
    {
        Assert.Equal(Data.Mapped.Count, Data.Mapped.Select(m => m.Definition.Key).Distinct().Count());
        Assert.Equal(Data.Mapped.Count, Data.Mapped.Select(m => m.Raw.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Data.Mapped, m =>
        {
            Assert.True(Data.TryGetByKey(m.Definition.Key, out var byKey));
            Assert.Same(m, byKey);
        });
    }

    [Fact]
    public void Keys_are_semantic_never_a_vendor_id_in_disguise()
    {
        Assert.All(Data.Mapped, m =>
        {
            var key = m.Definition.Key.Value;
            Assert.True(FailureKey.TryParse(key, out _));
            Assert.DoesNotContain(m.Raw.Id.ToLowerInvariant(), key, StringComparison.Ordinal);
            Assert.DoesNotContain(m.Raw.Id.ToLowerInvariant().Replace('_', '-'), key, StringComparison.Ordinal);
            Assert.DoesNotContain("fenix", key, StringComparison.Ordinal);
            Assert.False(key.StartsWith("f-", StringComparison.Ordinal) || key.StartsWith("f.", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Every_definition_has_a_display_name_a_category_one_target_and_both_operations()
    {
        Assert.All(Data.Mapped, m =>
        {
            var d = m.Definition;
            Assert.False(string.IsNullOrWhiteSpace(d.DisplayName));
            Assert.Equal(FenixFailureCatalogData.CategoryOf(m.Raw.Ata), d.Category);
            Assert.True(d.Category != FailureCategory.Other || m.Raw.Ata == 46, d.Key.Value); // ATA 46 (information systems) has no category
            Assert.Single(d.SupportedTargets);
            Assert.Equal(FailureOperations.Trigger | FailureOperations.Clear, d.Operations);
        });
    }

    [Fact]
    public void Typed_targets_follow_the_key_policy()
    {
        Assert.All(Data.Mapped, m =>
        {
            var key = m.Definition.Key.Value;
            var expected =
                key.StartsWith("electrical.bus.", StringComparison.Ordinal) ? FailureTargetKind.ElectricalBus
                : key.StartsWith("fuel.pump.", StringComparison.Ordinal) ? FailureTargetKind.FuelPump
                : Regex.IsMatch(key, @"^hydraulic\.(green|blue|yellow)\.") ? FailureTargetKind.HydraulicSystem
                : Regex.IsMatch(key, @"^navigation\.ir\.\d\.") ? FailureTargetKind.InertialReference
                : Regex.IsMatch(key, @"^(engine|fire\.engine)\.\d\.") ? FailureTargetKind.Engine
                : key.StartsWith("apu.", StringComparison.Ordinal) || key.StartsWith("fire.apu.", StringComparison.Ordinal) || key == "electrical.generator.apu" ? FailureTargetKind.Apu
                : FailureTargetKind.Aircraft;
            Assert.True(expected == m.Target.Kind, $"{key}: {m.Target}");
        });
        Assert.Equal(FailureTarget.Engine(2), Target("engine.2.reverser.unlocked"));
        Assert.Equal(FailureTarget.InertialReference(3), Target("navigation.ir.3.alignment"));
        Assert.Equal(FailureTarget.Apu, Target("fire.apu.loop-a"));
    }

    [Fact]
    public void Targets_reuse_the_telemetry_ids()
    {
        var targets = Data.Mapped.Select(m => m.Target).ToArray();

        Assert.Contains(FailureTarget.FuelPump("left-1"), targets);
        Assert.Contains(FailureTarget.FuelPump("right-1"), targets);
        Assert.Contains(FailureTarget.HydraulicSystem("green"), targets);
        Assert.Contains(FailureTarget.HydraulicSystem("blue"), targets);
        Assert.Contains(FailureTarget.HydraulicSystem("yellow"), targets);
        Assert.Contains(FailureTarget.Engine(1), targets);
        Assert.Contains(FailureTarget.ElectricalBus("dc-1"), targets);
        Assert.Equal(FailureTarget.Engine(1), Target("engine.1.surge"));
        Assert.Equal(FailureTarget.HydraulicSystem("blue"), Target("hydraulic.blue.leak"));
        Assert.Equal(FailureTarget.Aircraft, Target("air-conditioning.cpc.1"));
    }

    [Fact]
    public void Categories_follow_the_fenix_ata_chapter()
    {
        Assert.Equal(FailureCategory.AirConditioning, Definition("air-conditioning.cpc.1").Category);
        Assert.Equal(FailureCategory.Navigation, Definition("navigation.fmgc.1").Category);
        Assert.Equal(FailureCategory.Engine, Definition("engine.1.surge").Category);
        Assert.Equal(FailureCategory.Fire, Definition("fire.engine.1.loop-a").Category);
        Assert.Equal(FailureCategory.Engine, FenixFailureCatalogData.CategoryOf(80));
        Assert.Equal(FailureCategory.Other, FenixFailureCatalogData.CategoryOf(46));
    }

    [Fact]
    public void No_public_definition_carries_a_raw_fenix_id()
    {
        var serialized = JsonSerializer.Serialize(Data.Catalog.ToArray());

        Assert.All(Data.Raw, raw => Assert.DoesNotContain(raw.Id, serialized, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void An_inconsistent_resource_is_rejected_at_load()
    {
        const string raw = """[{"id":"F_A","title":"A","category":"ATA 21 - Air / G"},{"id":"F_B","title":"B","category":"ATA 21 - Air / G"}]""";

        Assert.Throws<InvalidOperationException>(() => Parse("""[{"id":"F_A","title":"A","category":"ATA 21 - Air / G"},{"id":"F_A","title":"A","category":"ATA 21 - Air / G"}]""", Mapping()));
        Assert.Throws<InvalidOperationException>(() => Parse(raw, Mapping(("F_MISSING", "air.a"))));
        Assert.Throws<InvalidOperationException>(() => Parse(raw, Mapping(("F_A", "air.a"), ("F_B", "air.a"))));
        Assert.Throws<InvalidOperationException>(() => Parse(raw, Mapping(("F_A", "air.a"), ("F_A", "air.b"))));
        Assert.Throws<InvalidOperationException>(() => Parse(raw, Mapping(("F_A", "F_A"))));
        Assert.Throws<InvalidOperationException>(() => Parse(raw, MappingWithKinds(("F_A", "air.a", "wing"))));
        Assert.Throws<InvalidOperationException>(() => Parse("""[{"id":"F_A","title":"A","category":"no chapter"}]""", Mapping()));
        Assert.Single(Parse(raw, Mapping(("F_A", "air.a"))).Mapped);
    }

    private static FailureTarget Target(string key) => Definition(key).SupportedTargets[0];

    private static string Contract(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FSGAP.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("FSGAP.sln"), "tests", "FSGAP.Fenix.Tests", "Contract", file);
    }

    private static FailureDefinition Definition(string key)
    {
        Assert.True(Data.Catalog.TryGet(FailureKey.Parse(key), out var definition));
        return definition;
    }

    private static string MappingWithKinds(params (string Id, string Key, string Kind)[] entries) =>
        JsonSerializer.Serialize(new { failures = entries.Select(e => new { fenixId = e.Id, key = e.Key, displayName = "x", target = new { kind = e.Kind } }) });

    private static string Mapping(params (string Id, string Key)[] entries) =>
        MappingWithKinds(entries.Select(e => (e.Id, e.Key, "aircraft")).ToArray());

    private static FenixFailureCatalogData Parse(string raw, string mapping) =>
        FenixFailureCatalogData.Parse(new MemoryStream(Encoding.UTF8.GetBytes(raw)), new MemoryStream(Encoding.UTF8.GetBytes(mapping)));
}
