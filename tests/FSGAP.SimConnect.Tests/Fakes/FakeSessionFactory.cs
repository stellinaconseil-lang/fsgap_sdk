using FSGAP.SimConnect.Native;

namespace FSGAP.SimConnect.Tests.Fakes;

/// <summary>
/// Scriptable connection opener. Each attempt consumes the next scripted outcome; when the script is empty the
/// simulator is "not running".
/// </summary>
internal sealed class FakeSessionFactory : ISimConnectSessionFactory
{
    private readonly object _gate = new();
    private readonly Queue<Func<CancellationToken, Task<ISimConnectSession>>> _script = new();
    private readonly List<FakeSession> _sessions = [];
    private int _attempts;

    public int Attempts => Volatile.Read(ref _attempts);

    public string? LastApplicationName { get; private set; }

    /// <summary>Highest number of sessions alive (opened and not disposed) at the moment a new one was opened.</summary>
    public int MaxLiveSessions { get; private set; }

    public IReadOnlyList<FakeSession> Sessions
    {
        get { lock (_gate) { return _sessions.ToArray(); } }
    }

    public FakeSessionFactory SimulatorAbsent()
    {
        Enqueue(_ => Task.FromException<ISimConnectSession>(new SimulatorUnavailableException("not running")));
        return this;
    }

    /// <summary>The next attempt succeeds, optionally only once <paramref name="openWhen"/> completes.</summary>
    public FakeSession SimulatorPresent(RawAircraftIdentity identity = default, Task? openWhen = null)
    {
        var created = new FakeSession { Identity = identity };
        Enqueue(async ct =>
        {
            if (openWhen is not null)
            {
                await openWhen.WaitAsync(ct);
            }

            lock (_gate)
            {
                _sessions.Add(created);
                MaxLiveSessions = Math.Max(MaxLiveSessions, _sessions.Count(s => !s.IsDisposed));
            }

            return created;
        });
        return created;
    }

    public FakeSessionFactory Throws(Exception exception)
    {
        Enqueue(_ => Task.FromException<ISimConnectSession>(exception));
        return this;
    }

    /// <summary>The next attempt never completes until cancelled (a hanging native open).</summary>
    public FakeSessionFactory Hangs()
    {
        Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public Task<ISimConnectSession> ConnectAsync(string applicationName, CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<ISimConnectSession>>? next;
        lock (_gate)
        {
            LastApplicationName = applicationName;
            next = _script.Count > 0 ? _script.Dequeue() : null;
        }

        Interlocked.Increment(ref _attempts);
        return next is null
            ? Task.FromException<ISimConnectSession>(new SimulatorUnavailableException("not running"))
            : next(cancellationToken);
    }

    private void Enqueue(Func<CancellationToken, Task<ISimConnectSession>> outcome)
    {
        lock (_gate)
        {
            _script.Enqueue(outcome);
        }
    }
}
