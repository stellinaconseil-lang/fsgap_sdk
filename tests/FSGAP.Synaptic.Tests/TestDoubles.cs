using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Synaptic.Tests;

/// <summary>The live-qualified descriptors (BLOCK 10A-LIVE) and look-alikes.</summary>
internal static class Descriptors
{
    public static AircraftDescriptor A220(string title = "A220-300 - No Cabin", string? folder = "AIR FRANCE F-HZUF", string? livery = "Air France A220-300", string? atcId = null) =>
        new() { Title = title, Model = "A220-300", Manufacturer = "223", LiveryFolder = folder, Livery = livery, Registration = atcId };

    public static readonly AircraftDescriptor Delta = A220("A220-300", "DELTA N324DU", "Delta A220-300", "C-FFCO");
    public static readonly AircraftDescriptor AirBaltic = A220("A220-300 - No Cabin", "AIR BALTIC YL-CSM", "Air Baltic A220-300", "C-FFCO");
    public static readonly AircraftDescriptor AirFrance = A220();

    public static readonly AircraftDescriptor FenixA319 = new() { Title = "FenixA319 CFM SL", LiveryFolder = "AFR-F-GRHZ-0001", Livery = "Air France" };
    public static readonly AircraftDescriptor FenixA320 = new() { Title = "FenixA320 IAE WF", LiveryFolder = "BAW-G-EUYA-0002", Livery = "British Airways" };
    public static readonly AircraftDescriptor FenixA321 = new() { Title = "FenixA321 CFM WF SC", Registration = "F-GMZC", LiveryFolder = "AFR-F-GMZC-8761", Livery = "Air France 'Standard' F-GMZC (2026)" };
    public static readonly AircraftDescriptor IniBuildsA380 = new() { Title = "A380-800 EA Basic", Model = "A380-800", Manufacturer = "Airbus", Registration = "ASXGS", LiveryFolder = "INIBUILDS", Livery = "IniBuilds A6-INI" };
    public static readonly AircraftDescriptor Cessna = new() { Title = "Cessna Skyhawk G1000 Asobo", Model = "C172", Manufacturer = "Cessna" };
}

/// <summary>Scriptable simulator variable reader: values by name, read log, optional failure.</summary>
internal sealed class FakeVariableReader : ISimulatorVariableReader
{
    private int _reads;

    public ConcurrentDictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<IReadOnlyList<SimulatorVariable>> Reads { get; } = new();

    public int ReadCount => Volatile.Read(ref _reads);

    public Exception? Failure { get; set; }

    public int ReadsWhere(Func<SimulatorVariable, bool> predicate) => Reads.Count(r => r.Any(predicate));

    public Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _reads);
        Reads.Enqueue(variables);
        if (Failure is { } failure)
        {
            return Task.FromException<IReadOnlyList<double>>(failure);
        }

        return Task.FromResult<IReadOnlyList<double>>(variables.Select(v => Values.TryGetValue(v.Name, out var value) ? value : 0.0).ToArray());
    }
}

/// <summary>Settable aircraft detector.</summary>
internal sealed class FakeDetector(AircraftDescriptor? current) : IAircraftDetector
{
    private volatile AircraftDescriptor? _current = current;

    public AircraftDescriptor? Current
    {
        get => _current;
        set => _current = value;
    }

    public async IAsyncEnumerable<AircraftDescriptor?> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return _current;
    }
}

/// <summary>Generic telemetry double: a fixed snapshot restamped to "now" on every read.</summary>
internal sealed class StampedGenericTelemetry(TimeProvider time, AircraftTelemetry? template = null) : ITelemetryProvider
{
    public Task<AircraftTelemetry> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult((template ?? AircraftTelemetry.Unavailable(time.GetUtcNow())) with { Timestamp = time.GetUtcNow() });

    public async IAsyncEnumerable<AircraftTelemetry> StreamAsync(
        TelemetryStreamOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return await GetSnapshotAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(5), cancellationToken);
        }
    }
}

/// <summary>Scriptable livery enumeration.</summary>
internal sealed class FakeLiveryService : IInstalledLiveryService
{
    public IReadOnlyList<InstalledLivery> Rows { get; set; } = [];

    public Exception? Failure { get; set; }

    public int Calls { get; private set; }

    public Task<IReadOnlyList<InstalledLivery>> GetInstalledAircraftLiveriesAsync(CancellationToken cancellationToken = default)
    {
        Calls++;
        return Failure is { } failure ? Task.FromException<IReadOnlyList<InstalledLivery>>(failure) : Task.FromResult(Rows);
    }
}

/// <summary>Fake clock that counts timers, so a test can wait until polling loops are parked on their delay.</summary>
internal sealed class CountingClock : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _inner = new(Start);
    private int _timers;

    public int TimersCreated => Volatile.Read(ref _timers);

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    public override long GetTimestamp() => _inner.GetTimestamp();

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = _inner.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timers);
        return timer;
    }

    public void Advance(TimeSpan delta) => _inner.Advance(delta);
}

/// <summary>Waits for background work without waiting for simulated time.</summary>
internal static class Wait
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(5);
        }
    }

    /// <summary>Gives background loops time to run without advancing the fake clock (to prove nothing happens).</summary>
    public static Task SettleAsync() => Task.Delay(100);
}

/// <summary>A temporary data directory, deleted with the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "fsgap-synaptic-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
    }
}

/// <summary>
/// Scriptable local-variable writer: records every write and, like the simulator, makes the value readable through the
/// linked reader (unless <see cref="Ignore"/> is set, which models a write the aircraft does not take).
/// </summary>
internal sealed class FakeVariableWriter(FakeVariableReader reader) : ISimulatorVariableWriter
{
    public ConcurrentQueue<(SimulatorVariable Variable, double Value)> Writes { get; } = new();

    public Exception? Failure { get; set; }

    public bool Ignore { get; set; }

    /// <summary>When set, writes succeed this many times, then every write throws <see cref="FailAfterWith"/>.</summary>
    public int? FailAfter { get; set; }

    public Exception FailAfterWith { get; set; } = new InvalidOperationException("The simulator is not connected.");

    public Task WriteAsync(SimulatorVariable variable, double value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is { } failure)
        {
            return Task.FromException(failure);
        }

        if (FailAfter is { } limit && Writes.Count >= limit)
        {
            return Task.FromException(FailAfterWith);
        }

        Writes.Enqueue((variable, value));
        if (!Ignore)
        {
            reader.Values[variable.Name] = value;
        }

        return Task.CompletedTask;
    }
}

/// <summary>Aircraft detector that streams every change to its watchers (connection loss = <see langword="null"/>).</summary>
internal sealed class StreamingDetector(AircraftDescriptor? current) : IAircraftDetector
{
    private readonly object _gate = new();
    private readonly List<System.Threading.Channels.Channel<AircraftDescriptor?>> _watchers = [];
    private AircraftDescriptor? _current = current;

    public int Watchers
    {
        get
        {
            lock (_gate)
            {
                return _watchers.Count;
            }
        }
    }

    public AircraftDescriptor? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Publish(AircraftDescriptor? aircraft)
    {
        lock (_gate)
        {
            _current = aircraft;
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryWrite(aircraft);
            }
        }
    }

    public async IAsyncEnumerable<AircraftDescriptor?> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<AircraftDescriptor?>();
        lock (_gate)
        {
            _watchers.Add(channel);
            channel.Writer.TryWrite(_current);
        }

        try
        {
            await foreach (var aircraft in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return aircraft;
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
