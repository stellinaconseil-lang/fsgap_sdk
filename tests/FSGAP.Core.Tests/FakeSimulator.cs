using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Core.Observation;

namespace FSGAP.Core.Tests;

/// <summary>
/// Scriptable implementation of the three simulator contracts, as a transport would provide them. It proves the
/// contracts are implementable with <see cref="ObservableState{T}"/> and without any simulator library.
/// </summary>
internal sealed class FakeSimulator : ISimulatorConnection, ISimulatorStateProvider, IAircraftDetector
{
    private readonly ObservableState<SimulatorConnectionStatus> _status = new(SimulatorConnectionStatus.Initial);
    private readonly ObservableState<SimulatorState> _state = new(SimulatorState.Initial);
    private readonly ObservableState<AircraftDescriptor?> _aircraft = new(null);

    public SimulatorConnectionStatus Status => _status.Current;

    public TimeSpan SessionElapsed { get; set; }

    SimulatorState ISimulatorStateProvider.Current => _state.Current;

    AircraftDescriptor? IAircraftDetector.Current => _aircraft.Current;

    public void MoveTo(SimulatorConnectionState state, string? detail = null) =>
        _status.Set(new SimulatorConnectionStatus { State = state, Since = DateTimeOffset.UtcNow, Detail = detail });

    public void SetState(SimulatorState state) => _state.Set(state);

    public void Load(AircraftDescriptor? aircraft) => _aircraft.Set(aircraft);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Status.State == SimulatorConnectionState.Disconnected)
        {
            MoveTo(SimulatorConnectionState.Connecting);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        MoveTo(SimulatorConnectionState.Disconnected);
        SessionElapsed = TimeSpan.Zero;
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<SimulatorConnectionStatus> WatchStatusAsync(CancellationToken cancellationToken = default) =>
        _status.WatchAsync(cancellationToken);

    IAsyncEnumerable<SimulatorState> ISimulatorStateProvider.WatchAsync(CancellationToken cancellationToken) =>
        _state.WatchAsync(cancellationToken);

    IAsyncEnumerable<AircraftDescriptor?> IAircraftDetector.WatchAsync(CancellationToken cancellationToken) =>
        _aircraft.WatchAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        _status.Complete();
        _state.Complete();
        _aircraft.Complete();
        return ValueTask.CompletedTask;
    }
}
