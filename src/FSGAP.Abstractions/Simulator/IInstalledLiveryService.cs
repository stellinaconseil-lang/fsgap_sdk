namespace FSGAP.Abstractions.Simulator;

/// <summary>
/// Lists the aircraft liveries the simulator can load: every (aircraft title, livery name) pair it reports, including
/// content the local file system cannot read (for example streamed marketplace packages). Read-only.
/// </summary>
/// <remarks>
/// <para>
/// This is exactly what the simulator enumerates, and nothing more: no registration, no operator, no livery folder, no
/// vendor or model interpretation. Recognizing which rows belong to an aircraft family, merging rows that describe the
/// same livery under several presets, and resolving registrations are the business of the aircraft providers.
/// </para>
/// <para>
/// Results are faithful: rows come in the simulator's order, and duplicates are kept. Outcomes are distinct, as for
/// <see cref="IAirportService"/>: an empty list means the simulator answered with nothing; a
/// <see cref="SimulatorServiceException"/> means it could not be asked (<see cref="SimulatorServiceError.SimulatorUnavailable"/>)
/// or did not answer completely and usably in time (<see cref="SimulatorServiceError.QueryFailed"/>). Thread-safe.
/// </para>
/// </remarks>
public interface IInstalledLiveryService
{
    /// <summary>Every aircraft livery the simulator reports, in its order, duplicates included.</summary>
    /// <param name="cancellationToken">Cancels the wait for the answer.</param>
    /// <exception cref="SimulatorServiceException">The simulator is not connected, or the query failed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<IReadOnlyList<InstalledLivery>> GetInstalledAircraftLiveriesAsync(CancellationToken cancellationToken = default);
}

/// <summary>One aircraft livery as the simulator enumerates it.</summary>
public sealed record InstalledLivery
{
    /// <summary>
    /// Title of the aircraft (the container title the simulator loads, for example the <c>title</c> of a preset in
    /// <c>aircraft.cfg</c>), trimmed. Never empty: rows without a title are not reported.
    /// </summary>
    public required string AircraftTitle { get; init; }

    /// <summary>
    /// Name of the livery (the <c>name</c> of its <c>livery.cfg</c>), trimmed; <see langword="null"/> when the simulator
    /// reports an empty name (typically the aircraft's default, unnamed livery).
    /// </summary>
    public string? LiveryName { get; init; }
}
