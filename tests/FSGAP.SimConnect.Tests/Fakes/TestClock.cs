using Microsoft.Extensions.Time.Testing;

namespace FSGAP.SimConnect.Tests.Fakes;

/// <summary>
/// Fake clock that also counts the timers created through it, so a test can wait until the transport has scheduled
/// a delay before advancing time. No real waiting is involved.
/// </summary>
internal sealed class TestClock : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _inner = new(Start);
    private int _timersCreated;

    public int TimersCreated => Volatile.Read(ref _timersCreated);

    public override long TimestampFrequency => _inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => _inner.LocalTimeZone;

    public override DateTimeOffset GetUtcNow() => _inner.GetUtcNow();

    public override long GetTimestamp() => _inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = _inner.CreateTimer(callback, state, dueTime, period);
        Interlocked.Increment(ref _timersCreated);
        return timer;
    }

    public void Advance(TimeSpan delta) => _inner.Advance(delta);

    /// <summary>Waits (without advancing time) until at least <paramref name="count"/> timers were created.</summary>
    public Task WaitForTimersAsync(int count) => Eventually.TrueAsync(() => TimersCreated >= count, $"{count} timers");
}
