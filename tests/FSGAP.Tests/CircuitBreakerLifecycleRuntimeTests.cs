using FSGAP.Abstractions.Failures;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.Tests;

/// <summary>
/// A tripped Synaptic circuit breaker through the public runtime, the chain an application uses: the runtime closing (the
/// application quits), the aircraft unloaded and reloaded, and a new runtime on the same simulator. The breaker stays out
/// and a repeated trigger never writes it again. One fake simulator stands for the aircraft that outlives the runtimes.
/// </summary>
public class CircuitBreakerLifecycleRuntimeTests
{
    private const string Dome = "electrical.circuit-breaker.lights-dome";
    private const string DomeCell = "L:A22X Circuit Breaker R B4";

    private static FailureCommand Cmd(string key) => new(FailureKey.Parse(key));

    private static FakeSimulator A220Simulator()
    {
        var simulator = new FakeSimulator();
        simulator.InputEvents.Add(new SimulatorInputEvent("AIRLINER_CB_LIGHTS_DOME", 1));
        simulator.Values[DomeCell] = 0;
        return simulator;
    }

    private static (string, double)[] DomeWrites(FakeSimulator simulator) => simulator.Writes.Where(w => w.Name == DomeCell).ToArray();

    [Fact]
    public async Task B1_closing_the_runtime_with_the_aircraft_loaded_leaves_the_tripped_breaker_out()
    {
        var simulator = A220Simulator();
        await using (var rig = new RuntimeRig(simulator: simulator))
        {
            await rig.Runtime.StartAsync();
            var session = (await rig.LoadAsync(Aircraft.SynapticA220)).Session!;
            Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        } // the application quits: Runtime.DisposeAsync closes the session while the aircraft is still loaded

        Assert.Equal(1, simulator.Values[DomeCell]);
        Assert.Equal([(DomeCell, 1.0)], DomeWrites(simulator));
    }

    [Fact]
    public async Task B2_a_new_runtime_on_the_same_aircraft_confirms_the_breaker_without_writing()
    {
        var simulator = A220Simulator();
        await using (var first = new RuntimeRig(simulator: simulator))
        {
            await first.Runtime.StartAsync();
            var session = (await first.LoadAsync(Aircraft.SynapticA220)).Session!;
            Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        }

        await using var second = new RuntimeRig(simulator: simulator); // the application restarted
        await second.Runtime.StartAsync();
        var again = (await second.LoadAsync(Aircraft.SynapticA220)).Session!;
        var trip = await again.Failures.TriggerAsync(Cmd(Dome));

        Assert.Equal(FailureCommandStatus.Succeeded, trip.Status);
        Assert.Equal([(DomeCell, 1.0)], DomeWrites(simulator));
        Assert.Equal([Dome], (await again.Failures.GetActiveFailuresAsync()).Select(a => a.Key!.Value));
    }

    [Fact]
    public async Task Unloading_and_reloading_the_aircraft_never_resets_the_breaker_and_a_redelivered_trigger_writes_nothing()
    {
        var simulator = A220Simulator();
        await using var rig = new RuntimeRig(simulator: simulator);
        await rig.Runtime.StartAsync();
        var session = (await rig.LoadAsync(Aircraft.SynapticA220)).Session!;
        Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);

        await rig.LoadAsync(null); // disconnection / aircraft unloaded: the session is closed
        var reattached = (await rig.LoadAsync(Aircraft.SynapticA220)).Session!;
        Assert.NotSame(session, reattached);
        Assert.True((await reattached.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);

        Assert.Equal(1, simulator.Values[DomeCell]);
        Assert.Equal([(DomeCell, 1.0)], DomeWrites(simulator));
    }

    [Fact]
    public async Task Closing_the_runtime_still_restores_a_historical_synaptic_recipe()
    {
        var simulator = A220Simulator();
        simulator.Values["L:A22X R Gen Off"] = 0;
        await using (var rig = new RuntimeRig(simulator: simulator))
        {
            await rig.Runtime.StartAsync();
            var session = (await rig.LoadAsync(Aircraft.SynapticA220)).Session!;
            Assert.True((await session.Failures.TriggerAsync(Cmd("electrical.generator.2"))).IsSuccess);
            Assert.True((await session.Failures.TriggerAsync(Cmd(Dome))).IsSuccess);
        }

        Assert.Equal(0, simulator.Values["L:A22X R Gen Off"]);
        Assert.Equal([("L:A22X R Gen Off", 1.0), ("L:A22X R Gen Off", 0.0)], simulator.Writes.Where(w => w.Name == "L:A22X R Gen Off").ToArray());
        Assert.Equal(1, simulator.Values[DomeCell]);
    }
}
