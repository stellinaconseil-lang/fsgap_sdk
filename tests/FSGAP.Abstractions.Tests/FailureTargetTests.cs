using FSGAP.Abstractions.Failures;

namespace FSGAP.Abstractions.Tests;

public class FailureTargetTests
{
    [Fact]
    public void Numbered_target_carries_index()
    {
        var target = FailureTarget.Engine(2);

        Assert.Equal(FailureTargetKind.Engine, target.Kind);
        Assert.Equal(2, target.Index);
        Assert.Null(target.Id);
        Assert.Equal("Engine 2", target.ToString());
        Assert.Equal(FailureTarget.Engine(2), target);
        Assert.NotEqual(FailureTarget.Engine(1), target);
    }

    [Fact]
    public void Named_target_carries_id()
    {
        var target = FailureTarget.HydraulicSystem("green");

        Assert.Equal(FailureTargetKind.HydraulicSystem, target.Kind);
        Assert.Equal("green", target.Id);
        Assert.Null(target.Index);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Index_is_one_based(int index)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FailureTarget.Engine(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => FailureTarget.InertialReference(index));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Id_is_required(string id)
    {
        Assert.Throws<ArgumentException>(() => FailureTarget.FuelPump(id));
        Assert.Throws<ArgumentException>(() => FailureTarget.ElectricalBus(id));
    }

    [Fact]
    public void Command_results_expose_success()
    {
        Assert.True(FailureCommandResult.Succeeded.IsSuccess);
        Assert.False(FailureCommandResult.NotSupported().IsSuccess);
        Assert.Equal(FailureCommandStatus.Rejected, FailureCommandResult.Rejected("no such pump").Status);
    }
}
