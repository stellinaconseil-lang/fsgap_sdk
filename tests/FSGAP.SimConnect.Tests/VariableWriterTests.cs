using System.Reflection;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// BLOCK 11.0: the bounded local-variable writer. The transport implements it explicitly on its single connection: local
/// variables only, finite values, no public write method on the class.
/// </summary>
public class VariableWriterTests
{
    private static readonly SimulatorVariable Local = new("L:VENDOR SWITCH", "number");

    [Fact]
    public async Task A_local_write_goes_to_the_one_connected_session()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        ISimulatorVariableWriter writer = h.Simulator;

        await writer.WriteAsync(Local, 1);
        await writer.WriteAsync(Local, 0);

        Assert.Equal([(Local, 1.0), (Local, 0.0)], session.LocalWrites);
        Assert.Equal(1, h.Factory.Attempts);
    }

    [Fact]
    public async Task Writing_without_a_connection_throws_and_writes_nothing()
    {
        await using var h = new Harness();
        ISimulatorVariableWriter writer = h.Simulator;

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(Local, 1));
    }

    [Theory]
    [InlineData("PLANE ALTITUDE")]
    [InlineData("A:LIGHT LANDING")]
    [InlineData("K:TOGGLE_ENGINE1_FAILURE")]
    [InlineData("l:lower case")]
    [InlineData("L:")]
    public async Task Only_local_variables_can_be_written(string name)
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        ISimulatorVariableWriter writer = h.Simulator;

        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(new SimulatorVariable(name, "number"), 1));

        Assert.Empty(session.LocalWrites);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public async Task Only_finite_values_can_be_written(double value)
    {
        await using var h = new Harness();
        ISimulatorVariableWriter writer = h.Simulator;

        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteAsync(Local, value));
    }

    [Fact]
    public async Task A_cancelled_write_is_cancelled_not_timed_out()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        session.WriteGate = new TaskCompletionSource();
        ISimulatorVariableWriter writer = h.Simulator;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(Local, 1, cancel.Token));

        Assert.Empty(session.LocalWrites);
    }

    [Fact]
    public void The_transport_has_no_public_write_method_of_its_own()
    {
        var publicWrites = typeof(SimConnectSimulator)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
            .Where(m => m.Name.Contains("Write", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(publicWrites);
        Assert.True(typeof(ISimulatorVariableWriter).IsAssignableFrom(typeof(SimConnectSimulator)));
        Assert.Equal([typeof(SimConnectSimulator)], typeof(SimConnectSimulator).Assembly.GetExportedTypes());
    }
}
