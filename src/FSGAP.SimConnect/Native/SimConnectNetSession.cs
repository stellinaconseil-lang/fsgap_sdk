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

    /// <summary>
    /// Time allowed for the whole livery enumeration answer. Live: 15 815 rows in 201 packets arrived in 60–73 ms, so this
    /// leaves two orders of magnitude of margin while still bounding the wait.
    /// </summary>
    internal static readonly TimeSpan LiveryListTimeout = TimeSpan.FromSeconds(10);

    private static int _nextLiveryRequestId = 0x46534C00;

    /// <summary>BLOCK 10A.5 (experimental AI probes): time allowed for an AI object id to be assigned or removed.</summary>
    internal static readonly TimeSpan LiveryDiscoveryTimeout = TimeSpan.FromSeconds(15);

    private static int _nextDiscoveryRequestId = 0x46534800;

    private readonly SimConnectClient _client;
    private readonly SemaphoreSlim _airportRequests = new(1, 1);
    private readonly SemaphoreSlim _liveryRequests = new(1, 1);
    private readonly SemaphoreSlim _discoveryRequests = new(1, 1);
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
        return new RawAircraftIdentity(vars.Title, vars.AtcId, vars.LiveryFolder, vars.LiveryName, vars.AtcModel, vars.AtcType);
    }

    public Task<TGroup> ReadTelemetryGroupAsync<TGroup>(CancellationToken cancellationToken)
        where TGroup : struct =>
        _client.SimVars.GetAsync<TGroup>(0, cancellationToken);

    public Task<double[]> ReadVariablesAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken) =>
        VariableSetStructs.For(variables)(_client, cancellationToken);

    public Task WriteLocalAsync(SimulatorVariable variable, double value, CancellationToken cancellationToken) =>
        _client.SimVars.SetAsync(variable.Name, variable.Unit, value, 0, cancellationToken);

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

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// One request at a time on this connection, each with its own request id. Packets arrive through the public
    /// <c>RawMessageReceived</c> event on the library's message thread; the handler reads the request id straight from
    /// native memory, copies only the packets of this request (the pointer is valid during the callback only), and hands
    /// them to a <see cref="LiveryListAssembly"/> under a lock. Continuations run elsewhere.
    /// </para>
    /// <para>
    /// The request ends when every packet 0 … <c>dwOutOf</c> − 1 has arrived, on the first malformed packet
    /// (<see cref="FormatException"/>), after <see cref="LiveryListTimeout"/> (<see cref="TimeoutException"/>: an
    /// incomplete list is never returned), or on cancellation. The handler is always detached.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<RawLiveryEntry>> RequestAircraftLiveriesAsync(CancellationToken cancellationToken)
    {
        await _liveryRequests.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var assembly = new LiveryListAssembly(unchecked((uint)Interlocked.Increment(ref _nextLiveryRequestId)));
            var gate = new object();
            var done = new TaskCompletionSource<IReadOnlyList<RawLiveryEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnRaw(object? sender, RawSimConnectMessageEventArgs e)
            {
                if (e.MessageId != SimConnectRecvId.EnumerateSimobjectAndLiveryList
                    || e.DataSize < LiveryListParser.HeaderSize
                    || unchecked((uint)System.Runtime.InteropServices.Marshal.ReadInt32(e.DataPointer, 12)) != assembly.RequestId)
                {
                    return; // another message, or another request's answer
                }

                var buffer = new byte[e.DataSize];
                System.Runtime.InteropServices.Marshal.Copy(e.DataPointer, buffer, 0, (int)e.DataSize);
                lock (gate)
                {
                    if (done.Task.IsCompleted)
                    {
                        return;
                    }

                    try
                    {
                        assembly.Accept(buffer);
                        if (assembly.IsComplete)
                        {
                            done.TrySetResult(assembly.ToList());
                        }
                    }
                    catch (FormatException ex)
                    {
                        done.TrySetException(ex);
                    }
                }
            }

            _client.RawMessageReceived += OnRaw;
            try
            {
                var hr = await LiveryInterop.EnumerateAsync(_client, assembly.RequestId, LiveryInterop.AircraftObjectType, cancellationToken).ConfigureAwait(false);
                if (hr != 0)
                {
                    throw new InvalidOperationException($"The simulator rejected the livery enumeration (HRESULT 0x{hr:X8}).");
                }

                try
                {
                    return await done.Task.WaitAsync(LiveryListTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    int received;
                    lock (gate)
                    {
                        received = assembly.PacketCount;
                    }

                    throw new TimeoutException($"The livery enumeration did not complete within {LiveryListTimeout.TotalSeconds:0} s ({received} packets received).");
                }
            }
            finally
            {
                _client.RawMessageReceived -= OnRaw;
            }
        }
        finally
        {
            _liveryRequests.Release();
        }
    }


    /// <inheritdoc />
    /// <remarks>
    /// BLOCK 10A.5, experimental. One probe at a time. The identity is read with the object's own id (never object 0);
    /// the user aircraft's title is read separately in the same cycle so the result can prove the two differ. The object
    /// is removed in a <c>finally</c>; removal is confirmed when a read of its id no longer answers.
    /// </remarks>
    public async Task<AiProbeResult> ProbeAiAircraftAsync(
        string containerTitle,
        string livery,
        string tailNumber,
        AiProbePosition position,
        TimeSpan settle,
        AiObjectLedger ledger,
        CancellationToken cancellationToken)
    {
        await _discoveryRequests.WaitAsync(cancellationToken).ConfigureAwait(false);
        var exceptions = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var createRequestId = unchecked((uint)Interlocked.Increment(ref _nextDiscoveryRequestId));
        var assigned = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnRaw(object? sender, RawSimConnectMessageEventArgs e)
        {
            if (e.MessageId == SimConnectRecvId.AssignedObjectId && e.DataSize >= 20)
            {
                var buffer = new byte[e.DataSize];
                System.Runtime.InteropServices.Marshal.Copy(e.DataPointer, buffer, 0, (int)e.DataSize);
                if (BitConverter.ToUInt32(buffer, 12) == createRequestId)
                {
                    assigned.TrySetResult(BitConverter.ToUInt32(buffer, 16));
                }
            }
            else if (e.MessageId == SimConnectRecvId.Exception && e.DataSize >= 24)
            {
                var buffer = new byte[e.DataSize];
                System.Runtime.InteropServices.Marshal.Copy(e.DataPointer, buffer, 0, (int)e.DataSize);
                exceptions.Enqueue($"exception {BitConverter.ToUInt32(buffer, 12)} (send id {BitConverter.ToUInt32(buffer, 16)}, index {BitConverter.ToUInt32(buffer, 20)})");
            }
        }

        _client.RawMessageReceived += OnRaw;
        var result = new AiProbeResult { ContainerTitle = containerTitle, Livery = livery, TailNumber = tailNumber };
        uint? objectId = null;
        var createSent = false;
        try
        {
            (result, objectId) = await CreateAndReadAsync(result).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = result with { Error = $"{ex.GetType().Name}: {ex.Message}" };
        }
        finally
        {
            // A late assignment (after the timeout, or after a cancellation) must not leave an object behind: wait for it
            // once more, independently of the caller's token, and remove whatever was assigned.
            if (objectId is null && createSent)
            {
                try
                {
                    objectId = await assigned.Task.WaitAsync(LiveryDiscoveryTimeout).ConfigureAwait(false);
                    ledger.RecordCreated(objectId.Value);
                    result = result with { ObjectId = objectId, Error = (result.Error ?? "cancelled") + "; object id assigned late, removed" };
                }
                catch (TimeoutException)
                {
                    // Nothing was ever assigned: nothing to remove.
                }
            }

            _client.RawMessageReceived -= OnRaw;
            if (objectId is { } id)
            {
                var (removeHr, confirmed, latency) = await RemoveAndConfirmAsync(id, ledger).ConfigureAwait(false);
                result = result with { RemoveHResult = removeHr, RemovalConfirmed = confirmed, RemoveLatency = latency };
            }

            _discoveryRequests.Release();
        }

        return result with { Exceptions = exceptions.ToArray() };

        async Task<(AiProbeResult Result, uint? ObjectId)> CreateAndReadAsync(AiProbeResult probe)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var initial = new SimConnectDataInitPosition
            {
                Latitude = position.LatitudeDegrees,
                Longitude = position.LongitudeDegrees,
                Altitude = position.AltitudeFeet,
                Pitch = 0,
                Bank = 0,
                Heading = position.HeadingDegrees,
                OnGround = position.OnGround ? 1u : 0u,
                Airspeed = 0,
            };
            var hr = await AiProbeInterop.CreateNonAtcAircraftAsync(_client, containerTitle, livery, tailNumber, initial, createRequestId, cancellationToken).ConfigureAwait(false);
            probe = probe with { CreateHResult = hr };
            if (hr != 0)
            {
                return (probe with { Error = $"create rejected (HRESULT 0x{hr:X8})" }, null);
            }

            createSent = true;

            uint id;
            try
            {
                id = await assigned.Task.WaitAsync(LiveryDiscoveryTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return (probe with { Error = "no object id assigned in time", CreateLatency = clock.Elapsed }, null);
            }

            ledger.RecordCreated(id);
            probe = probe with { ObjectId = id, CreateLatency = clock.Elapsed };

            try
            {
                await Task.Delay(settle, cancellationToken).ConfigureAwait(false);
                clock.Restart();
                var vars = await _client.SimVars.GetAsync<AiIdentityVars>(id, cancellationToken)
                    .WaitAsync(LiveryDiscoveryTimeout, cancellationToken).ConfigureAwait(false);
                var readLatency = clock.Elapsed;
                var user = await _client.SimVars.GetAsync<AiIdentityVars>(0, cancellationToken)
                    .WaitAsync(LiveryDiscoveryTimeout, cancellationToken).ConfigureAwait(false);
                return (probe with
                {
                    Identity = new RawObjectIdentity(Clean(vars.Title), Clean(vars.LiveryName), Clean(vars.LiveryFolder), Clean(vars.AtcId), Clean(vars.AtcAirline), Clean(vars.AtcModel), Clean(vars.AtcType)),
                    UserAircraftTitle = Clean(user.Title),
                    ReadLatency = readLatency,
                }, id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                return (probe with { Error = $"read failed: {ex.GetType().Name}: {ex.Message}" }, id);
            }
            catch (OperationCanceledException)
            {
                // Removal still happens in the caller's finally: hand the id back through the probe.
                objectId = id;
                throw;
            }
        }

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// BLOCK 10A.5 cleanup, deliberately independent of the caller's token: removes an experimental AI object and
    /// confirms it is gone (a read of its id no longer answers within 3 s).
    /// </summary>
    internal async Task<(int RemoveHResult, bool Confirmed, TimeSpan Latency)> RemoveAndConfirmAsync(uint objectId, AiObjectLedger ledger)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(LiveryDiscoveryTimeout);
        var removeRequestId = unchecked((uint)Interlocked.Increment(ref _nextDiscoveryRequestId));
        var hr = await AiProbeInterop.RemoveObjectAsync(_client, objectId, removeRequestId, timeout.Token).ConfigureAwait(false);
        var latency = clock.Elapsed;
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        bool stillThere;
        try
        {
            await _client.SimVars.GetAsync<AiIdentityVars>(objectId, timeout.Token).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            stillThere = true;
        }
        catch (Exception)
        {
            stillThere = false;
        }

        if (!stillThere)
        {
            ledger.RecordRemoved(objectId);
        }

        return (hr, !stillThere, latency);
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
/// The MSFS aircraft-identity SimVars, read as one batched request. The first four are the ones proven in the
/// audited applications (<c>AircraftIdentityVars</c> in FSHANGAR/FLIPPP). <c>ATC MODEL</c> and <c>ATC TYPE</c> were
/// added by the BLOCK 10A-LIVE audit (gap G-D1): they are the stock <c>aircraft.cfg</c> <c>atc_model</c> /
/// <c>atc_type</c> strings, the generic evidence an aircraft provider can use when the title alone is ambiguous.
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

    [SimConnect("ATC MODEL", SimConnectDataType.String256)]
    public string AtcModel;

    [SimConnect("ATC TYPE", SimConnectDataType.String256)]
    public string AtcType;
}

/// <summary>BLOCK 10A.5, experimental: the identity strings read from an AI probe object (and, for contrast, object 0).</summary>
internal struct AiIdentityVars
{
    [SimConnect("TITLE", SimConnectDataType.String256)]
    public string Title;

    [SimConnect("LIVERY NAME", SimConnectDataType.String256)]
    public string LiveryName;

    [SimConnect("LIVERY FOLDER", SimConnectDataType.String256)]
    public string LiveryFolder;

    [SimConnect("ATC ID", SimConnectDataType.String256)]
    public string AtcId;

    [SimConnect("ATC AIRLINE", SimConnectDataType.String256)]
    public string AtcAirline;

    [SimConnect("ATC MODEL", SimConnectDataType.String256)]
    public string AtcModel;

    [SimConnect("ATC TYPE", SimConnectDataType.String256)]
    public string AtcType;
}

#pragma warning restore CS0649
