using System.Text.Json;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Telemetry;
using FSGAP.Fenix.Variables;

namespace FSGAP.Fenix.Tests;

/// <summary>
/// Legacy parity: the same raw Fenix values mean the same thing in FSGAP's normalized model as in the audited
/// FSHANGAR registry (Fixtures/fenix-systems-golden.json).
/// </summary>
public class FenixSystemGoldenFixtureTests
{
    private static readonly DateTimeOffset At = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static TheoryData<string> Scenarios()
    {
        var data = new TheoryData<string>();
        foreach (var s in Load().Scenarios)
        {
            data.Add(s.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Normalized_state_says_what_the_legacy_registry_said(string name)
    {
        var s = Load().Scenarios.Single(x => x.Name == name);
        var raw = FenixVariables.Cockpit.Select(v => s.Raw.TryGetValue(v.Name, out var value) ? value : 0.0).ToArray();

        var state = FenixSystemMapper.ApplyCockpit(FenixSystemState.Empty, raw, At);

        Assert.Equal(s.Expected.Ir, state.InertialReferences.Select(i => Legacy(i.Mode.Value)));
        Assert.Equal(s.Expected.Pumps, state.FuelPumps.Select(p => p.IsOn.Value ? "ON" : "OFF"));
        Assert.Equal(
            s.Expected.Handles,
            state.EngineFirePanels.Select(p => p.HandlePulled).Append(state.ApuFireHandlePulled).Select(h => h.Value ? "PULLED" : "STOWED"));
        Assert.Equal(s.Expected.Lights, state.EngineFirePanels.Select(p => p.WarningLit.Value ? "ON" : "OFF"));
    }

    [Fact]
    public void Every_legacy_encoding_is_covered_by_the_mapper()
    {
        var legacy = Load().Legacy;

        foreach (var (raw, meaning) in legacy["selector"])
        {
            Assert.Equal(meaning, Legacy(FenixSystemMapper.IrMode(double.Parse(raw), At).Value));
        }

        foreach (var table in new[] { "pump", "light" })
        {
            foreach (var (raw, meaning) in legacy[table])
            {
                Assert.Equal(meaning, FenixSystemMapper.Discrete(double.Parse(raw), At).Value ? "ON" : "OFF");
            }
        }

        foreach (var (raw, meaning) in legacy["handle"])
        {
            Assert.Equal(meaning, FenixSystemMapper.Discrete(double.Parse(raw), At).Value ? "PULLED" : "STOWED");
        }
    }

    private static string Legacy(InertialReferenceMode mode) => mode switch
    {
        InertialReferenceMode.Off => "OFF",
        InertialReferenceMode.Navigation => "NAV",
        InertialReferenceMode.Attitude => "ATT",
        _ => mode.ToString(),
    };

    private static GoldenFile Load() =>
        JsonSerializer.Deserialize<GoldenFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "fenix-systems-golden.json")), Json)!;

    private sealed record GoldenFile(Dictionary<string, Dictionary<string, string>> Legacy, IReadOnlyList<Scenario> Scenarios);

    private sealed record Scenario(string Name, Dictionary<string, double> Raw, Expected Expected);

    private sealed record Expected(string[] Ir, string[] Pumps, string[] Handles, string[] Lights);
}
