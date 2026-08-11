using CrabSenseBE.Application.DTOs.Condition;
using CrabSenseBE.Application.Services;
using FluentAssertions;

namespace CrabSenseBE.UnitTests.Application;

public class CrabConditionServiceTests
{
    [Fact]
    public void Evaluate_CwProfile_MatchesPythonDemo_H8()
    {
        // H8 cái: W=271, CW=8.5 → Kn≈1.050 full_meat (demo_your_data.py)
        var dto = KnConditionEngine.Evaluate(new EvaluateCrabConditionRequest(
            WeightG: 271m,
            CarapaceWidthCm: 8.5m,
            CrabType: "cai"));

        dto.Status.Should().Be("full_meat");
        dto.AlertHarvest.Should().BeFalse();
        dto.Kn.Should().BeApproximately(1.0504m, 0.01m);
        dto.ProfileUsed.Should().Be("ras_hcm_raw");
        dto.MoltingStatusHint.Should().Be("hardShell");
    }

    [Fact]
    public void Evaluate_CwProfile_H4_PreMoltAlert()
    {
        // H4 đực: W=243, CW=7.5 → Kn≈1.319 pre_molt
        var dto = KnConditionEngine.Evaluate(new EvaluateCrabConditionRequest(
            243m, CarapaceWidthCm: 7.5m, CrabType: "duc"));

        dto.Status.Should().Be("pre_molt");
        dto.AlertHarvest.Should().BeTrue();
        dto.Kn.Should().BeGreaterThan(1.20m);
        dto.MoltingStatusHint.Should().Be("preMolt");
    }

    [Fact]
    public void Evaluate_ClFormula_Works()
    {
        var dto = KnConditionEngine.Evaluate(new EvaluateCrabConditionRequest(
            WeightG: 248m,
            CarapaceLengthCm: 12.0m,
            Dimension: "cl",
            CrabType: "yem_vuong"));

        dto.DimensionUsed.Should().Be("cl");
        dto.ProfileUsed.Should().Be("kn_method_cl");
        dto.WeightEstimatedG.Should().BeGreaterThan(0);
        dto.Kn.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Evaluate_MissingDimension_Throws()
    {
        var act = () => KnConditionEngine.Evaluate(new EvaluateCrabConditionRequest(200m));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DailyCheck_SuggestsFeedAt5PercentBw()
    {
        var dto = KnConditionEngine.DailyCheck(new DailyCheckRequest(
            CrabType: "duc",
            WeightG: 200m,
            CarapaceWidthCm: 8.0m,
            WeightInitialG: 180m,
            DaysInCulture: 10));

        dto.FeedTomorrowG.Should().Be(10.0m); // 5% of 200
        dto.FeedTime.Should().Be("22:00");
        dto.Kn.Should().NotBeNull();
    }
}
