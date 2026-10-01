using System.Reflection;
using FSGAP.SimConnect.Console;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>BLOCK 10C.3 — RESEARCH ONLY: the safety model of the Synaptic controlled-degradation probe.</summary>
public class SynapticDegradationSessionTests
{
    private static readonly DegradationConditions Parked = new(AircraftQualified: true, OnGround: true, GroundSpeedKnots: 0, ParkingBrakeSet: true, Engine1N2Percent: 67, Engine2N2Percent: 67);

    private static DegradationCandidate Gen => SynapticDegradationSession.Find("ELEC_L_GEN_OFF")!;

    private static void Apply(SynapticDegradationSession s, string id)
    {
        var c = SynapticDegradationSession.Find(id)!;
        Assert.Null(s.BeginBaseline(id, c.NormalValue));
        var (write, refusal) = s.PlanApply(id, c.NormalValue, Parked);
        Assert.Null(refusal);
        s.RecordWritten(write!);
    }

    private static void FullCycle(SynapticDegradationSession s, string id)
    {
        var c = SynapticDegradationSession.Find(id)!;
        Apply(s, id);
        var (restore, refusal) = s.PlanRestore(id, Parked);
        Assert.Null(refusal);
        s.RecordWritten(restore!);
        Assert.Null(s.CheckRestoreReadback(c, c.NormalValue));
        Assert.Null(s.Verify(id, c.NormalValue));
    }

    [Fact]
    public void The_whitelist_is_four_reviewed_documented_local_variables_with_explicit_set_values()
    {
        Assert.Equal(["ELEC_L_GEN_OFF", "HYD_ACMP_3A_OFF", "PNEU_L_PACK_OFF", "FCS_PFCC_1_OFF"], SynapticDegradationSession.Candidates.Select(c => c.Id));
        Assert.All(SynapticDegradationSession.Candidates, c =>
        {
            Assert.StartsWith("L:A22X ", c.Variable);
            Assert.NotEqual(c.NormalValue, c.DegradedValue);
            Assert.Equal(c.NormalValue, c.RestoreValue);
            Assert.NotEmpty(c.Indicators);
            Assert.DoesNotContain(c.Variable, c.Indicators);
        });
        Assert.Equal(SynapticDegradationSession.Candidates.Count, SynapticDegradationSession.Candidates.Select(c => c.Variable).Distinct().Count());
    }

    [Theory]
    [InlineData("L:A22X L Eng Fire")]
    [InlineData("L:A22X Probe Heat")]
    [InlineData("L:A22X Circuit Breaker L A1")]
    [InlineData("HYD_SOV_1_CLOSED")]
    [InlineData("ELEC_R_GEN_OFF")]
    [InlineData("TOGGLE_ENGINE1_FAILURE")]
    [InlineData("")]
    public void No_arbitrary_variable_or_id_can_be_baselined_applied_restored_or_verified(string id)
    {
        var s = new SynapticDegradationSession();

        Assert.NotNull(s.BeginBaseline(id, 0));
        Assert.NotNull(s.PlanApply(id, 0, Parked).Refusal);
        Assert.NotNull(s.PlanRestore(id, Parked).Refusal);
        Assert.NotNull(s.Verify(id, 0));
        Assert.Null(SynapticDegradationSession.Find(id));
    }

    [Fact]
    public void Excluded_families_stay_out_of_the_whitelist()
    {
        var variables = SynapticDegradationSession.Candidates.Select(c => c.Variable).ToArray();

        Assert.DoesNotContain(variables, v => v.Contains("Fire", StringComparison.Ordinal));
        Assert.DoesNotContain(variables, v => v.Contains("Probe Heat", StringComparison.Ordinal));
        Assert.DoesNotContain(variables, v => v.Contains("Circuit Breaker", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cycle_goes_clean_baseline_applied_restored_verified_once()
    {
        var s = new SynapticDegradationSession();
        Assert.Equal(DegradationStep.Clean, s.StepOf(Gen.Id));
        Assert.Null(s.BeginBaseline(Gen.Id, 0));
        Assert.Equal(DegradationStep.Baseline, s.StepOf(Gen.Id));

        var (apply, _) = s.PlanApply(Gen.Id, 0, Parked);
        Assert.Equal(1, apply!.Value);
        s.RecordWritten(apply);
        Assert.Equal(DegradationStep.Applied, s.StepOf(Gen.Id));
        Assert.Equal(Gen.Id, s.ActiveId);

        var (restore, _) = s.PlanRestore(Gen.Id, Parked);
        Assert.Equal(0, restore!.Value);
        s.RecordWritten(restore);
        Assert.Equal(DegradationStep.Restored, s.StepOf(Gen.Id));
        Assert.Null(s.ActiveId);
        Assert.Null(s.Verify(Gen.Id, 0));
        Assert.Equal(DegradationStep.Verified, s.StepOf(Gen.Id));
        Assert.Null(s.OpenId);

        Assert.Contains("one cycle per candidate", s.BeginBaseline(Gen.Id, 0));
    }

    [Fact]
    public void Only_one_candidate_may_be_open_and_only_one_applied()
    {
        var s = new SynapticDegradationSession();
        Apply(s, "ELEC_L_GEN_OFF");

        Assert.Contains("ELEC_L_GEN_OFF is still open", s.BeginBaseline("PNEU_L_PACK_OFF", 0));
        Assert.Contains("apply only after its baseline", s.PlanApply("PNEU_L_PACK_OFF", 0, Parked).Refusal);
    }

    [Fact]
    public void A_restored_but_unverified_candidate_blocks_the_next_baseline()
    {
        var s = new SynapticDegradationSession();
        Apply(s, "ELEC_L_GEN_OFF");
        var (restore, _) = s.PlanRestore("ELEC_L_GEN_OFF", Parked);
        s.RecordWritten(restore!);

        Assert.Contains("still open (Restored)", s.BeginBaseline("FCS_PFCC_1_OFF", 0));
        Assert.Null(s.Verify("ELEC_L_GEN_OFF", 0));
        Assert.Null(s.BeginBaseline("FCS_PFCC_1_OFF", 0));
    }

    [Fact]
    public void The_live_value_must_be_the_documented_normal_before_baseline_apply_and_verify()
    {
        var s = new SynapticDegradationSession();
        var hyd = SynapticDegradationSession.Find("HYD_ACMP_3A_OFF")!;

        Assert.Contains("not the live state", s.BeginBaseline(hyd.Id, 0));
        Assert.Contains("not the live state", s.BeginBaseline(hyd.Id, 2));
        Assert.Null(s.BeginBaseline(hyd.Id, 1));
        Assert.Contains("not the live state", s.PlanApply(hyd.Id, 0, Parked).Refusal);
        var (apply, _) = s.PlanApply(hyd.Id, 1, Parked);
        Assert.Equal(0, apply!.Value);
        s.RecordWritten(apply);
        var (restore, _) = s.PlanRestore(hyd.Id, Parked);
        Assert.Equal(1, restore!.Value);
        s.RecordWritten(restore);
        Assert.Contains("not the live state", s.Verify(hyd.Id, 0));
    }

    [Theory]
    [InlineData(false, true, 0.0, true, 67.0, "qualified")]
    [InlineData(true, false, 0.0, true, 67.0, "ground only")]
    [InlineData(true, true, 2.0, true, 67.0, "stop first")]
    [InlineData(true, true, 0.0, false, 67.0, "parking brake")]
    [InlineData(true, true, 0.0, true, 10.0, "both engines")]
    public void Apply_is_ground_parked_with_both_engines_running(bool qualified, bool onGround, double gs, bool parked, double n2, string expected)
    {
        var s = new SynapticDegradationSession();
        Assert.Null(s.BeginBaseline(Gen.Id, 0));

        var refusal = s.PlanApply(Gen.Id, 0, new DegradationConditions(qualified, onGround, gs, parked, 67, n2)).Refusal;

        Assert.Contains(expected, refusal);
    }

    [Fact]
    public void Restore_is_explicit_and_never_gated_on_motion()
    {
        var s = new SynapticDegradationSession();
        Assert.Contains("only an applied candidate", s.PlanRestore(Gen.Id, Parked).Refusal);
        Apply(s, Gen.Id);

        var rolling = new DegradationConditions(true, false, 40, false, 0, 0);
        var (restore, refusal) = s.PlanRestore(Gen.Id, rolling);

        Assert.Null(refusal);
        Assert.Equal(Gen.NormalValue, restore!.Value);
        Assert.NotNull(s.PlanRestore(Gen.Id, rolling with { AircraftQualified = false }).Refusal);
    }

    [Fact]
    public void A_restore_readback_that_is_not_normal_halts_for_good()
    {
        var s = new SynapticDegradationSession();
        Apply(s, Gen.Id);
        var (restore, _) = s.PlanRestore(Gen.Id, Parked);
        s.RecordWritten(restore!);

        Assert.NotNull(s.CheckRestoreReadback(Gen, 1));
        Assert.True(s.Halted);
        Assert.Contains("reload the aircraft", s.HaltReason);
        Assert.Contains("halted", s.BeginBaseline("PNEU_L_PACK_OFF", 0));
        Assert.NotNull(s.CheckRestoreReadback(Gen, double.NaN));
    }

    [Fact]
    public void An_uncertain_write_halts_and_blocks_every_further_write()
    {
        var s = new SynapticDegradationSession();
        Assert.Null(s.BeginBaseline(Gen.Id, 0));
        var (apply, _) = s.PlanApply(Gen.Id, 0, Parked);

        s.RecordUncertain(apply!, "timeout");

        Assert.True(s.Halted);
        Assert.Throws<InvalidOperationException>(() => s.RecordWritten(apply!));
        Assert.Contains("halted", s.PlanRestore(Gen.Id, Parked).Refusal);
    }

    [Fact]
    public void Connection_loss_or_reload_halts_only_with_a_candidate_applied()
    {
        var idle = new SynapticDegradationSession();
        Assert.Null(idle.BeginBaseline(Gen.Id, 0));
        idle.ConnectionLost();
        idle.AircraftReplaced("reload");
        Assert.False(idle.Halted);

        var applied = new SynapticDegradationSession();
        Apply(applied, Gen.Id);
        applied.AircraftReplaced("reload");
        Assert.Contains("while ELEC_L_GEN_OFF was applied", applied.HaltReason);
    }

    [Fact]
    public void A_baseline_without_write_can_be_cancelled()
    {
        var s = new SynapticDegradationSession();
        Assert.Null(s.BeginBaseline(Gen.Id, 0));
        Assert.Null(s.CancelBaseline(Gen.Id));
        Assert.Equal(DegradationStep.Clean, s.StepOf(Gen.Id));

        Apply(s, Gen.Id);
        Assert.NotNull(s.CancelBaseline(Gen.Id));
    }

    [Fact]
    public void Every_candidate_can_complete_one_cycle_in_turn()
    {
        var s = new SynapticDegradationSession();
        foreach (var c in SynapticDegradationSession.Candidates)
        {
            FullCycle(s, c.Id);
        }

        Assert.All(s.Snapshot().Values, v => Assert.Equal("Verified", v));
        Assert.False(s.Halted);
    }

    [Theory]
    [InlineData("ELEC_L_GEN_OFF", "USABLE_CONTROLLED_DEGRADATION", true)]
    [InlineData("FCS_PFCC_1_OFF", "NOT_REVERSIBLE", true)]
    [InlineData("ELEC_L_GEN_OFF", "USABLE", false)]
    [InlineData("L_ENG_FIRE", "NOT_REVERSIBLE", false)]
    public void Results_use_only_the_block_vocabulary(string id, string result, bool valid)
    {
        Assert.Equal(valid, SynapticDegradationSession.ValidateResult(id, result) is null);
    }

    [Theory]
    [InlineData("GENERAL ENG COMBUSTION:1")]
    [InlineData("A:LIGHT LANDING")]
    [InlineData("TOGGLE_ENGINE1_FAILURE")]
    [InlineData("l:lowercase")]
    [InlineData("L:")]
    public void The_transport_only_writes_local_variables(string name)
    {
        Assert.Throws<ArgumentException>(() => DiagnosticLocalWrite.Validate(name, "number", 1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void The_transport_only_writes_finite_values(double value)
    {
        Assert.Throws<ArgumentException>(() => DiagnosticLocalWrite.Validate("L:ANY", "number", value));
    }

    [Fact]
    public async Task The_transport_write_needs_a_connection_and_the_native_session()
    {
        await using var h = new Harness();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Simulator.ExperimentalWriteLocalAsync("L:ANY", "number", 1, CancellationToken.None));

        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(FSGAP.Abstractions.Simulator.SimulatorConnectionState.Connected);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.Simulator.ExperimentalWriteLocalAsync("L:ANY", "number", 1, CancellationToken.None));
        Assert.Contains("SimConnect.NET session", error.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => h.Simulator.ExperimentalWriteLocalAsync("PLANE ALTITUDE", "feet", 1, CancellationToken.None));
    }

    [Fact]
    public void The_research_write_path_is_internal()
    {
        var method = typeof(SimConnectSimulator).GetMethod(nameof(SimConnectSimulator.ExperimentalWriteLocalAsync), BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.True(method!.IsAssembly);
        Assert.False(typeof(DiagnosticLocalWrite).IsPublic);
        Assert.Equal([typeof(SimConnectSimulator)], typeof(SimConnectSimulator).Assembly.GetExportedTypes());
    }
}
