using CrabSenseBE.Application.DTOs.Water;
using CrabSenseBE.Application.Services;
using FluentAssertions;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>
/// BỘT test này gãy nếu ai đó:
///   · đổi quy ước 1‰ ≈ 1 g muối/L (delta × thể tích),
///   · quên chia cho độ tinh khiết,
///   · mặc định độ tinh khiết của muối thô thành một con số nào đó,
///   · tính sai phép pha loãng V₂ = V₁·S₁/S₂ hoặc cách thay nước,
///   · để Ca/Mg lọt vào phép tính pha độ mặn.
/// </summary>
public class SalinityMixingCalculatorTests
{
    // ── Ví dụ chuẩn trong yêu cầu nghiệp vụ ────────────────────────────────
    // 1.000 L, 10‰ → 20‰, muối thô tinh khiết 90% ⇒ 10,00 kg lý thuyết, 11,11 kg thực tế.
    private static SalinityMixRequest SpecIncrease() => new(
        WaterVolumeL: 1000m,
        CurrentSalinityPpt: 10m,
        TargetSalinityPpt: 20m,
        SaltType: SalinitySaltTypes.Raw,
        SaltPurityPercent: 90m);

    [Fact]
    public void Increase_SpecExample_MatchesHandComputedSalt()
    {
        var r = SalinityMixingCalculator.Calculate(SpecIncrease());

        r.Direction.Should().Be(SalinityMixingCalculator.DirIncrease);
        r.DeltaPpt.Should().Be(10m);
        r.TheoreticalSaltKg.Should().Be(10.00m); // 10 × 1000 / 1000
        r.ActualSaltKg.Should().Be(11.11m);      // 10 / 0,90
        r.PurityAssumed.Should().BeFalse();
        r.SaltPurityPercent.Should().Be(90m);
        r.FreshwaterToAddL.Should().BeNull();
        r.WaterToReplaceL.Should().BeNull();
    }

    [Fact]
    public void Increase_PurityScalesSalt_NotSalinity()
    {
        var half = SalinityMixingCalculator.Calculate(SpecIncrease() with { SaltPurityPercent = 50m });
        var pure = SalinityMixingCalculator.Calculate(SpecIncrease() with { SaltPurityPercent = 100m });

        pure.ActualSaltKg.Should().Be(pure.TheoreticalSaltKg);
        half.ActualSaltKg.Should().Be(pure.ActualSaltKg * 2);
        // Độ mặn mục tiêu không đổi theo độ tinh khiết — chỉ lượng muối đổi.
        half.TargetSalinityPpt.Should().Be(pure.TargetSalinityPpt);
    }

    [Fact]
    public void Increase_UnknownPurity_AssumesPureAndWarns()
    {
        var r = SalinityMixingCalculator.Calculate(SpecIncrease() with { SaltPurityPercent = null });

        r.PurityAssumed.Should().BeTrue();
        r.SaltPurityPercent.Should().BeNull();
        r.ActualSaltKg.Should().Be(r.TheoreticalSaltKg);
        r.Warnings.Should().Contain(w => w.Contains("TỐI THIỂU"));
    }

    [Fact]
    public void RawSalt_NeverGetsDefaultPurity_SoFarmerIsForcedToMeasure()
    {
        // Không được lén mặc định muối thô = 90%: mỗi lô một khác.
        SalinitySaltTypes.All
            .Single(t => t.Key == SalinitySaltTypes.Raw)
            .TypicalPurityPercent.Should().BeNull();

        var r = SalinityMixingCalculator.Calculate(SpecIncrease() with { SaltPurityPercent = null });
        r.Warnings.Should().Contain(w => w.Contains("chưa biết độ tinh khiết", StringComparison.OrdinalIgnoreCase)
                                      || w.Contains("Chưa biết độ tinh khiết"));
    }

    [Fact]
    public void Increase_NaClAndRawSalt_WarnThatCaMgDoNotFollowSalinity()
    {
        foreach (var type in new[] { SalinitySaltTypes.Table, SalinitySaltTypes.Raw })
        {
            var r = SalinityMixingCalculator.Calculate(SpecIncrease() with { SaltType = type });
            r.Warnings.Should().Contain(w => w.Contains("Ca/Mg"));
        }
    }

    // ── Giảm độ mặn: V₂ = V₁·S₁/S₂ ─────────────────────────────────────────
    [Fact]
    public void Decrease_SpecExample_DoublesVolume()
    {
        // 1.000 L ở 20‰ → 10‰ đòi hỏi gấp đôi thể tích: thêm 1.000 L nước ngọt.
        var r = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m,
            CurrentSalinityPpt: 20m,
            TargetSalinityPpt: 10m));

        r.Direction.Should().Be(SalinityMixingCalculator.DirDecrease);
        r.DeltaPpt.Should().Be(-10m);
        r.FreshwaterToAddL.Should().Be(1000m);
        r.FinalVolumeL.Should().Be(2000m);
        // Cách thay nước giữ nguyên thể tích: rút 500 L rồi châm lại 500 L nước ngọt.
        r.WaterToReplaceL.Should().Be(500m);
        // Giảm độ mặn thì không có muối — null chứ không phải 0.
        r.ActualSaltKg.Should().BeNull();
        r.SaltKgPerBatch.Should().BeNull();
    }

    [Fact]
    public void Decrease_ReplaceMethod_HalvesVolumeForHalvedSalinity()
    {
        // 20‰ → 10‰: rút đúng nửa thể tích, thay bằng nước ngọt.
        var r = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 800m,
            CurrentSalinityPpt: 20m,
            TargetSalinityPpt: 10m));

        r.WaterToReplaceL.Should().Be(400m);
        r.FreshwaterToAddL.Should().Be(800m);
        r.FinalVolumeL.Should().Be(1600m);
    }

    [Fact]
    public void Decrease_ToZeroSalinity_IsRejectedInsteadOfDividingByZero()
    {
        var act = () => SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m,
            CurrentSalinityPpt: 20m,
            TargetSalinityPpt: 0m));

        act.Should().Throw<ArgumentException>().WithMessage("*must be > 0*");
    }

    [Fact]
    public void Decrease_NeedingMoreFreshwaterThanVolume_WarnsAboutTankCapacity()
    {
        // 30‰ → 5‰ cần gấp 6 lần thể tích.
        var r = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m,
            CurrentSalinityPpt: 30m,
            TargetSalinityPpt: 5m));

        r.FreshwaterToAddL.Should().Be(5000m);
        r.Warnings.Should().Contain(w => w.Contains("sức chứa"));
    }

    // ── Không đổi / thiếu dữ liệu ──────────────────────────────────────────
    [Fact]
    public void Hold_WhenSalinityAlreadyAtTarget()
    {
        var r = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m,
            CurrentSalinityPpt: 15m,
            TargetSalinityPpt: 15m));

        r.Direction.Should().Be(SalinityMixingCalculator.DirHold);
        r.DeltaPpt.Should().Be(0m);
        r.BatchCount.Should().Be(0);
        r.ActualSaltKg.Should().BeNull();
        r.FreshwaterToAddL.Should().BeNull();
    }

    [Fact]
    public void Unknown_NamesTheMissingField_InsteadOfSayingNoData()
    {
        var noTarget = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m, CurrentSalinityPpt: 12m));
        noTarget.Direction.Should().Be(SalinityMixingCalculator.DirUnknown);
        noTarget.Warnings.Should().Contain(w => w.Contains("chỉ còn thiếu mức mong muốn"));

        var noCurrent = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m, TargetSalinityPpt: 20m));
        noCurrent.Warnings.Should().Contain(w => w.Contains("chỉ còn thiếu độ mặn hiện tại"));
    }

    // ── Chia lần để không sốc cua ──────────────────────────────────────────
    [Fact]
    public void Increase_SplitsIntoBatches_EachStepWithinLimit()
    {
        var r = SalinityMixingCalculator.Calculate(SpecIncrease());

        // 10‰ / trần 3‰ ⇒ 4 lần, khớp ví dụ "chia 3–4 lần" trong yêu cầu.
        r.MaxChangePerBatchPpt.Should().Be(SalinityMixingCalculator.DefaultMaxChangePerBatchPpt);
        r.BatchCount.Should().Be(4);
        r.SaltKgPerBatch.Should().Be(2.78m); // 11,11 / 4
        r.BatchIntervalHours.Should().Be(SalinityMixingCalculator.BatchIntervalHours);
        // Mỗi lần cộng dồn không vượt mục tiêu.
        (r.SaltKgPerBatch!.Value * r.BatchCount).Should().BeGreaterThanOrEqualTo(r.ActualSaltKg!.Value);
    }

    [Fact]
    public void Decrease_SplitsIntoBatches_EachStepWithinLimit()
    {
        var r = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m,
            CurrentSalinityPpt: 20m,
            TargetSalinityPpt: 10m));

        r.BatchCount.Should().Be(4);
        r.FreshwaterLPerBatch.Should().Be(250m); // 1.000 / 4
        r.SaltKgPerBatch.Should().BeNull();
    }

    [Fact]
    public void BatchCount_RejectsNonPositiveStep()
    {
        var act = () => SalinityMixingCalculator.BatchCount(10m, 0m);
        act.Should().Throw<ArgumentException>();
        SalinityMixingCalculator.BatchCount(0m, 3m).Should().Be(0);
    }

    [Fact]
    public void Increase_CustomStep_IsRespected()
    {
        var r = SalinityMixingCalculator.Calculate(SpecIncrease() with { MaxChangePerBatchPpt = 5m });
        r.MaxChangePerBatchPpt.Should().Be(5m);
        r.BatchCount.Should().Be(2);
    }

    // ── Hướng dẫn pha: phải có, phải đúng thứ tự, phải nói đo lại ──────────
    [Fact]
    public void Increase_Instructions_CoverDissolveSeparatelyAndNeverPourDrySalt()
    {
        var r = SalinityMixingCalculator.Calculate(SpecIncrease());
        var text = string.Join("\n", r.Instructions);

        r.Instructions.Should().HaveCountGreaterThanOrEqualTo(7);
        text.Should().Contain("không đổ muối khô trực tiếp");
        text.Should().Contain("ĐO LẠI độ mặn");
        text.Should().Contain("TIN MÁY ĐO");
    }

    [Fact]
    public void Decrease_Instructions_RequireDechlorinatedFreshwater()
    {
        var r = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m,
            CurrentSalinityPpt: 20m,
            TargetSalinityPpt: 10m));
        var text = string.Join("\n", r.Instructions);

        text.Should().Contain("KHỬ CLO");
        text.Should().Contain("Cách B");
        text.Should().Contain("nhiệt độ");
    }

    [Fact]
    public void EveryDirection_ReturnsNextStepsLinkingToMineralDosing()
    {
        var results = new[]
        {
            SalinityMixingCalculator.Calculate(SpecIncrease()),
            SalinityMixingCalculator.Calculate(new SalinityMixRequest(
                WaterVolumeL: 1000m, CurrentSalinityPpt: 20m, TargetSalinityPpt: 10m)),
            SalinityMixingCalculator.Calculate(new SalinityMixRequest(
                WaterVolumeL: 1000m, CurrentSalinityPpt: 10m, TargetSalinityPpt: 10m)),
        };

        foreach (var r in results)
        {
            r.NextSteps.Should().HaveCount(3);
            var text = string.Join("\n", r.NextSteps);
            text.Should().Contain("Ca/Mg");
            text.Should().Contain("Kiểm tra cuối");
        }
    }

    // ── Cảnh báo vùng nghiên cứu ───────────────────────────────────────────
    [Fact]
    public void Increase_TargetAboveResearchBand_Warns()
    {
        var r = SalinityMixingCalculator.Calculate(SpecIncrease() with { TargetSalinityPpt = 25m });
        r.Warnings.Should().Contain(w => w.Contains("vượt vùng đã nghiên cứu"));
    }

    [Fact]
    public void BigChange_AlwaysComesWithSplitWarning()
    {
        var r = SalinityMixingCalculator.Calculate(SpecIncrease());
        r.Warnings.Should().Contain(w => w.Contains("thay đổi lớn"));
    }

    // ── Hợp đồng dữ liệu với UI ────────────────────────────────────────────
    [Fact]
    public void SaltTypeCatalogue_HasStableKeysAndNoInventedPurityForRaw()
    {
        var types = SalinityMixingCalculator.SaltTypes();

        types.Select(t => t.Key).Should().Equal(
            SalinitySaltTypes.Raw, SalinitySaltTypes.Table, SalinitySaltTypes.SeaSaltMix);
        types.Should().OnlyContain(t => !string.IsNullOrWhiteSpace(t.Label)
                                     && !string.IsNullOrWhiteSpace(t.Note));
        types.Should().OnlyContain(t => t.TypicalPurityPercent == null
                                     || (t.TypicalPurityPercent > 0m && t.TypicalPurityPercent <= 100m));
    }

    [Fact]
    public void Resolve_AcceptsEmptyAndRejectsUnknownSaltType()
    {
        SalinitySaltTypes.Resolve(null).Key.Should().Be(SalinitySaltTypes.Raw);
        SalinitySaltTypes.Resolve("  SEA-SALT-MIX ").Key.Should().Be(SalinitySaltTypes.SeaSaltMix);

        var act = () => SalinitySaltTypes.Resolve("cat");
        act.Should().Throw<ArgumentException>().WithMessage("*raw, table, sea_salt_mix*");
    }

    [Fact]
    public void Validate_RejectsBadInput()
    {
        var zeroVolume = () => SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 0m, CurrentSalinityPpt: 10m, TargetSalinityPpt: 20m));
        zeroVolume.Should().Throw<ArgumentException>().WithMessage("*WaterVolumeL*");

        var negative = () => SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 1000m, CurrentSalinityPpt: -1m, TargetSalinityPpt: 20m));
        negative.Should().Throw<ArgumentException>().WithMessage("*CurrentSalinityPpt*");

        var purity = () => SalinityMixingCalculator.Calculate(SpecIncrease() with { SaltPurityPercent = 0m });
        purity.Should().Throw<ArgumentException>().WithMessage("*SaltPurityPercent*");

        var purityOver = () => SalinityMixingCalculator.Calculate(SpecIncrease() with { SaltPurityPercent = 101m });
        purityOver.Should().Throw<ArgumentException>().WithMessage("*SaltPurityPercent*");
    }

    [Fact]
    public void RaiseFromZero_WorksWithoutSpecialCase()
    {
        var r = SalinityMixingCalculator.Calculate(new SalinityMixRequest(
            WaterVolumeL: 500m,
            CurrentSalinityPpt: 0m,
            TargetSalinityPpt: 10m,
            SaltPurityPercent: 100m));

        r.Direction.Should().Be(SalinityMixingCalculator.DirIncrease);
        r.TheoreticalSaltKg.Should().Be(5.00m); // 10 × 500 / 1000
        r.WaterToReplaceL.Should().BeNull();
    }
}
