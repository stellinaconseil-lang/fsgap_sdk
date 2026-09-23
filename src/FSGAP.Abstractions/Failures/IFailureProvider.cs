namespace FSGAP.Abstractions.Failures;

/// <summary>Reads, triggers and clears normalized failures on the attached aircraft.</summary>
/// <remarks>
/// Check <see cref="Capabilities.FailureCapabilities"/> before calling: an unsupported read throws, while an
/// unsupported trigger or clear returns <see cref="FailureCommandStatus.NotSupported"/>.
/// </remarks>
public interface IFailureProvider
{
    /// <summary>Reads the failures currently active on the aircraft.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="NotSupportedException">
    /// The provider cannot read active failures. An empty result always means "no active failure", never "cannot tell".
    /// </exception>
    Task<IReadOnlyCollection<AircraftFailure>> GetActiveFailuresAsync(CancellationToken cancellationToken = default);

    /// <summary>Triggers a failure.</summary>
    /// <param name="command">Failure to trigger.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<FailureCommandResult> TriggerAsync(FailureCommand command, CancellationToken cancellationToken = default);

    /// <summary>Clears a failure.</summary>
    /// <param name="command">Failure to clear.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    Task<FailureCommandResult> ClearAsync(FailureCommand command, CancellationToken cancellationToken = default);
}
