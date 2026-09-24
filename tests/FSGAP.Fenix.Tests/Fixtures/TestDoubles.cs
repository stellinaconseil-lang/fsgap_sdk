using FSGAP.Abstractions.Aircraft;
using Microsoft.Extensions.Logging;

namespace FSGAP.Fenix.Tests.Fixtures;

/// <summary>In-memory installed catalog for provider tests.</summary>
internal sealed class InMemoryCatalog(params InstalledAircraft[] aircraft) : IInstalledAircraftCatalog
{
    public Exception? Failure { get; init; }

    public Task<IReadOnlyList<InstalledAircraft>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstalledAircraft>>(aircraft);

    public Task<InstalledAircraft?> FindByLiveryFolderAsync(string liveryFolder, CancellationToken cancellationToken = default) =>
        Failure is not null
            ? Task.FromException<InstalledAircraft?>(Failure)
            : Task.FromResult(aircraft.SingleOrDefault(a => string.Equals(a.LiveryFolder, liveryFolder, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<InstalledAircraft>> FindByRegistrationAsync(string registration, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<CatalogScanResult> RefreshAsync(IProgress<CatalogScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public static InstalledAircraft Livery(string folder, string? registration, string? model = "A320", string? engine = null, string? wingtip = null, string? operatorIcao = null, string? name = null) => new()
    {
        Id = "test:" + folder,
        LiveryFolder = folder,
        Identity = new AircraftIdentity
        {
            Developer = "Fenix Simulations",
            Manufacturer = "Airbus",
            Family = "A320",
            Model = model,
            IcaoType = model,
            EngineVariant = engine,
            WingtipConfiguration = wingtip,
            Registration = registration,
            Livery = name,
            OperatorIcao = operatorIcao,
        },
    };
}

/// <summary>Captures log entries.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(LogLevel Level, string Message)> Entries
    {
        get { lock (_entries) { return _entries.ToArray(); } }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

/// <summary>Collects progress reports synchronously (unlike <see cref="Progress{T}"/>, which posts them).</summary>
internal sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = [];

    public IReadOnlyList<T> Reports
    {
        get { lock (_reports) { return _reports.ToArray(); } }
    }

    public Action<T>? OnReport { get; init; }

    public void Report(T value)
    {
        lock (_reports)
        {
            _reports.Add(value);
        }

        OnReport?.Invoke(value);
    }
}
