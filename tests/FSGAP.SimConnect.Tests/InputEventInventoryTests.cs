using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using FSGAP.Abstractions.Simulator;
using FSGAP.SimConnect.Tests.Fakes;

namespace FSGAP.SimConnect.Tests;

/// <summary>
/// 0.13.0-preview.4: the read-only input-event inventory. The transport lists the loaded aircraft's input events on its
/// single connection; it never sets, subscribes to or maps an input event.
/// </summary>
public class InputEventInventoryTests
{
    [Fact]
    public async Task The_loaded_aircraft_input_events_are_listed_on_the_single_connection()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.InputEvents = [new SimulatorInputEvent("VENDOR_EVENT_A", 1), new SimulatorInputEvent("VENDOR_EVENT_B", ulong.MaxValue)];
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        ISimulatorInputEventInventory inventory = h.Simulator;

        var listed = await inventory.EnumerateInputEventsAsync();

        Assert.Equal(session.InputEvents, listed);
        Assert.Equal(1, session.InputEventListings);
        Assert.Equal(1, h.Factory.Attempts);
    }

    [Fact]
    public async Task Without_a_connection_the_listing_fails_and_nothing_is_sent()
    {
        await using var h = new Harness();
        ISimulatorInputEventInventory inventory = h.Simulator;

        await Assert.ThrowsAsync<InvalidOperationException>(() => inventory.EnumerateInputEventsAsync());
        Assert.Equal(0, h.Factory.Attempts);
    }

    [Fact]
    public async Task A_cancelled_listing_is_cancelled_not_a_timeout()
    {
        await using var h = new Harness();
        var session = h.Factory.SimulatorPresent(Identity.Of("Test Airliner"));
        session.InputEventGate = new TaskCompletionSource();
        await h.Simulator.StartAsync();
        await h.WaitForAsync(SimulatorConnectionState.Connected);
        ISimulatorInputEventInventory inventory = h.Simulator;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inventory.EnumerateInputEventsAsync(cancel.Token));
    }

    [Fact]
    public void The_transport_only_lists_input_events()
    {
        // The only input-event call is the listing: nothing in the transport can set, subscribe to or map one.
        var names = ReferencedNames(typeof(SimConnectSimulator).Assembly.Location);

        Assert.Contains("EnumerateInputEventsAsync", names);
        foreach (var forbidden in new[] { "SetInputEventAsync", "SubscribeToInputEventAsync", "MapInputEventToClientEventAsync", "GetInputEventAsync", "EnumerateInputEventParametersAsync" })
        {
            Assert.DoesNotContain(forbidden, names);
        }

        Assert.Equal(["EnumerateInputEventsAsync"], typeof(ISimulatorInputEventInventory).GetMethods().Select(m => m.Name));
    }

    /// <summary>Names of the members the assembly references (member references) and defines.</summary>
    private static HashSet<string> ReferencedNames(string path)
    {
        using var pe = new PEReader(File.OpenRead(path));
        var reader = pe.GetMetadataReader();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handle in reader.MemberReferences)
        {
            names.Add(reader.GetString(reader.GetMemberReference(handle).Name));
        }

        foreach (var handle in reader.MethodDefinitions)
        {
            names.Add(reader.GetString(reader.GetMethodDefinition(handle).Name));
        }

        return names;
    }
}
