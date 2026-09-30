// BLOCK 10B.3 live qualification support (read-only). Instruments what the production providers do on the single
// SimConnectSimulator connection: every variable read (Synaptic A22X vs other), the raw Synaptic values as the provider
// received them (logged on change, for the APU switch sequence), and every Fenix EFB request. Nothing here writes to
// the aircraft; the instrumentation only observes calls made by the providers themselves.
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using FSGAP.Abstractions.Simulator;

namespace FSGAP.SimConnect.Console;

/// <summary>A pass-through variable reader that counts and logs what the providers read.</summary>
internal sealed class QualificationReader(ISimulatorVariableReader inner, Action<string, string> print) : ISimulatorVariableReader
{
    private readonly ConcurrentDictionary<string, double> _lastSynaptic = new(StringComparer.Ordinal);
    private long _synapticReads;
    private long _otherReads;
    private long _synapticVariables;
    private long _otherVariables;

    public long SynapticReads => Interlocked.Read(ref _synapticReads);

    public long OtherReads => Interlocked.Read(ref _otherReads);

    public long SynapticVariables => Interlocked.Read(ref _synapticVariables);

    public long OtherVariables => Interlocked.Read(ref _otherVariables);

    public async Task<IReadOnlyList<double>> ReadAsync(IReadOnlyList<SimulatorVariable> variables, CancellationToken cancellationToken = default)
    {
        var synaptic = variables.Any(v => v.Name.StartsWith("L:A22X ", StringComparison.Ordinal));
        if (synaptic)
        {
            Interlocked.Increment(ref _synapticReads);
            Interlocked.Add(ref _synapticVariables, variables.Count);
        }
        else
        {
            Interlocked.Increment(ref _otherReads);
            Interlocked.Add(ref _otherVariables, variables.Count);
        }

        var values = await inner.ReadAsync(variables, cancellationToken).ConfigureAwait(false);
        if (synaptic)
        {
            for (var i = 0; i < variables.Count && i < values.Count; i++)
            {
                var name = variables[i].Name;
                if (!_lastSynaptic.TryGetValue(name, out var previous) || !previous.Equals(values[i]))
                {
                    _lastSynaptic[name] = values[i];
                    print("raw.a22x", string.Create(CultureInfo.InvariantCulture, $"{name} = {values[i]:R}"));
                }
            }
        }

        return values;
    }
}

/// <summary>Counts and logs Fenix EFB requests (the only HTTP the providers can issue).</summary>
internal sealed class QualificationEfbHandler(Action<string, string> print) : DelegatingHandler(new HttpClientHandler())
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        print("efb", $"{request.Method} {request.RequestUri}");
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Tees the console output to a log file.</summary>
internal static class QualificationLog
{
    private static readonly object Gate = new();
    private static StreamWriter? _writer;

    public static void Open(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"qualification-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        _writer = new StreamWriter(path, append: false) { AutoFlush = true };
        System.Console.WriteLine($"logging to {path}");
    }

    public static void Write(string line)
    {
        lock (Gate)
        {
            System.Console.WriteLine(line);
            _writer?.WriteLine(line);
        }
    }

    public static void WriteJson(string directory, string name, object value) =>
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
}
