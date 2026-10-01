namespace FSGAP.Abstractions.Degradations;

/// <summary>
/// Applies, restores and reads controlled degradations on the attached aircraft. A controlled degradation is a documented
/// aircraft system control deliberately forced into a degraded configuration by FSGAP; it is distinct from a failure
/// (<see cref="Failures.IFailureProvider"/>), which is a component the aircraft itself represents as failed.
/// </summary>
/// <remarks>
/// <para>
/// The degradations a provider supports are published in <see cref="Capabilities.DegradationCapabilities"/>. A command for
/// a key outside that catalog answers <see cref="DegradationCommandStatus.NotSupported"/> without contacting the aircraft.
/// </para>
/// <para>
/// Every write is an explicit set of a qualified value (never a toggle) and succeeds only once read back. FSGAP tracks what
/// <em>this session</em> applied: a control already degraded before FSGAP touched it is reported as
/// <see cref="DegradationState.PreExisting"/> and is never applied, restored or released by FSGAP.
/// </para>
/// </remarks>
public interface IDegradationProvider
{
    /// <summary>Reads the current state of the control behind <paramref name="key"/>.</summary>
    /// <param name="key">A key of the provider's catalog.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The observed state; <see cref="DegradationState.Unavailable"/> when the control cannot be read now.</returns>
    /// <exception cref="NotSupportedException">The provider cannot read this degradation's state.</exception>
    Task<DegradationState> GetStateAsync(DegradationKey key, CancellationToken cancellationToken = default);

    /// <summary>Forces the control behind <paramref name="key"/> into its degraded value, then reads it back.</summary>
    /// <param name="key">A key of the provider's catalog.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<DegradationCommandResult> ApplyAsync(DegradationKey key, CancellationToken cancellationToken = default);

    /// <summary>Restores the control behind <paramref name="key"/> to its qualified normal value, then reads it back.</summary>
    /// <param name="key">A key of the provider's catalog, applied by this session.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<DegradationCommandResult> RestoreAsync(DegradationKey key, CancellationToken cancellationToken = default);
}
