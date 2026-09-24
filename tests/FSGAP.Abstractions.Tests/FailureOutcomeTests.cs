using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Tests;

/// <summary>BLOCK 7 failure contract additions: retry-relevant outcomes and "cannot tell right now".</summary>
public class FailureOutcomeTests
{
    [Fact]
    public void Unavailable_and_unconfirmed_are_distinct_non_success_outcomes()
    {
        var unavailable = FailureCommandResult.Unavailable("not running");
        var unconfirmed = FailureCommandResult.Unconfirmed("no answer");

        Assert.Equal(FailureCommandStatus.Unavailable, unavailable.Status);
        Assert.Equal(FailureCommandStatus.Unconfirmed, unconfirmed.Status);
        Assert.False(unavailable.IsSuccess);
        Assert.False(unconfirmed.IsSuccess);
        Assert.Equal("not running", unavailable.Message);
    }

    [Fact]
    public void Existing_status_values_are_unchanged()
    {
        // Appended, never reordered: persisted or serialized values keep their meaning.
        Assert.Equal(0, (int)FailureCommandStatus.Succeeded);
        Assert.Equal(1, (int)FailureCommandStatus.NotSupported);
        Assert.Equal(2, (int)FailureCommandStatus.Rejected);
        Assert.Equal(3, (int)FailureCommandStatus.Failed);
        Assert.Equal(4, (int)FailureCommandStatus.Unavailable);
        Assert.Equal(5, (int)FailureCommandStatus.Unconfirmed);
    }

    [Fact]
    public void Failures_unavailable_is_an_exception_distinct_from_not_supported()
    {
        var inner = new TimeoutException();
        var exception = new FailuresUnavailableException("cannot tell", inner);

        Assert.IsNotType<NotSupportedException>(exception);
        Assert.Same(inner, exception.InnerException);
        Assert.False(string.IsNullOrWhiteSpace(new FailuresUnavailableException().Message));
    }
}
