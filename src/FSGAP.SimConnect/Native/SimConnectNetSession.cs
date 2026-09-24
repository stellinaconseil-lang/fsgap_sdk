using FSGAP.Abstractions.Simulator;
using SimConnect.NET;
using SimConnect.NET.Events;

namespace FSGAP.SimConnect.Native;

/// <summary>
/// <see cref="ISimConnectSessionFactory"/> backed by SimConnect.NET. Together with
/// <see cref="SimConnectNetSession"/>, the request structs, <see cref="VariableSetStructs"/> and <see cref="FacilityInterop"/>,
/// this is the only code in FSGAP that touches SimConnect.NET types.
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

    /// <summary>Time allowed for the whole airport list answer (as in the audited applications).</summary>
    internal static readonly TimeSpan AirportListTimeout = TimeSpan.FromSeconds(5);

    private static int _nextAirportRequestId = 0x46534700;

    private readonly SimConnectClient _client;
    private readonly SemaphoreSlim _airportRequests = new(1, 1);
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

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// One request at a time on this connection, each with its own request id (large, far from the library's own
    /// counter), so packets of another request are never mixed in. Packets arrive through the public
    /// <c>RawMessageReceived</c> event on the library's message thread: the handler only copies the bytes (the native
    /// pointer is valid during the callback only), parses them and completes a task whose continuations run elsewhere.
    /// </para>
    /// <para>The answer is complete when every packet <c>0 .. OutOf-1</c> has arrived.</para>
    /// </remarks>
    public async Task<IReadOnlyList<RawAirport>> RequestAirportsAsync(CancellationToken cancellationToken)
    {
        await _airportRequests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requestId = unchecked((uint)Interlocked.Increment(ref _nextAirportRequestId));
            var gate = new object();
            var received = new Dictionary<uint, AirportListPacket>();
            var done = new TaskCompletionSource<IReadOnlyList<RawAirport>>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnRaw(object? sender, RawSimConnectMessageEventArgs e)
            {
                if (e.MessageId != SimConnectRecvId.AirportList || e.DataSize < AirportListParser.DocumentedHeaderSize)
                {
                    return;
                }

                var buffer = new byte[e.DataSize];
                System.Runtime.InteropServices.Marshal.Copy(e.DataPointer, buffer, 0, (int)e.DataSize);
                if (BitConverter.ToUInt32(buffer, 12) != requestId)
                {
                    return; // another request's answer
                }

                try
                {
                    var packet = AirportListParser.Parse(buffer);
                    lock (gate)
                    {
                        received[packet.EntryNumber] = packet;
                        if (received.Count >= Math.Max(packet.OutOf, 1))
                        {
                            done.TrySetResult(received.OrderBy(p => p.Key).SelectMany(p => p.Value.Airports).ToArray());
                        }
                    }
                }
                catch (FormatException ex)
                {
                    done.TrySetException(ex);
                }
            }

            _client.RawMessageReceived += OnRaw;
            try
            {
                var hr = await FacilityInterop.RequestAirportListAsync(_client, requestId, cancellationToken).ConfigureAwait(false);
                if (hr != 0)
                {
                    throw new InvalidOperationException($"The simulator rejected the airport list request (HRESULT 0x{hr:X8}).");
                }

                return await done.Task.WaitAsync(AirportListTimeout, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _client.RawMessageReceived -= OnRaw;
            }
        }
        finally
        {
            _airportRequests.Release();
        }
    }

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
