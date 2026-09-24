using System.Net;
using FSGAP.Abstractions.Failures;
using FSGAP.Fenix.Failures;
using Microsoft.Extensions.Logging.Abstractions;

namespace FSGAP.Fenix.Tests;

/// <summary>Trigger, clear and read active failures against an in-memory EFB.</summary>
public class FenixFailureCommandTests
{
    private static readonly FailureCommand Cpc1 = new(FailureKey.Parse("air-conditioning.cpc.1"));
    private static readonly FailureCommand BlueLeak = new(FailureKey.Parse("hydraulic.blue.leak"), FailureTarget.HydraulicSystem("blue"));
    private static readonly FailureCommand TyreRight1 = new(FailureKey.Parse("landing-gear.tyre-pressure.right-1"));

    internal sealed class Rig
    {
        public Rig(Func<bool>? aircraftReplaced = null, FenixOptions? options = null)
        {
            Options = options ?? new FenixOptions();
            Provider = new FenixFailureProvider(
                new FenixEfbClient(new HttpClient(Efb), Options, Clock),
                FenixFailureCatalogData.Default,
                aircraftReplaced ?? (() => false),
                Options,
                Clock,
                NullLogger.Instance);
        }

        public FakeEfb Efb { get; } = new();

        public CountingClock Clock { get; } = new();

        public FenixOptions Options { get; }

        public FenixFailureProvider Provider { get; }

        /// <summary>Waits for the request (and its timeout timer) to exist, then advances past the timeout.</summary>
        public async Task ElapseTimeoutAsync()
        {
            await Efb.Started.Task.WaitAsync(Wait.Timeout);
            await Task.Delay(20);
            Clock.Advance(Options.RequestTimeout + TimeSpan.FromMilliseconds(1));
        }
    }

    // ---- trigger -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Trigger_posts_the_fenix_id_and_title_with_failed_true_and_succeeds_on_the_echo()
    {
        var rig = new Rig();

        var result = await rig.Provider.TriggerAsync(Cpc1);

        Assert.Equal(FailureCommandStatus.Succeeded, result.Status);
        var (method, path, _) = Assert.Single(rig.Efb.Requests);
        Assert.Equal("POST", method);
        Assert.Equal("/fenix/failures/saveManual", path);
        var body = Assert.Single(rig.Efb.SaveBodies);
        Assert.Equal("F_PNEUMATIC_CPC_1", body["id"]!.GetValue<string>());
        Assert.Equal("CPC 1", body["title"]!.GetValue<string>());
        Assert.True(body["failed"]!.GetValue<bool>());
        Assert.True(body.ContainsKey("failureCondition"));
        Assert.Null(body["failureCondition"]);
        Assert.True(rig.Efb.IsFailed("F_PNEUMATIC_CPC_1"));
    }

    [Fact]
    public async Task Clear_posts_failed_false()
    {
        var rig = new Rig();
        rig.Efb.SetFailed("F_PNEUMATIC_CPC_1", true);

        var result = await rig.Provider.ClearAsync(Cpc1);

        Assert.True(result.IsSuccess);
        Assert.False(Assert.Single(rig.Efb.SaveBodies)["failed"]!.GetValue<bool>());
        Assert.False(rig.Efb.IsFailed("F_PNEUMATIC_CPC_1"));
    }

    [Fact]
    public async Task A_stale_echo_is_confirmed_by_reading_the_list_back()
    {
        var rig = new Rig();
        rig.Efb.StaleEcho = true;

        var result = await rig.Provider.TriggerAsync(TyreRight1);

        Assert.Equal(FailureCommandStatus.Succeeded, result.Status);
        Assert.Equal(["POST", "GET"], rig.Efb.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task An_unconfirmed_state_after_every_readback_is_unconfirmed_not_failed()
    {
        var rig = new Rig(options: new FenixOptions { ConfirmationDelay = TimeSpan.Zero });
        rig.Efb.IgnoreSaves = true;
        rig.Efb.StaleEcho = true;

        var result = await rig.Provider.TriggerAsync(Cpc1);

        Assert.Equal(FailureCommandStatus.Unconfirmed, result.Status);
        Assert.Equal(1, rig.Efb.Requests.Count(r => r.Method == "POST"));
        Assert.Equal(3, rig.Efb.Requests.Count(r => r.Method == "GET"));
    }

    [Fact]
    public async Task An_unparseable_answer_falls_back_to_the_readback()
    {
        var rig = new Rig();
        rig.Efb.SaveBodyOverride = "not json";

        var result = await rig.Provider.TriggerAsync(Cpc1);

        Assert.Equal(FailureCommandStatus.Succeeded, result.Status);
        Assert.Contains(rig.Efb.Requests, r => r.Method == "GET");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, FailureCommandStatus.Rejected)]
    [InlineData(HttpStatusCode.NotFound, FailureCommandStatus.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, FailureCommandStatus.Failed)]
    public async Task An_http_error_is_rejected_or_failed_and_never_retried(HttpStatusCode status, FailureCommandStatus expected)
    {
        var rig = new Rig();
        rig.Efb.SaveStatus = status;

        var result = await rig.Provider.TriggerAsync(Cpc1);

        Assert.Equal(expected, result.Status);
        Assert.Single(rig.Efb.Requests);
    }

    [Fact]
    public async Task Connection_refused_is_unavailable_and_nothing_is_retried()
    {
        var rig = new Rig();
        rig.Efb.Unreachable = true;

        var trigger = await rig.Provider.TriggerAsync(Cpc1);
        var clear = await rig.Provider.ClearAsync(Cpc1);

        Assert.Equal(FailureCommandStatus.Unavailable, trigger.Status);
        Assert.Equal(FailureCommandStatus.Unavailable, clear.Status);
        Assert.Equal(2, rig.Efb.RequestCount);
    }

    [Fact]
    public async Task A_timeout_is_unconfirmed_because_the_efb_may_have_applied_it()
    {
        var rig = new Rig();
        rig.Efb.HangOnSave = true;

        var pending = rig.Provider.TriggerAsync(Cpc1);
        await rig.ElapseTimeoutAsync();
        var result = await pending.WaitAsync(Wait.Timeout);

        Assert.Equal(FailureCommandStatus.Unconfirmed, result.Status);
        Assert.Single(rig.Efb.Requests);
    }

    [Fact]
    public async Task Clear_times_out_the_same_way()
    {
        var rig = new Rig();
        rig.Efb.HangOnSave = true;

        var pending = rig.Provider.ClearAsync(Cpc1);
        await rig.ElapseTimeoutAsync();

        Assert.Equal(FailureCommandStatus.Unconfirmed, (await pending.WaitAsync(Wait.Timeout)).Status);
    }

    [Fact]
    public async Task Cancelling_a_command_throws_and_is_not_reported_as_a_result()
    {
        var rig = new Rig();
        rig.Efb.HangOnSave = true;
        using var cts = new CancellationTokenSource();

        var pending = rig.Provider.TriggerAsync(Cpc1, cts.Token);
        await rig.Efb.Started.Task.WaitAsync(Wait.Timeout);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(Wait.Timeout));
    }

    [Fact]
    public async Task Unknown_key_unsupported_target_and_replaced_aircraft_send_nothing()
    {
        var replaced = false;
        var rig = new Rig(() => replaced);

        var unknown = await rig.Provider.TriggerAsync(new FailureCommand(FailureKey.Parse("engine.9.explodes")));
        var wrongTarget = await rig.Provider.TriggerAsync(new FailureCommand(BlueLeak.Key, FailureTarget.HydraulicSystem("green")));
        var defaultTarget = await rig.Provider.ClearAsync(new FailureCommand(BlueLeak.Key));
        replaced = true;
        var afterChange = await rig.Provider.TriggerAsync(Cpc1);

        Assert.Equal(FailureCommandStatus.NotSupported, unknown.Status);
        Assert.Equal(FailureCommandStatus.NotSupported, wrongTarget.Status);
        Assert.Equal(FailureCommandStatus.NotSupported, defaultTarget.Status);
        Assert.Equal(FailureCommandStatus.Unavailable, afterChange.Status);
        Assert.Equal(0, rig.Efb.RequestCount);
    }

    [Fact]
    public async Task A_typed_target_command_reaches_the_efb()
    {
        var rig = new Rig();

        Assert.True((await rig.Provider.TriggerAsync(BlueLeak)).IsSuccess);
        Assert.True(rig.Efb.IsFailed("F_HYD_LEAK_BLUE"));
    }

    [Fact]
    public async Task Result_messages_never_contain_a_fenix_id()
    {
        var rig = new Rig();
        var messages = new List<string?>();
        rig.Efb.Unreachable = true;
        messages.Add((await rig.Provider.TriggerAsync(Cpc1)).Message);
        rig.Efb.Unreachable = false;
        rig.Efb.SaveStatus = HttpStatusCode.BadRequest;
        messages.Add((await rig.Provider.TriggerAsync(Cpc1)).Message);
        messages.Add((await rig.Provider.TriggerAsync(new FailureCommand(FailureKey.Parse("engine.9.explodes")))).Message);

        Assert.All(messages, m =>
        {
            Assert.NotNull(m);
            Assert.DoesNotContain("F_", m, StringComparison.Ordinal);
            Assert.DoesNotContain("8083", m, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Commands_run_one_at_a_time_in_the_order_issued()
    {
        var rig = new Rig();
        rig.Efb.HangOnSave = true;

        var trigger = rig.Provider.TriggerAsync(Cpc1);
        await rig.Efb.Started.Task.WaitAsync(Wait.Timeout);
        var clear = rig.Provider.ClearAsync(Cpc1);
        await Wait.SettleAsync();

        Assert.Single(rig.Efb.Requests); // the clear waits for the trigger
        rig.Efb.HangOnSave = false;
        await rig.ElapseTimeoutAsync();
        await trigger.WaitAsync(Wait.Timeout);
        await clear.WaitAsync(Wait.Timeout);

        Assert.Equal([true, false], rig.Efb.SaveBodies.Select(b => b["failed"]!.GetValue<bool>()));
        Assert.False(rig.Efb.IsFailed("F_PNEUMATIC_CPC_1"));
    }

    [Fact]
    public async Task Two_simultaneous_triggers_both_complete()
    {
        var rig = new Rig();

        var results = await Task.WhenAll(rig.Provider.TriggerAsync(Cpc1), rig.Provider.TriggerAsync(BlueLeak));

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.True(rig.Efb.IsFailed("F_PNEUMATIC_CPC_1") && rig.Efb.IsFailed("F_HYD_LEAK_BLUE"));
    }

    // ---- read active ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task No_active_failure_reads_as_empty()
    {
        var rig = new Rig();

        Assert.Empty(await rig.Provider.GetActiveFailuresAsync());
        Assert.Equal("/fenix/failures/manual", Assert.Single(rig.Efb.Requests).Path);
    }

    [Fact]
    public async Task Known_active_failures_are_reported_by_key_target_and_category()
    {
        var rig = new Rig();
        rig.Efb.SetFailed("F_PNEUMATIC_CPC_1", true);
        rig.Efb.SetFailed("F_HYD_LEAK_BLUE", true);

        var active = await rig.Provider.GetActiveFailuresAsync();

        Assert.Equal(2, active.Count);
        var leak = Assert.Single(active, f => f.Key == BlueLeak.Key);
        Assert.Equal(FailureTarget.HydraulicSystem("blue"), leak.Target);
        Assert.Equal(FailureCategory.Hydraulic, leak.Category);
        Assert.Equal("Blue hydraulic leak", leak.Description);
        Assert.Contains(active, f => f.Key == Cpc1.Key);
    }

    [Fact]
    public async Task A_catalog_failure_without_a_key_is_reported_unclassified_without_its_id()
    {
        var rig = new Rig();
        rig.Efb.SetFailed("F_PNEUMATIC_CPC_2", true);

        var failure = Assert.Single(await rig.Provider.GetActiveFailuresAsync());

        Assert.Null(failure.Key);
        Assert.False(failure.IsClassified);
        Assert.Equal(FailureCategory.AirConditioning, failure.Category);
        Assert.Equal("CPC 2", failure.Description);
        Assert.Equal(FailureTarget.Aircraft, failure.Target);
    }

    [Fact]
    public async Task A_failure_unknown_to_the_catalog_is_reported_not_dropped()
    {
        var rig = new Rig();
        rig.Efb.ListBodyOverride = """{"atas":[{"groups":[{"failures":[{"id":"F_NEW_IN_A_FENIX_UPDATE","title":"New thing","failureCondition":null,"failed":true}]}]}]}""";

        var failure = Assert.Single(await rig.Provider.GetActiveFailuresAsync());

        Assert.Null(failure.Key);
        Assert.Equal("New thing", failure.Description);
        Assert.Equal(FailureCategory.Other, failure.Category);
    }

    [Fact]
    public async Task A_failure_listed_twice_is_one_active_failure()
    {
        var rig = new Rig();
        rig.Efb.ListBodyOverride = """{"atas":[{"groups":[{"failures":[{"id":"F_PNEUMATIC_CPC_1","failed":true},{"id":"F_PNEUMATIC_CPC_1","failed":true}]}]},{"groups":[{"failures":[{"id":"f_pneumatic_cpc_1","failed":true}]}]}]}""";

        Assert.Single(await rig.Provider.GetActiveFailuresAsync());
    }

    [Fact]
    public async Task An_active_entry_without_an_id_is_still_reported()
    {
        var rig = new Rig();
        rig.Efb.ListBodyOverride = """{"atas":[{"groups":[{"failures":[{"title":"Mystery","failed":true},{"id":"F_PNEUMATIC_CPC_1","failed":false}]}]}]}""";

        var failure = Assert.Single(await rig.Provider.GetActiveFailuresAsync());

        Assert.Null(failure.Key);
        Assert.Equal("Unidentified failure", failure.Description);
    }

    [Theory]
    [InlineData("""{"atas":[{"groups":[{"failures":[{"id":"F_PNEUMATIC_CPC_1","failed":"yes"}]}]}]}""")]
    [InlineData("""{"atas":[{"groups":[{"failures":[{"id":"F_PNEUMATIC_CPC_1"}]}]}]}""")]
    [InlineData("""{"something":"else"}""")]
    [InlineData("""[1,2,3]""")]
    [InlineData("""<html>""")]
    public async Task An_answer_that_cannot_be_fully_read_is_unavailable_not_empty(string body)
    {
        var rig = new Rig();
        rig.Efb.ListBodyOverride = body;

        await Assert.ThrowsAsync<FailuresUnavailableException>(() => rig.Provider.GetActiveFailuresAsync());
    }

    [Fact]
    public async Task An_unreachable_efb_or_an_http_error_is_unavailable()
    {
        var rig = new Rig();
        rig.Efb.Unreachable = true;
        await Assert.ThrowsAsync<FailuresUnavailableException>(() => rig.Provider.GetActiveFailuresAsync());

        rig.Efb.Unreachable = false;
        rig.Efb.ListStatus = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<FailuresUnavailableException>(() => rig.Provider.GetActiveFailuresAsync());
    }

    [Fact]
    public async Task A_read_timeout_is_unavailable()
    {
        var rig = new Rig();
        rig.Efb.HangOnList = true;

        var pending = rig.Provider.GetActiveFailuresAsync();
        await rig.ElapseTimeoutAsync();

        await Assert.ThrowsAsync<FailuresUnavailableException>(() => pending.WaitAsync(Wait.Timeout));
    }

    [Fact]
    public async Task Reading_after_the_aircraft_was_replaced_is_unavailable_and_sends_nothing()
    {
        var rig = new Rig(() => true);

        await Assert.ThrowsAsync<FailuresUnavailableException>(() => rig.Provider.GetActiveFailuresAsync());
        Assert.Equal(0, rig.Efb.RequestCount);
    }

    [Fact]
    public async Task The_active_list_comes_from_the_efb_not_from_the_commands_sent()
    {
        var rig = new Rig();
        rig.Efb.IgnoreSaves = true; // the EFB accepts but does not apply

        await rig.Provider.TriggerAsync(Cpc1);

        Assert.Empty(await rig.Provider.GetActiveFailuresAsync());
    }

    [Fact]
    public async Task Active_failures_never_expose_a_fenix_id()
    {
        var rig = new Rig();
        foreach (var id in new[] { "F_PNEUMATIC_CPC_1", "F_PNEUMATIC_CPC_2", "B_INT_SFCDC1F", "F_HYD_LEAK_BLUE" })
        {
            rig.Efb.SetFailed(id, true);
        }

        var serialized = System.Text.Json.JsonSerializer.Serialize(await rig.Provider.GetActiveFailuresAsync());

        Assert.All(FenixFailureCatalogData.Default.Raw, raw => Assert.DoesNotContain(raw.Id, serialized, StringComparison.OrdinalIgnoreCase));
    }

    // ---- lifecycle ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Calls_after_dispose_throw_object_disposed()
    {
        var rig = new Rig();
        await rig.Provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Provider.TriggerAsync(Cpc1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Provider.ClearAsync(Cpc1));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => rig.Provider.GetActiveFailuresAsync());
        Assert.Equal(0, rig.Efb.RequestCount);
    }

    [Fact]
    public async Task Dispose_during_a_post_makes_the_command_unconfirmed()
    {
        var rig = new Rig();
        rig.Efb.HangOnSave = true;

        var pending = rig.Provider.TriggerAsync(Cpc1);
        await rig.Efb.Started.Task.WaitAsync(Wait.Timeout);
        await rig.Provider.DisposeAsync();

        Assert.Equal(FailureCommandStatus.Unconfirmed, (await pending.WaitAsync(Wait.Timeout)).Status);
    }

    [Fact]
    public async Task Dispose_during_a_get_ends_the_read_with_object_disposed()
    {
        var rig = new Rig();
        rig.Efb.HangOnList = true;

        var pending = rig.Provider.GetActiveFailuresAsync();
        await rig.Efb.Started.Task.WaitAsync(Wait.Timeout);
        await rig.Provider.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(Wait.Timeout));
    }

    [Fact]
    public async Task A_command_waiting_its_turn_when_the_session_ends_is_never_sent()
    {
        var rig = new Rig();
        rig.Efb.HangOnSave = true;

        var first = rig.Provider.TriggerAsync(Cpc1);
        await rig.Efb.Started.Task.WaitAsync(Wait.Timeout);
        var second = rig.Provider.TriggerAsync(BlueLeak);
        await Wait.SettleAsync();
        await rig.Provider.DisposeAsync();

        Assert.Equal(FailureCommandStatus.Unconfirmed, (await first.WaitAsync(Wait.Timeout)).Status);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => second.WaitAsync(Wait.Timeout));
        Assert.Single(rig.Efb.Requests);
    }
}
