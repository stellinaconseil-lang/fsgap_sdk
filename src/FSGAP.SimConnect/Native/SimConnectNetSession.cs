using FSGAP.Abstractions.Simulator;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace FSGAP.SimConnect.Native;

/// <summary>
/// <see cref="ISimConnectSessionFactory"/> backed by SimConnect.NET. Together with
/// <see cref="SimConnectNetSession"/>, the request structs and <see cref="VariableSetStructs"/>, this is the only code in
/// FSGAP that touches SimConnect.NET types.
/// </summary>
internal sealed class SimConnectNetSessionFactory : ISimConnectSessionFactory
{
    /// <inheritdoc />
    public async Task<ISimConnectSession> ConnectAsync(string applicationName, CancellationToken cancellationToken)
    {
        try
        {
            var client = await SafeOpen.OpenAsync(
                () => new SimConnectClient(applicationName)
                {
                    // One reconnect path only: the FSGAP transport. The library's own auto-reconnect stays off.
                    AutoReconnectEnabled = false,
                },
                c => c.ConnectAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
            return new SimConnectNetSession(client);
        }
        catch (SimConnectException ex)
        {
            throw new SimulatorUnavailableException(ex.Message, ex);
        }
    }
}

/// <summary>An open SimConnect.NET client, adapted to <see cref="ISimConnectSession"/>.</summary>
internal sealed class SimConnectNetSession : ISimConnectSession
{
    private const uint CrashedEventId = 1;
    private const uint PauseEventId = 2;

    private readonly SimConnectClient _client;
    private int _disposed;

    public SimConnectNetSession(SimConnectClient client)
    {
        _client = client;
        _client.ConnectionStatusChanged += OnConnectionStatusChanged;
        _client.SystemEventReceived += OnSystemEvent;
        _client.SystemEventEx1Received += OnSystemEventEx1;
    }

    public event Action? Disconnected;

    public event Action? Crashed;

    public event Action<bool>? PauseChanged;

    public bool IsConnected => _client.IsConnected;

    public Task SubscribeAsync(SimulatorSystemEvent systemEvent, CancellationToken cancellationToken) => systemEvent switch
    {
        SimulatorSystemEvent.Crashed => _client.SubscribeToEventAsync("Crashed", CrashedEventId, cancellationToken),
        SimulatorSystemEvent.Pause => _client.SubscribeToEventAsync("Pause_EX1", PauseEventId, cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(systemEvent), systemEvent, null),
    };

    public async Task<RawAircraftIdentity> ReadAircraftIdentityAsync(CancellationToken cancellationToken)
    {
        var vars = await _client.SimVars.GetAsync<AircraftIdentityVars>(0, cancellationToken).ConfigureAwait(false);
        return new RawAircraftIdentity(vars.Title, vars.AtcId, vars.LiveryFolder, vars.LiveryName);
    }

    public Task<TGroup> ReadTelemetryGroupAsync<TGroup>(CancellationToken cancellationToken)
        where TGroup : struct =>
        _client.SimVars.GetAsync<TGroup>(0, cancellationToken);

    public Task<double[]> ReadVariablesAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken) =>
        VariableSetStructs.For(variables)(_client, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _client.ConnectionStatusChanged -= OnConnectionStatusChanged;
        _client.SystemEventReceived -= OnSystemEvent;
        _client.SystemEventEx1Received -= OnSystemEventEx1;
        await _client.DisposeAsync().ConfigureAwait(false);
    }

    private void OnConnectionStatusChanged(object? sender, ConnectionStatusChangedEventArgs e)
    {
        if (!e.IsConnected)
        {
            Disconnected?.Invoke();
        }
    }

    private void OnSystemEvent(object? sender, SimSystemEventReceivedEventArgs e)
    {
        if (e.EventId == CrashedEventId)
        {
            Crashed?.Invoke();
        }
    }

    private void OnSystemEventEx1(object? sender, SimSystemEventEx1ReceivedEventArgs e)
    {
        // Pause_EX1 carries a bitmask of pause kinds (full, sim-only, active...). The exact bit layout is not
        // confirmed, so any non-zero value counts as paused, as in the audited applications.
        if (e.EventId == PauseEventId)
        {
            PauseChanged?.Invoke(e.Data0 != 0);
        }
    }
}

#pragma warning disable CS0649 // Fields are written by SimConnect.NET when it unmarshals the response.

/// <summary>
/// The four MSFS aircraft-identity SimVars, read as one batched request. Names and types are the ones proven in the
/// audited applications (<c>AircraftIdentityVars</c> in FSHANGAR/FLIPPP).
/// </summary>
internal struct AircraftIdentityVars
{
    [SimConnect("ATC ID", SimConnectDataType.String256)]
    public string AtcId;

    [SimConnect("TITLE", SimConnectDataType.String256)]
    public string Title;

    [SimConnect("LIVERY FOLDER", SimConnectDataType.String256)]
    public string LiveryFolder;

    [SimConnect("LIVERY NAME", SimConnectDataType.String256)]
    public string LiveryName;
}

#pragma warning restore CS0649
