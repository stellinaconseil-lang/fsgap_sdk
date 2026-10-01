using System.Text.Json;
using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Tests;

/// <summary>BLOCK 10C.2 — RESEARCH ONLY: the safety model of the native failure qualification campaign.</summary>
public class NativeFailureMatrixTests
{
    private static readonly NativeFailureConditions Parked = new(AircraftQualified: true, OnGround: true, GroundSpeedKnots: 0.0, ParkingBrakeSet: true, HeightAboveGroundFeet: 0, Engine1N2Percent: 60, Engine2N2Percent: 60);

    private static readonly NativeFailureConditions Cruise = new(AircraftQualified: true, OnGround: false, GroundSpeedKnots: 250, ParkingBrakeSet: false, HeightAboveGroundFeet: 6000, Engine1N2Percent: 85, Engine2N2Percent: 85);

    private static NativeFailurePlan Inject(NativeFailureMatrix m, string key, NativeFailureConditions c)
    {
        var (plan, refusal) = m.PlanInject(key, c);
        Assert.Null(refusal);
        m.RecordSent(plan!);
        return plan!;
    }

    private static void Restore(NativeFailureMatrix m, string key, NativeFailureConditions c)
    {
        var (plan, refusal) = m.PlanRestore(key, c);
        Assert.Null(refusal);
        m.RecordSent(plan!);
    }

    private static void Cycle(NativeFailureMatrix m, string key, NativeFailureConditions c)
    {
        Inject(m, key, c);
        Restore(m, key, c);
        Assert.Null(m.Verify(key));
    }

    [Fact]
    public void The_matrix_is_exactly_the_nine_allowed_events_each_once()
    {
        Assert.Equal(["LB", "RB", "TB", "E1", "E2", "HYD", "ELEC", "PITOT", "STATIC"], NativeFailureMatrix.Definitions.Select(d => d.Key));
        Assert.Equal(
            NativeFailureProbeEvents.ClientEventIds.Keys.Order(StringComparer.Ordinal),
            NativeFailureMatrix.Definitions.Select(d => d.EventName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("TOGGLE_LEFT_BRAKE_FAILURE")]
    [InlineData("E3")]
    [InlineData("VACUUM")]
    [InlineData("")]
    public void No_arbitrary_key_or_event_name_can_be_planned(string key)
    {
        var m = new NativeFailureMatrix();

        Assert.NotNull(m.PlanInject(key, Parked).Refusal);
        Assert.NotNull(m.PlanRestore(key, Parked).Refusal);
        Assert.NotNull(m.Verify(key));
    }

    [Fact]
    public void Each_event_is_sent_at_most_twice_inject_then_restore()
    {
        var m = new NativeFailureMatrix();
        var inject = Inject(m, "LB", Parked);
        Assert.Equal(NativeFailureDiagnosticState.Injected, m.StateOf("LB"));

        Assert.Throws<InvalidOperationException>(() => m.RecordSent(inject));
        Restore(m, "LB", Parked);
        Assert.Equal(NativeFailureDiagnosticState.Restored, m.StateOf("LB"));
        Assert.Null(m.Verify("LB"));

        Assert.Contains("already Restored", m.PlanInject("LB", Parked).Refusal);
        Assert.NotNull(m.PlanRestore("LB", Parked).Refusal);
    }

    [Fact]
    public void Only_one_failure_may_be_injected_at_a_time()
    {
        var m = new NativeFailureMatrix();
        Inject(m, "LB", Parked);

        Assert.Contains("LB is still injected", m.PlanInject("RB", Parked).Refusal);
        Assert.Contains("LB is still injected", m.PlanInject("HYD", Parked).Refusal);
        Assert.Equal("LB", m.ActiveKey);
    }

    [Fact]
    public void A_restore_must_be_verified_before_the_next_inject()
    {
        var m = new NativeFailureMatrix();
        Inject(m, "LB", Parked);
        Restore(m, "LB", Parked);

        Assert.Contains("not verified", m.PlanInject("RB", Parked).Refusal);
        Assert.Null(m.Verify("LB"));
        Assert.Null(m.PlanInject("RB", Parked).Refusal);
    }

    [Fact]
    public void Restore_is_only_for_the_injected_failure_and_never_gated_on_motion()
    {
        var m = new NativeFailureMatrix();
        Assert.Contains("only an injected failure", m.PlanRestore("LB", Parked).Refusal);
        Inject(m, "LB", Parked);

        var rolling = Parked with { GroundSpeedKnots = 12, ParkingBrakeSet = false };
        Assert.Null(m.PlanRestore("LB", rolling).Refusal);
        Assert.Null(m.PlanRestore("LB", rolling with { OnGround = false }).Refusal);
        Assert.NotNull(m.PlanRestore("LB", rolling with { AircraftQualified = false }).Refusal);
        Assert.NotNull(m.PlanRestore("RB", rolling).Refusal);
    }

    [Fact]
    public void Verified_needs_a_restored_failure()
    {
        var m = new NativeFailureMatrix();
        Assert.NotNull(m.Verify("LB"));
        Inject(m, "LB", Parked);
        Assert.NotNull(m.Verify("LB"));
        Restore(m, "LB", Parked);
        Assert.Null(m.Verify("LB"));
        Assert.True(m.IsVerified("LB"));
    }

    [Theory]
    [InlineData(false, true, 0.0, true, "qualified")]
    [InlineData(true, false, 0.0, true, "SIM ON GROUND")]
    [InlineData(true, true, 1.0, true, "stop the aircraft")]
    [InlineData(true, true, 0.2, false, "parking brake")]
    public void Ground_events_need_a_qualified_aircraft_stationary_on_the_ground_with_the_parking_brake(bool qualified, bool onGround, double groundSpeed, bool parked, string expected)
    {
        var m = new NativeFailureMatrix();

        var refusal = m.PlanInject("HYD", Parked with { AircraftQualified = qualified, OnGround = onGround, GroundSpeedKnots = groundSpeed, ParkingBrakeSet = parked }).Refusal;

        Assert.Contains(expected, refusal);
    }

    [Theory]
    [InlineData("E1")]
    [InlineData("E2")]
    public void Engine_events_also_need_both_engines_running(string key)
    {
        var m = new NativeFailureMatrix();

        Assert.Contains("both engines", m.PlanInject(key, Parked with { Engine2N2Percent = 20 }).Refusal);
        Assert.Contains("both engines", m.PlanInject(key, Parked with { Engine1N2Percent = 0 }).Refusal);
        Assert.Null(m.PlanInject(key, Parked).Refusal);
    }

    [Fact]
    public void Total_brakes_only_after_left_and_right_were_restored_and_verified()
    {
        var m = new NativeFailureMatrix();
        Assert.Contains("requires LB", m.PlanInject("TB", Parked).Refusal);
        Cycle(m, "LB", Parked);
        Assert.Contains("requires RB", m.PlanInject("TB", Parked).Refusal);
        Cycle(m, "RB", Parked);
        Assert.Null(m.PlanInject("TB", Parked).Refusal);
    }

    [Theory]
    [InlineData("PITOT")]
    [InlineData("STATIC")]
    public void Air_data_events_are_airborne_only_and_above_the_minimum_height(string key)
    {
        var m = new NativeFailureMatrix();

        Assert.Contains("airborne only", m.PlanInject(key, Parked).Refusal);
        Assert.Contains("AGL", m.PlanInject(key, Cruise with { HeightAboveGroundFeet = 400 }).Refusal);
        Assert.Contains("qualified", m.PlanInject(key, Cruise with { AircraftQualified = false }).Refusal);
        Assert.Null(m.PlanInject(key, Cruise).Refusal);
    }

    [Fact]
    public void Ground_events_are_refused_in_flight()
    {
        var m = new NativeFailureMatrix();

        Assert.Contains("ground tests only", m.PlanInject("LB", Cruise).Refusal);
        Assert.Contains("ground tests only", m.PlanInject("E1", Cruise).Refusal);
    }

    [Fact]
    public void Air_data_tests_after_ground_tests_need_a_clean_reload()
    {
        var m = new NativeFailureMatrix();
        Cycle(m, "HYD", Parked);

        Assert.Contains("clean aircraft reload", m.PlanInject("PITOT", Cruise).Refusal);
        Assert.Null(m.RecordCleanReload());
        Assert.True(m.CleanReloadAfterGroundTests);
        Assert.Null(m.PlanInject("PITOT", Cruise).Refusal);
    }

    [Fact]
    public void A_reload_is_refused_while_a_failure_is_injected()
    {
        var m = new NativeFailureMatrix();
        Inject(m, "ELEC", Parked);

        Assert.Contains("ELEC is still injected", m.RecordCleanReload());
        Assert.False(m.CleanReloadAfterGroundTests);
    }

    [Fact]
    public void An_uncertain_send_halts_the_matrix_for_good()
    {
        var m = new NativeFailureMatrix();
        var (plan, _) = m.PlanInject("E1", Parked);

        m.RecordUncertain(plan!, "timeout");

        Assert.True(m.Halted);
        Assert.Contains("uncertain", m.HaltReason);
        Assert.Contains("halted", m.PlanInject("E2", Parked).Refusal);
        Assert.Throws<InvalidOperationException>(() => m.RecordSent(plan!));
    }

    [Fact]
    public void A_halt_also_blocks_the_restore()
    {
        var m = new NativeFailureMatrix();
        Inject(m, "E1", Parked);

        m.Halt("engine did not recover");

        Assert.Contains("halted", m.PlanRestore("E1", Parked).Refusal);
        Assert.Equal("engine did not recover", m.HaltReason);
        m.Halt("second reason");
        Assert.Equal("engine did not recover", m.HaltReason);
    }

    [Fact]
    public void Connection_loss_or_aircraft_replacement_halts_only_with_a_failure_injected()
    {
        var idle = new NativeFailureMatrix();
        idle.ConnectionLost();
        idle.AircraftReplaced("reload");
        Assert.False(idle.Halted);

        var lost = new NativeFailureMatrix();
        Inject(lost, "LB", Parked);
        lost.ConnectionLost();
        Assert.Contains("connection lost while LB", lost.HaltReason);

        var replaced = new NativeFailureMatrix();
        Inject(replaced, "RB", Parked);
        replaced.AircraftReplaced("'A220-300' -> 'A320'");
        Assert.Contains("aircraft replaced", replaced.HaltReason);
    }

    [Fact]
    public void Snapshot_reports_local_diagnostic_state_for_every_key()
    {
        var m = new NativeFailureMatrix();
        Cycle(m, "LB", Parked);
        Inject(m, "RB", Parked);

        var snapshot = m.Snapshot();

        Assert.Equal(9, snapshot.Count);
        Assert.Equal("Restored+VERIFIED", snapshot["LB"]);
        Assert.Equal("Injected", snapshot["RB"]);
        Assert.Equal("NotTouched", snapshot["STATIC"]);
    }

    [Theory]
    [InlineData("LB", "NATIVE_FAILURE_WORKS", "PROVEN_LIVE", true)]
    [InlineData("STATIC", "FAILURE_RESULT_INDETERMINATE", "EXTRAPOLATED", true)]
    [InlineData("LB", "NATIVE_BRAKE_FAILURE_WORKS", "PROVEN_LIVE", false)]
    [InlineData("LB", "NATIVE_FAILURE_WORKS", "PROVEN", false)]
    [InlineData("E3", "NATIVE_FAILURE_WORKS", "PROVEN_LIVE", false)]
    public void Verdicts_use_only_the_campaign_vocabulary(string key, string verdict, string confidence, bool valid)
    {
        Assert.Equal(valid, NativeFailureMatrix.ValidateVerdict(key, verdict, confidence) is null);
    }

    [Fact]
    public async Task Journal_lines_are_valid_json_with_a_strictly_increasing_sequence_even_when_written_concurrently()
    {
        using var writer = new StringWriter();
        var journal = new NativeFailureJournal(writer, () => new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                journal.Write("sample", new { t, i });
            }
        })));
        journal.Write("end", null);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(401, lines.Length);
        Assert.Equal(401, journal.Count);
        var sequence = lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("seq").GetInt64()).ToArray();
        Assert.Equal(Enumerable.Range(1, 401).Select(i => (long)i), sequence);
        var last = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal("end", last.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("payload").ValueKind);
    }

    [Fact]
    public void Journal_refuses_an_empty_kind()
    {
        var journal = new NativeFailureJournal(new StringWriter());

        Assert.Throws<ArgumentException>(() => journal.Write(" ", new { }));
        Assert.Equal(0, journal.Count);
    }
}
