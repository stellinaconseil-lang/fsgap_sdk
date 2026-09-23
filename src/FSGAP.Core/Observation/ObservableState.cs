using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace FSGAP.Core.Observation;

/// <summary>
/// Thread-safe holder of a current value that any number of observers can watch as an
/// <see cref="IAsyncEnumerable{T}"/>. Implementations of the FSGAP <c>Watch*Async</c> contracts build on it.
/// </summary>
/// <remarks>
/// Each watcher first receives the value current at subscription time, then every change. Delivery is
/// "latest value wins": a slow watcher may skip intermediate values but never receives a stale one after a newer
/// one, and never slows down <see cref="Set"/>. Setting a value equal to the current one notifies nobody.
/// </remarks>
/// <typeparam name="T">Value type; compared with the supplied or default equality comparer.</typeparam>
public sealed class ObservableState<T>
{
    private static readonly BoundedChannelOptions WatcherChannelOptions = new(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    };

    private readonly object _gate = new();
    private readonly List<Channel<T>> _watchers = [];
    private readonly IEqualityComparer<T> _comparer;
    private T _current;
    private bool _completed;

    /// <summary>Creates the holder.</summary>
    /// <param name="initial">Initial value.</param>
    /// <param name="comparer">Equality used to detect changes; <see cref="EqualityComparer{T}.Default"/> when <see langword="null"/>.</param>
    public ObservableState(T initial, IEqualityComparer<T>? comparer = null)
    {
        _current = initial;
        _comparer = comparer ?? EqualityComparer<T>.Default;
    }

    /// <summary>Current value.</summary>
    public T Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Replaces the current value and notifies watchers if it changed.</summary>
    /// <returns><see langword="true"/> when the value changed.</returns>
    /// <exception cref="InvalidOperationException">The holder has been completed.</exception>
    public bool Set(T value)
    {
        lock (_gate)
        {
            if (_completed)
            {
                throw new InvalidOperationException("The observable state has been completed.");
            }

            if (_comparer.Equals(_current, value))
            {
                return false;
            }

            _current = value;
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryWrite(value);
            }

            return true;
        }
    }

    /// <summary>Ends every current and future observation; the enumerations complete normally.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryComplete();
            }

            _watchers.Clear();
        }
    }

    /// <summary>
    /// Yields the current value, then each change, until <paramref name="cancellationToken"/> is cancelled (throws
    /// <see cref="OperationCanceledException"/>) or <see cref="Complete"/> is called (completes).
    /// </summary>
    /// <param name="cancellationToken">Ends the observation.</param>
    public async IAsyncEnumerable<T> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<T>(WatcherChannelOptions);
        lock (_gate)
        {
            channel.Writer.TryWrite(_current);
            if (_completed)
            {
                channel.Writer.TryComplete();
            }
            else
            {
                _watchers.Add(channel);
            }
        }

        try
        {
            await foreach (var value in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return value;
            }
        }
        finally
        {
            lock (_gate)
            {
                _watchers.Remove(channel);
            }
        }
    }
}
