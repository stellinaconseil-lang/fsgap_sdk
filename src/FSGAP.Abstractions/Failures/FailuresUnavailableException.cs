namespace FSGAP.Abstractions.Failures;

/// <summary>
/// The active failures cannot be read right now: the failure system is not reachable, did not answer in time, or
/// answered with something that cannot be interpreted. Temporary, unlike <see cref="NotSupportedException"/>.
/// </summary>
/// <remarks>
/// Thrown by <see cref="IFailureProvider.GetActiveFailuresAsync"/> instead of returning an empty list, because an
/// empty list always means "no active failure", never "cannot tell".
/// </remarks>
public sealed class FailuresUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FailuresUnavailableException()
        : base("The active failures cannot be read right now.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What made the read impossible.</param>
    public FailuresUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    /// <param name="message">What made the read impossible.</param>
    /// <param name="innerException">The underlying error.</param>
    public FailuresUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
