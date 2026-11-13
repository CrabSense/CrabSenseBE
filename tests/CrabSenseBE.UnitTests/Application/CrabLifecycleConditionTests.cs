using CrabSenseBE.Domain.Enums;

namespace CrabSenseBE.UnitTests.Application;

public sealed class CrabLifecycleConditionTests
{
    [Fact]
    public void Problem_IsNotQuarantine()
    {
        Assert.Equal(CrabStatus.Alive, CrabConditions.ToLifecycle(CrabCondition.Problem));
        Assert.Equal(CrabStatus.Alive, CrabConditions.ApplyCondition(CrabStatus.Alive, CrabCondition.Problem));
        Assert.Equal(CrabStatus.Molting, CrabConditions.ApplyCondition(CrabStatus.Molting, CrabCondition.Problem));
    }

    [Theory]
    [InlineData(CrabStatus.Dead)]
    [InlineData(CrabStatus.Harvested)]
    [InlineData(CrabStatus.Sold)]
    [InlineData(CrabStatus.Missing)]
    [InlineData(CrabStatus.Quarantined)]
    public void HealthObservation_CannotResurrectCrab(CrabStatus status)
    {
        Assert.Equal(status, CrabConditions.ApplyCondition(status, CrabCondition.Normal));
        Assert.Equal(status, CrabConditions.ApplyCondition(status, CrabCondition.Problem));
    }
}
