namespace FSGAP.Abstractions.Failures;

/// <summary>A request to trigger or clear a normalized failure on a target.</summary>
public sealed record FailureCommand
{
    /// <summary>Creates a command.</summary>
    /// <param name="key">Failure to act on.</param>
    /// <param name="target">Affected system instance; <see cref="FailureTarget.Aircraft"/> when <see langword="null"/>.</param>
    public FailureCommand(FailureKey key, FailureTarget? target = null)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Target = target ?? FailureTarget.Aircraft;
    }

    /// <summary>Failure to act on.</summary>
    public FailureKey Key { get; }

    /// <summary>Affected system instance.</summary>
    public FailureTarget Target { get; }
}
