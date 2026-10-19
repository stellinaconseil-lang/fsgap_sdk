using SimConnect.NET;

namespace FSGAP.SimConnect.Native;

/// <summary>What ended a native connection.</summary>
internal enum SessionEndReason
{
    /// <summary><c>SIMCONNECT_RECV_ID_QUIT</c> received.</summary>
    Quit,

    /// <summary>The library reported the connection closed.</summary>
    LibraryStatus,
}

/// <summary>
/// Whether one native connection is still alive, from the two signals that actually report its end.
/// </summary>
/// <remarks>
/// <para>
/// SimConnect.NET 0.2.2 (decompiled, <c>SimConnectClient.ProcessNextMessageAsync</c> and
/// <c>StartMessageProcessingLoopAsync</c>) does not report the simulator closing: <c>SIMCONNECT_RECV_ID_QUIT</c> falls
/// through to the SimVar manager and is dropped, a failing <c>SimConnect_GetNextDispatch</c> is read as "no message",
/// and <c>IsConnected</c> / <c>ConnectionStatusChanged(false)</c> only change on an explicit disconnect or with the
/// library's own auto-reconnect, which FSGAP keeps off. Live 2026-10-13 (live capture 2026-10-13):
/// MSFS 2024 closed by the user, the host stayed Connected for more than ten minutes and never reconnected.
/// </para>
/// <para>
/// The documented end of a SimConnect connection is the QUIT message, so it is observed here directly. The library's
/// own status change still counts. Silence is NOT a signal: a stable aircraft may stop publishing, and a read that
/// times out may be a loading screen.
/// </para>
/// <para>The counters and the end reason are evidence for live qualification only; they change no behaviour.</para>
/// </remarks>
internal sealed class SessionLiveness(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private int _lost;
    private int _quitMessages;
    private SessionEndReason? _reason;
    private DateTimeOffset? _endedAt;

    /// <summary>Raised once, on the native message thread, when the connection ends.</summary>
    public event Action? Lost;

    /// <summary>Whether an end of connection was observed.</summary>
    public bool IsLost => Volatile.Read(ref _lost) != 0;

    /// <summary>QUIT messages received on this connection (normally 0 or 1).</summary>
    public int QuitMessages => Volatile.Read(ref _quitMessages);

    /// <summary>The first end signal observed, and when; null while alive.</summary>
    public (SessionEndReason Reason, DateTimeOffset At)? End
    {
        get
        {
            lock (_gate)
            {
                return _reason is { } reason && _endedAt is { } at ? (reason, at) : null;
            }
        }
    }

    /// <summary>A message was received on the connection.</summary>
    public void OnMessage(SimConnectRecvId messageId)
    {
        if (messageId == SimConnectRecvId.Quit)
        {
            Interlocked.Increment(ref _quitMessages);
            MarkLost(SessionEndReason.Quit);
        }
    }

    /// <summary>The library reported a connection status.</summary>
    public void OnLibraryStatus(bool isConnected)
    {
        if (!isConnected)
        {
            MarkLost(SessionEndReason.LibraryStatus);
        }
    }

    private void MarkLost(SessionEndReason reason)
    {
        if (Interlocked.Exchange(ref _lost, 1) == 0)
        {
            lock (_gate)
            {
                _reason = reason;
                _endedAt = _time.GetUtcNow();
            }

            Lost?.Invoke();
        }
    }
}
