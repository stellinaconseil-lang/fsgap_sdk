namespace FSGAP.SimConnect.Native;

// BLOCK 10A.5 — EXPERIMENTAL, diagnostic only. Types of the AI-probe experiment (temporary AI aircraft created to read a
// livery's identity strings). Internal; no production path uses them, nothing here is public, and BLOCK 10A.5 concluded
// that AI probing is not a production technique. Livery enumeration itself is production code: see LiveryList.cs.

/// <summary>Where to place a probe aircraft.</summary>
internal sealed record AiProbePosition(double LatitudeDegrees, double LongitudeDegrees, double AltitudeFeet, double HeadingDegrees, bool OnGround);

/// <summary>The identity strings read from one SimObject.</summary>
internal sealed record RawObjectIdentity(
    string? Title,
    string? LiveryName,
    string? LiveryFolder,
    string? AtcId,
    string? AtcAirline,
    string? AtcModel,
    string? AtcType);

/// <summary>One create / read / remove cycle.</summary>
internal sealed record AiProbeResult
{
    public required string ContainerTitle { get; init; }

    public required string Livery { get; init; }

    public required string TailNumber { get; init; }

    public int CreateHResult { get; init; }

    public uint? ObjectId { get; init; }

    public IReadOnlyList<string> Exceptions { get; init; } = [];

    public RawObjectIdentity? Identity { get; init; }

    /// <summary>The user aircraft's title read in the same cycle, to prove the probe did not read object 0.</summary>
    public string? UserAircraftTitle { get; init; }

    public TimeSpan CreateLatency { get; init; }

    public TimeSpan ReadLatency { get; init; }

    public int? RemoveHResult { get; init; }

    public bool RemovalConfirmed { get; init; }

    public TimeSpan RemoveLatency { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Bookkeeping of the AI objects the experiment created, so that nothing is left in the simulator: every created id is
/// recorded before anything else happens to it, and only a confirmed removal clears it.
/// </summary>
internal sealed class AiObjectLedger
{
    private readonly object _gate = new();
    private readonly HashSet<uint> _alive = [];
    private int _created;
    private int _removed;

    public int Created { get { lock (_gate) { return _created; } } }

    public int Removed { get { lock (_gate) { return _removed; } } }

    public IReadOnlyList<uint> Remaining { get { lock (_gate) { return _alive.Order().ToArray(); } } }

    public void RecordCreated(uint objectId)
    {
        lock (_gate)
        {
            if (_alive.Add(objectId))
            {
                _created++;
            }
        }
    }

    public void RecordRemoved(uint objectId)
    {
        lock (_gate)
        {
            if (_alive.Remove(objectId))
            {
                _removed++;
            }
        }
    }
}
