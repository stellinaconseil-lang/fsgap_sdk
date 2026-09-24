using System.Reflection;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Native;
using FSGAP.SimConnect.Tests.Fakes;
using SimConnect.NET;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// <see cref="ISimulatorVariableReader"/> on the transport: same native session, one request per list, no read
/// without a connection.
/// </summary>
public class VariableReaderTests
{
    private static readonly SimulatorVariable[] Variables =
    [
        new("L:TEST_SELECTOR", "number"),
        new("HYDRAULIC PRESSURE:1", "Psi"),
    ];

    [Fact]
    public async Task A_list_is_read_in_one_request_on_the_connected_session()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.Variables["L:TEST_SELECTOR"] = 2;
        session.Variables["HYDRAULIC PRESSURE:1"] = 2933.29;
        await h.Simulator.StartAsync();
        await h.WaitForAsync(Abstractions.Simulator.SimulatorConnectionState.Connected);

        ISimulatorVariableReader reader = h.Simulator;
        var values = await reader.ReadAsync(Variables);

        Assert.Equal([2.0, 2933.29], values);
        Assert.Single(session.VariableReads);
        Assert.Single(h.Factory.Sessions);
        Assert.Equal(1, h.Factory.Attempts);
    }

    [Fact]
    public async Task Reading_without_a_connection_throws_and_opens_nothing()
    {
        await using var h = new Harness();

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Simulator.ReadAsync(Variables));
        Assert.Equal(0, h.Factory.Attempts);
    }

    [Fact]
    public async Task After_a_reconnection_reads_go_to_the_new_session_only()
    {
        await using var h = new Harness();
        var first = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        var second = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(Abstractions.Simulator.SimulatorConnectionState.Connected);
        await h.Simulator.ReadAsync(Variables);
        var timers = h.Clock.TimersCreated;

        first.Drop();
        await h.WaitForAsync(Abstractions.Simulator.SimulatorConnectionState.Reconnecting);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Simulator.ReadAsync(Variables));
        await h.ElapseRetryAsync(timers);
        await h.WaitForAsync(Abstractions.Simulator.SimulatorConnectionState.Connected);
        await h.Simulator.ReadAsync(Variables);

        Assert.Single(first.VariableReads);
        Assert.Single(second.VariableReads);
        Assert.Equal(1, h.Factory.MaxLiveSessions);
    }

    [Fact]
    public async Task Stop_and_dispose_end_reading()
    {
        var h = new Harness();
        h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(Abstractions.Simulator.SimulatorConnectionState.Connected);

        await h.Simulator.StopAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Simulator.ReadAsync(Variables));
        await h.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Simulator.ReadAsync(Variables));
    }

    [Fact]
    public async Task Stop_during_a_read_cancels_nothing_else_and_the_read_fails_cleanly()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.VariableReadGate = new TaskCompletionSource();
        await h.Simulator.StartAsync();
        await h.WaitForAsync(Abstractions.Simulator.SimulatorConnectionState.Connected);

        var read = h.Simulator.ReadAsync(Variables);
        await h.Simulator.StopAsync();
        session.VariableReadGate.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => read.WaitAsync(Eventually.Timeout));
        Assert.Equal(Abstractions.Simulator.SimulatorConnectionState.Disconnected, h.Simulator.Status.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Empty_or_duplicate_lists_are_rejected(int kind)
    {
        await using var h = new Harness();
        IReadOnlyList<SimulatorVariable> list = kind == 0 ? [] : [new("A", "number"), new("a", "number")];

        await Assert.ThrowsAsync<ArgumentException>(() => h.Simulator.ReadAsync(list));
    }

    [Fact]
    public void The_emitted_struct_is_public_and_carries_one_attributed_double_per_variable()
    {
        var type = VariableSetStructs.StructTypeFor(Variables);

        Assert.True(type.IsValueType);
        Assert.True(type.IsPublic);
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        Assert.Equal(["V0", "V1"], fields.Select(f => f.Name));
        Assert.All(fields, f => Assert.Equal(typeof(double), f.FieldType));
        var attributes = fields.Select(f => f.GetCustomAttribute<SimConnectAttribute>()!).ToArray();
        Assert.Equal(["L:TEST_SELECTOR", "HYDRAULIC PRESSURE:1"], attributes.Select(a => a.Name));
        Assert.Equal(["number", "Psi"], attributes.Select(a => a.Unit));
        Assert.All(attributes, a => Assert.Equal(SimConnectDataType.FloatDouble, a.DataType));
    }

    [Fact]
    public void The_reader_for_a_list_is_built_once_and_reused()
    {
        var copy = Variables.Select(v => new SimulatorVariable(v.Name, v.Unit)).ToArray();

        Assert.Same(VariableSetStructs.For(Variables), VariableSetStructs.For(copy));
        Assert.NotSame(VariableSetStructs.For(Variables), VariableSetStructs.For([new("L:OTHER", "number")]));
    }
}
