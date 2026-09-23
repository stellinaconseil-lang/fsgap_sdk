namespace FSGAP.Abstractions.Failures;

/// <summary>Operations a provider supports for one failure of its catalog.</summary>
[Flags]
public enum FailureOperations
{
    /// <summary>The failure is known (for example it can be reported as active) but cannot be commanded.</summary>
    None = 0,

    /// <summary>The failure can be triggered.</summary>
    Trigger = 1,

    /// <summary>The failure can be cleared.</summary>
    Clear = 2,
}
