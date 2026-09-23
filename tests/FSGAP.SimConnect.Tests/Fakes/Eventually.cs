namespace FSGAP.SimConnect.Tests.Fakes;

/// <summary>
/// Waits for asynchronous work to reach a point. It never waits for simulated time: time only moves when a test
/// advances the <see cref="TestClock"/>. The timeout guards against hangs and is never part of an assertion.
/// </summary>
internal static class Eventually
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static async Task TrueAsync(Func<bool> condition, string what)
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

    /// <summary>Gives background work a chance to run, to support "nothing more happened" assertions.</summary>
    public static Task SettleAsync() => Task.Delay(50);
}
