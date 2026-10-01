namespace FSGAP.Abstractions.Degradations;

/// <summary>What a provider can do with one degradation.</summary>
[Flags]
public enum DegradationOperations
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary><see cref="IDegradationProvider.ApplyAsync"/> is supported.</summary>
    Apply = 1,

    /// <summary><see cref="IDegradationProvider.RestoreAsync"/> is supported.</summary>
    Restore = 2,

    /// <summary><see cref="IDegradationProvider.GetStateAsync"/> is supported.</summary>
    ReadState = 4,
}
