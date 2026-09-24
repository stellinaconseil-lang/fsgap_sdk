using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FSGAP.Abstractions.Aircraft;
using FSGAP.Abstractions.Simulator;
using FSGAP.Abstractions.Telemetry;
using FSGAP.Fenix.Variables;
using Microsoft.Extensions.Time.Testing;

namespace FSGAP.Fenix.Tests;

/// <summary>Scriptable simulator variable reader: values by name, read log, optional failure and gate.</summary>
internal sealed class FakeVariableReader : ISimulatorVariableReader
{
    private int _reads;

    public ConcurrentDictionary<string, double> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

    public ConcurrentQueue<IReadOnlyList<SimulatorVariable>> Reads { get; } = new();

    public int ReadCount => Volatile.Read(ref _reads);

    /// <summary>When set, every read throws it (connection lost, unsupported variable...).</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, reads wait for it (a slow native read).</summary>
    public TaskCompletionSource? Gate { get; set; }

    public int ReadsOf(IReadOnlyList<SimulatorVariable> group) => Reads.Count(r => ReferenceEquals(r, group));

    public async Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref _reads);
        Reads.Enqueue(variables);
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        return variables.Select(v => Values.TryGetValue(v.Name, out var value) ? value : 0.0).ToArray();
    }

    /// <summary>A cockpit as the audited sessions left it: IRs in NAV, all six pumps on, handles stowed, lights off.</summary>
    public FakeVariableReader WithNominalCockpit()
    {
        foreach (var v in FenixVariables.Cockpit)
        {
            Values[v.Name] = 0.0;
        }

        Values["L:S_OH_NAV_IR1_MODE"] = 1;
        Values["L:S_OH_NAV_IR2_MODE"] = 1;
        Values["L:S_OH_NAV_IR3_MODE"] = 1;
        foreach (var pump in new[] { "LEFT_1", "LEFT_2", "CENTER_1", "CENTER_2", "RIGHT_1", "RIGHT_2" })
        {
            Values[$"L:S_OH_FUEL_{pump}"] = 1;
        }

        Values["HYDRAULIC PRESSURE:1"] = 3000;
        Values["HYDRAULIC PRESSURE:2"] = 2990;
        Values["HYDRAULIC RESERVOIR PERCENT:1"] = 99;
        Values["HYDRAULIC RESERVOIR PERCENT:2"] = 98;
        Values["ELECTRICAL BATTERY VOLTAGE:1"] = 28.2;
        return this;
    }
}

/// <summary>Settable aircraft detector.</summary>
internal sealed class FakeDetector : IAircraftDetector
{
    private volatile AircraftDescriptor? _current;

    public FakeDetector(AircraftDescriptor? current) => _current = current;

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

/// <summary>Generic telemetry double: a fixed snapshot restamped to "now" on every read, like the real transport.</summary>
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

/// <summary>Fake clock that counts timers, so a test can wait until the polling loops are parked on their delay.</summary>
internal sealed class CountingClock : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

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

/// <summary>Waits for background work without ever waiting for simulated time.</summary>
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

            await Task.Delay(2);
        }
    }

    /// <summary>Lets background work run, for "nothing more happened" assertions.</summary>
    public static Task SettleAsync() => Task.Delay(50);
}
