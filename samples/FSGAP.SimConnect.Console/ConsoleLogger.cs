using Microsoft.Extensions.Logging;

namespace FSGAP.SimConnect.Console;

/// <summary>Minimal console logger for the sample (Information and above), to avoid an extra logging package.</summary>
internal sealed class ConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var error = exception is null ? string.Empty : $" -- {exception.GetType().Name}: {exception.Message}";
        System.Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [log:{logLevel,-11}] {formatter(state, exception)}{error}");
    }
}
