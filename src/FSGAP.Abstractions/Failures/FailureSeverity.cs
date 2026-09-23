namespace FSGAP.Abstractions.Failures;

/// <summary>Operational severity of an active failure, as assessed by the provider.</summary>
public enum FailureSeverity
{
    /// <summary>The provider does not assess severity.</summary>
    Unspecified = 0,

    /// <summary>Minor: no significant operational impact.</summary>
    Minor,

    /// <summary>Major: significant operational impact, procedures required.</summary>
    Major,

    /// <summary>Critical: immediate crew action required (e.g. fire).</summary>
    Critical,
}
