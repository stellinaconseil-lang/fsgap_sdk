using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Tests.Fakes;

/// <summary>Scriptable native connection. The test thread plays the role of the native message thread.</summary>
internal sealed class FakeSession : ISimConnectSession
{
    [ThreadStatic]
    private static bool _inNativeCallback;

    private readonly object _gate = new();
    private readonly List<Delegate> _everSubscribed = [];
    private Action? _disconnected;
    private Action? _crashed;
    private Action<bool>? _pauseChanged;
    private RawAircraftIdentity _identity;
    private int _identityReads;
    private int _disposeCount;
    private volatile bool _connected = true;

    /// <summary>True on the current thread while a native callback is being delivered.</summary>
    public static bool InNativeCallback => _inNativeCallback;

    public event Action? Disconnected
    {
        add { lock (_gate) { _disconnected += value; } }
        remove { lock (_gate) { _disconnected -= value; } }
    }

    public event Action? Crashed
    {
        add { lock (_gate) { _crashed += value; Remember(value); } }
        remove { lock (_gate) { _crashed -= value; } }
    }

    public event Action<bool>? PauseChanged
    {
        add { lock (_gate) { _pauseChanged += value; Remember(value); } }
        remove { lock (_gate) { _pauseChanged -= value; } }
    }

    public bool Connected
    {
        get => _connected;
        set => _connected = value;
    }

    public HashSet<SimulatorSystemEvent> FailingSubscriptions { get; } = [];

    public List<SimulatorSystemEvent> Subscriptions { get; } = [];

    public Exception? IdentityFailure { get; set; }

    public bool IsConnected => _connected;

    public int IdentityReads => Volatile.Read(ref _identityReads);

    public int DisposeCount => Volatile.Read(ref _disposeCount);

    public bool IsDisposed => DisposeCount > 0;

    public bool HasHandlers
    {
        get { lock (_gate) { return _disconnected is not null || _crashed is not null || _pauseChanged is not null; } }
    }

    public RawAircraftIdentity Identity
    {
        get { lock (_gate) { return _identity; } }
        set { lock (_gate) { _identity = value; } }
    }

    public void Drop()
    {
        _connected = false;
        Deliver(() => Snapshot(() => _disconnected)?.Invoke());
    }

    public void RaiseCrash() => Deliver(() => Snapshot(() => _crashed)?.Invoke());

    public void RaisePause(bool paused) => Deliver(() => Snapshot(() => _pauseChanged)?.Invoke(paused));

    /// <summary>Simulates a late native callback delivered to handlers that were already unsubscribed.</summary>
    public void RaiseCrashIgnoringUnsubscription() => Deliver(() => Remembered<Action>().ForEach(h => h()));

    /// <summary>Same as <see cref="RaiseCrashIgnoringUnsubscription"/> for pause notifications.</summary>
    public void RaisePauseIgnoringUnsubscription(bool paused) => Deliver(() => Remembered<Action<bool>>().ForEach(h => h(paused)));

    public Task SubscribeAsync(SimulatorSystemEvent systemEvent, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            Subscriptions.Add(systemEvent);
        }

        return FailingSubscriptions.Contains(systemEvent)
            ? Task.FromException(new InvalidOperationException($"{systemEvent} subscription refused"))
            : Task.CompletedTask;
    }

    public Task<RawAircraftIdentity> ReadAircraftIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _identityReads);
        return IdentityFailure is { } failure ? Task.FromException<RawAircraftIdentity>(failure) : Task.FromResult(Identity);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Increment(ref _disposeCount);
        _connected = false;
        return ValueTask.CompletedTask;
    }

    private static void Deliver(Action callback)
    {
        _inNativeCallback = true;
        try
        {
            callback();
        }
        finally
        {
            _inNativeCallback = false;
        }
    }

    private T? Snapshot<T>(Func<T?> read)
    {
        lock (_gate)
        {
            return read();
        }
    }

    private void Remember(Delegate? handler)
    {
        if (handler is not null)
        {
            _everSubscribed.Add(handler);
        }
    }

    private List<T> Remembered<T>()
        where T : Delegate
    {
        lock (_gate)
        {
            return _everSubscribed.OfType<T>().ToList();
        }
    }
}
