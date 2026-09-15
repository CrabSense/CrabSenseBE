using CrabSenseBE.Application.DTOs.Water;
using CrabSenseBE.Application.Services;
using FluentAssertions;

namespace CrabSenseBE.UnitTests.Application;

/// <summary>
/// Bộ test này gãy nếu ai đó:
///   · để Mg bị suy từ Ca (điều cấm về đo lường),
///   · đổi hệ số sản phẩm 261,7 / 117,8,
///   · đổi band mục tiêu theo độ mặn,
///   · hoặc để độ mặn lọt vào phép tính gram.
/// </summary>
public class MineralDosingCalculatorTests
{
    // ── Ví dụ chuẩn trong yêu cầu nghiệp vụ ──────────────────────────────────
    // 1000 L, 10‰, Ca 200→300, Mg 700→900 ⇒ CaCl2 ≈ 382 g, MgCl2 ≈ 1698 g
    private static MineralDoseRequest SpecExample() => new(
        WaterVolumeL: 1000m,
        SalinityCurrentPpt: 10m,
        SalinityTargetPpt: 10m,
        CalciumCurrentMgL: 200m,
        CalciumTargetMgL: 300m,
        MagnesiumCurrentMgL: 700m,
        MagnesiumTargetMgL: 900m);

    [Fact]
    public void Calculate_SpecExample_MatchesHandComputedDoses()
    {
        var r = MineralDosingCalculator.Calculate(SpecExample());

        r.CalciumDeficitMgL.Should().Be(100m);
        r.MagnesiumDeficitMgL.Should().Be(200m);
        r.CaCl2DoseGrams.Should().Be(382.1m);   // 100 × 1000 / 261,7
        r.MgCl2DoseGrams.Should().Be(1697.8m);  // 200 × 1000 / 117,8
        r.SalinityStatus.Should().Be(MineralDosingCalculator.SalinityOk);
        r.CaCl2Product.Should().Be("CaCl2·2H2O 96%");
        r.MgCl2Product.Should().Be("MgCl2·6H2O 98,5%");
        r.CalciumCurrentSource.Should().Be(MineralDosingCalculator.SourceMeasured);
        r.MagnesiumCurrentSource.Should().Be(MineralDosingCalculator.SourceMeasured);
    }

    // ── Không được suy Mg từ Ca ──────────────────────────────────────────────
    [Fact]
    public void Calculate_MgNotTested_LeavesMgDoseNullAndAsksForTest()
    {
        // Nước 1000 L, 10‰, Ca 200 (đo), Mg CHƯA ĐO.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: null));

        // Không được tự phán Mg = 200 hay Mg = Ca × 3.
        r.MagnesiumCurrentMgL.Should().BeNull();
        r.MagnesiumCurrentSource.Should().Be(MineralDosingCalculator.SourceMissing);
        r.MgCl2DoseGrams.Should().BeNull();
        r.MagnesiumDeficitMgL.Should().BeNull();
        r.Warnings.Should().Contain(w => w.Contains("Chưa đo Mg"));

        // Mục tiêu Mg vẫn lấy được từ độ mặn, nhưng liều thì không.
        r.MagnesiumTargetMgL.Should().Be(900m);

        // Ca vẫn tính bình thường.
        r.CaCl2DoseGrams.Should().Be(382.1m);
    }

    // ── Band mục tiêu theo độ mặn ────────────────────────────────────────────
    [Theory]
    [InlineData(1.5, 150, 450)]  // nghiên cứu 1,5‰ (công bố 15/09/2026)
    [InlineData(2.9, 150, 450)]
    [InlineData(3, 300, 900)]    // biên dưới band 3–12‰
    [InlineData(10, 300, 900)]   // ví dụ trong yêu cầu
    [InlineData(12, 300, 900)]   // biên trên band 3–12‰
    [InlineData(12.1, 400, 1200)]
    [InlineData(30, 400, 1200)]
    public void RecommendTargets_UsesSalinityBands(double salinity, int ca, int mg)
    {
        var (rCa, rMg) = MineralDosingCalculator.RecommendTargets((decimal)salinity);

        rCa.Should().Be(ca);
        rMg.Should().Be(mg);
    }

    [Fact]
    public void RecommendTargets_WithoutSalinity_ReturnsNothing()
    {
        var (ca, mg) = MineralDosingCalculator.RecommendTargets(null);
        ca.Should().BeNull();
        mg.Should().BeNull();
    }

    [Fact]
    public void Recommend_AlwaysReportsRatioOneToThree()
    {
        foreach (var s in new decimal[] { 1.5m, 5m, 10m, 12m, 20m })
        {
            var rec = MineralDosingCalculator.Recommend(s);
            rec.RecommendedCaMgRatio.Should().Be("1:3", because: $"{s}‰ phải giữ tỷ lệ 1:3");
            rec.Note.Should().Contain("ĐỀ XUẤT");
        }
    }

    [Fact]
    public void Recommend_AboveResearchRange_WarnsAboutExtrapolation()
    {
        // Band 3–12‰ và >12‰ không còn rỗng cảnh báo: tổng 1200/1600 đều vượt mức đã kiểm chứng.
        MineralDosingCalculator.Recommend(20m).Warnings
            .Should().Contain(w => w.Contains("ngoại suy"));
    }

    // ── AUTO vs MANUAL ───────────────────────────────────────────────────────
    [Fact]
    public void Calculate_AutoMode_FillsMissingTargetFromSalinity()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 700m));

        r.CalciumTargetMgL.Should().Be(300m);
        r.MagnesiumTargetMgL.Should().Be(900m);
        r.UsedRecommendedTargets.Should().BeTrue();
        r.TargetMode.Should().Be(MineralDosingCalculator.ModeAuto);
    }

    [Fact]
    public void Calculate_ManualMode_DoesNotOverrideUserTargets()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            CalciumTargetMgL: 320m,
            MagnesiumCurrentMgL: 700m,
            MagnesiumTargetMgL: 950m,
            TargetMode: "manual"));

        // Tôn trọng mục tiêu người dùng, KHÔNG kéo về 300/900.
        r.CalciumTargetMgL.Should().Be(320m);
        r.MagnesiumTargetMgL.Should().Be(950m);
        r.UsedRecommendedTargets.Should().BeFalse();
        r.CaCl2DoseGrams.Should().Be(458.5m);  // 120 × 1000 / 261,7
        r.MgCl2DoseGrams.Should().Be(2122.2m); // 250 × 1000 / 117,8

        // Đề xuất vẫn được trả về để UI so sánh.
        r.RecommendedCalciumMgL.Should().Be(300m);
        r.RecommendedMagnesiumMgL.Should().Be(900m);
    }

    [Fact]
    public void Calculate_ManualMode_MissingTarget_WarnsInsteadOfGuessing()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 700m,
            TargetMode: "manual"));

        r.CalciumTargetMgL.Should().BeNull();
        r.MagnesiumTargetMgL.Should().BeNull();
        r.CaCl2DoseGrams.Should().BeNull();
        r.MgCl2DoseGrams.Should().BeNull();
        r.Warnings.Should().Contain(w => w.Contains("MANUAL"));
    }

    // ── Chênh lệch ≤ 0 ⇒ không châm ──────────────────────────────────────────
    [Fact]
    public void Calculate_AlreadyAtOrAboveTarget_ZeroDose()
    {
        var atTarget = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 300m,
            MagnesiumCurrentMgL: 900m));

        atTarget.CaCl2DoseGrams.Should().Be(0m);
        atTarget.MgCl2DoseGrams.Should().Be(0m);
        atTarget.CalciumDeficitMgL.Should().Be(0m);

        var above = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 350m,
            MagnesiumCurrentMgL: 950m));

        above.CaCl2DoseGrams.Should().Be(0m);
        above.MgCl2DoseGrams.Should().Be(0m);
        above.CalciumDeficitMgL.Should().Be(-50m); // giữ dấu để UI giải thích
        above.Warnings.Should().Contain(w => w.Contains("VƯỢT mục tiêu"));
    }

    // ── Nguồn dữ liệu: đo vs ước lượng ───────────────────────────────────────
    [Fact]
    public void Calculate_EstimatedValues_AreFlagged()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 700m,
            CalciumMeasured: false,
            MagnesiumMeasured: false));

        r.CalciumCurrentSource.Should().Be(MineralDosingCalculator.SourceEstimated);
        r.MagnesiumCurrentSource.Should().Be(MineralDosingCalculator.SourceEstimated);
        r.Warnings.Should().Contain(w => w.Contains("ƯỚC LƯỢNG"));
        r.Warnings.Should().Contain(w => w.Contains("Mg") && w.Contains("ƯỚC LƯỢNG"));
    }

    // ── Độ mặn: chỉ so sánh ──────────────────────────────────────────────────
    [Theory]
    [InlineData(8, 10, "SALINITY_LOW")]
    [InlineData(12, 10, "SALINITY_HIGH")]
    [InlineData(10, 10, "SALINITY_OK")]
    public void CompareSalinity_ReportsDirection(double current, double target, string expected)
    {
        var (status, label) = MineralDosingCalculator.CompareSalinity(
            (decimal)current, (decimal)target);

        status.Should().Be(expected);
        label.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void CompareSalinity_MissingHalf_ReportsWhichHalfMissing()
    {
        // Có số hiện tại, thiếu mục tiêu ⇒ KHÔNG phải "unknown": phải báo số đã nhập.
        // Đây là bug đã thấy trên UI (nhập 20‰ mà thẻ báo "Chưa đủ dữ liệu độ mặn").
        MineralDosingCalculator.CompareSalinity(10m, null).Status
            .Should().Be(MineralDosingCalculator.SalinityNoTarget);

        // Thiếu số hiện tại ⇒ vẫn là unknown, nhưng nhãn phải chỉ rõ thiếu vế nào.
        var (status, label) = MineralDosingCalculator.CompareSalinity(null, 10m);
        status.Should().Be(MineralDosingCalculator.SalinityUnknown);
        label.Should().Contain("hiện tại");
    }

    [Fact]
    public void Calculate_SalinityDoesNotAffectGramAmount()
    {
        // Cùng Ca/Mg và thể tích, khác độ mặn ⇒ liều gram phải GIỐNG NHAU.
        // (Độ mặn chỉ được phép đổi mục tiêu ở chế độ auto, nên test dùng manual.)
        decimal Dose(decimal salinity) => MineralDosingCalculator.Calculate(
            new MineralDoseRequest(
                WaterVolumeL: 500m,
                SalinityCurrentPpt: salinity,
                CalciumCurrentMgL: 200m,
                CalciumTargetMgL: 300m,
                MagnesiumCurrentMgL: 700m,
                MagnesiumTargetMgL: 900m,
                TargetMode: "manual")).CaCl2DoseGrams!.Value;

        Dose(3m).Should().Be(Dose(25m));
    }

    // ── Kiểm tra đầu vào (trust boundary) ────────────────────────────────────
    [Fact]
    public void Calculate_NonPositiveVolume_Throws()
    {
        var act = () => MineralDosingCalculator.Calculate(
            new MineralDoseRequest(WaterVolumeL: 0m, SalinityCurrentPpt: 10m));

        act.Should().Throw<ArgumentException>().WithMessage("*WaterVolumeL*");
    }

    [Theory]
    [InlineData(-1)]
    public void Calculate_NegativeInputs_Throw(double bad)
    {
        var act = () => MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m, CalciumCurrentMgL: (decimal)bad));
        act.Should().Throw<ArgumentException>();

        var act2 = () => MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m, SalinityCurrentPpt: (decimal)bad));
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Calculate_UnknownTargetMode_ThrowsInsteadOfGuessing()
    {
        var act = () => MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m, TargetMode: "hi-whatever"));

        act.Should().Throw<ArgumentException>().WithMessage("*TargetMode*");
    }

    [Fact]
    public void NormalizeMode_AcceptsCaseInsensitiveAndEmpty()
    {
        MineralDosingCalculator.NormalizeMode("AUTO").Should().Be("auto");
        MineralDosingCalculator.NormalizeMode("Manual").Should().Be("manual");
        MineralDosingCalculator.NormalizeMode(null).Should().Be("auto");
        MineralDosingCalculator.NormalizeMode("  ").Should().Be("auto");
    }

    // ── Hệ số sản phẩm đúng như tài liệu ─────────────────────────────────────
    [Fact]
    public void ProductDivisors_MatchDocumentedComposition()
    {
        // CaCl2·2H2O 96%: 1000 × (40,078/147,01 × 0,96) ≈ 261,7
        var caExpected = 1000.0 * (40.078 / 147.01) * 0.96;
        MineralDosingCalculator.CaCl2Divisor.Should().BeApproximately(caExpected, 0.5);

        // MgCl2·6H2O 98,5%: 1000 × (24,305/203,30 × 0,985) ≈ 117,8
        var mgExpected = 1000.0 * (24.305 / 203.30) * 0.985;
        MineralDosingCalculator.MgCl2Divisor.Should().BeApproximately(mgExpected, 0.5);
    }

    [Fact]
    public void Calculate_OneGramProductRaisesOneMgPerLitreInOneThousandLitres()
    {
        // Truy hồi: 1 mg/L trong 1000 L cần 3,821 g CaCl2·2H2O 96%.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            CalciumTargetMgL: 1m,
            MagnesiumTargetMgL: 1m,
            CalciumCurrentMgL: 0m,
            MagnesiumCurrentMgL: 0m,
            TargetMode: "manual"));

        r.CaCl2DoseGrams.Should().Be(3.8m);
        r.MgCl2DoseGrams.Should().Be(8.5m);
    }

    // ── Tổng Ca+Mg: bằng chứng từ Qin et al. 2026 (600 mg/L tối ưu ở 1,5‰) ───
    [Theory]
    [InlineData(1.5, 600)]
    [InlineData(10, 1200)]
    [InlineData(20, 1600)]
    public void Recommend_ExposesCalciumMagnesiumTotal(double salinity, int expectedTotal)
    {
        MineralDosingCalculator.Recommend((decimal)salinity).CalciumMagnesiumTotalMgL
            .Should().Be(expectedTotal);
    }

    [Fact]
    public void Recommend_ValidatedBand_HasNoHighTotalWarning()
    {
        // 1,5‰ → 150/450 = tổng 600, đúng mức tối ưu đã kiểm chứng ⇒ không cảnh báo tổng.
        var rec = MineralDosingCalculator.Recommend(1.5m);

        rec.CalciumMagnesiumTotalMgL.Should().Be(600m);
        rec.Warnings.Should().NotContain(w => w.Contains("vượt xa vùng đã kiểm chứng"));
    }

    [Theory]
    [InlineData(3)]    // biên dưới band 3–12‰
    [InlineData(10)]   // ví dụ chính trong yêu cầu
    [InlineData(12)]   // biên trên band 3–12‰
    [InlineData(25)]   // ngoại suy
    public void Recommend_TotalAboveValidated_WarnsAboutMoltingAndRasAccumulation(double salinity)
    {
        var rec = MineralDosingCalculator.Recommend((decimal)salinity);

        var total = rec.Warnings.Where(w => w.Contains("vượt xa vùng đã kiểm chứng")).ToList();
        total.Should().HaveCount(1);

        // Cảnh báo phải nêu đủ 3 ý: mức đã kiểm chứng, rủi ro khó lột, và cách châm an toàn cho RAS.
        total[0].Should().Contain("600 mg/L");
        total[0].Should().Contain("khó lột");
        total[0].Should().Contain("RAS tích tụ");
        total[0].Should().Contain("chia thành nhiều lần");
    }

    [Fact]
    public void Calculate_AutoMode_DoesNotDuplicateHighTotalWarning()
    {
        // Recommend() được gọi bên trong Calculate() và warnings được gộp lại,
        // nên cảnh báo tổng phải xuất hiện ĐÚNG MỘT LẦN sau khi Distinct().
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 700m));

        r.Warnings.Where(w => w.Contains("vượt xa vùng đã kiểm chứng")).Should().HaveCount(1);
        r.Warnings.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Calculate_ExposesTotalTargetAndCurrent()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 700m));

        r.CalciumMagnesiumTotalTargetMgL.Should().Be(1200m);   // 300 + 900
        r.CalciumMagnesiumTotalCurrentMgL.Should().Be(900m);   // 200 + 700
    }

    [Fact]
    public void Calculate_TotalCurrent_IsNullWhenOneValueMissing()
    {
        // Không đo Mg ⇒ không được cộng Ca vào để "đoán" tổng.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: null));

        r.CalciumMagnesiumTotalCurrentMgL.Should().BeNull();
        r.CalciumMagnesiumTotalTargetMgL.Should().Be(1200m); // mục tiêu vẫn biết từ độ mặn
    }

    [Fact]
    public void Calculate_ManualLowTotal_NoHighTotalWarning()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 50m,
            CalciumTargetMgL: 150m,
            MagnesiumCurrentMgL: 80m,
            MagnesiumTargetMgL: 450m,
            TargetMode: "manual"));

        // Mục tiêu người dùng đặt tổng 600 — đúng mức đã kiểm chứng ⇒ không cảnh báo tổng.
        r.CalciumMagnesiumTotalTargetMgL.Should().Be(600m);
        r.Warnings.Should().NotContain(w => w.Contains("vượt xa vùng đã kiểm chứng"));
    }

    // ── Chia liều: châm nhiều lần để không sốc cua ───────────────────────────
    // Ngưỡng 50 mg/L mỗi lần là AN TOÀN VẬN HÀNH, không phải số liệu nghiên cứu
    // (chưa bài nào thử tốc độ tăng Ca/Mg trên Scylla paramamosain).

    [Fact]
    public void SplitDoses_Deficit100_At50PerDose_GivesTwoDoses()
    {
        // 100 mg/L thiếu, trần 50 ⇒ 2 lần. Ca 382,1 g ⇒ 191,05 g mỗi lần.
        var (count, caPerDose, mgPerDose) = MineralDosingCalculator.SplitDoses(
            caDeficit: 100m, mgDeficit: 0m, caDose: 382.1m, mgDose: 0m,
            maxIncreasePerDoseMgL: 50m);

        count.Should().Be(2);
        caPerDose.Should().BeApproximately(191.05m, 0.1m); // 382,1 / 2
        mgPerDose.Should().Be(0m);                         // Mg đã đủ
    }

    [Fact]
    public void SplitDoses_CountDrivenByBiggestDeficit()
    {
        // Ca thiếu 20 nhưng Mg thiếu 200 ⇒ 4 lần (theo Mg). Ca mỗi lần chỉ 5 mg/L — chậm
        // hơn nhưng không sai, và đó là lựa chọn an toàn.
        var (count, _, _) = MineralDosingCalculator.SplitDoses(
            caDeficit: 20m, mgDeficit: 200m, caDose: 10m, mgDose: 100m,
            maxIncreasePerDoseMgL: 50m);

        count.Should().Be(4);
    }

    [Fact]
    public void SplitDoses_ExactMultiple_DoesNotAddExtraDose()
    {
        // 100 / 50 = đúng 2, không được thành 3.
        var (count, _, _) = MineralDosingCalculator.SplitDoses(
            caDeficit: 100m, mgDeficit: null, caDose: 100m, mgDose: null,
            maxIncreasePerDoseMgL: 50m);

        count.Should().Be(2);
    }

    [Fact]
    public void SplitDoses_NothingToDose_ReturnsZero()
    {
        var (count, caPerDose, mgPerDose) = MineralDosingCalculator.SplitDoses(
            caDeficit: 0m, mgDeficit: -30m, caDose: 0m, mgDose: 0m,
            maxIncreasePerDoseMgL: 50m);

        count.Should().Be(0);
        caPerDose.Should().BeNull();
        mgPerDose.Should().BeNull();
    }

    [Fact]
    public void SplitDoses_UnknownDose_CountZeroNotGuess()
    {
        // Chưa đo Ca ⇒ chưa tính được liều ⇒ không bịa ra số lần châm.
        var (count, caPerDose, _) = MineralDosingCalculator.SplitDoses(
            caDeficit: null, mgDeficit: null, caDose: null, mgDose: null,
            maxIncreasePerDoseMgL: 50m);

        count.Should().Be(0);
        caPerDose.Should().BeNull();
    }

    [Fact]
    public void SplitDoses_NonPositiveCap_Throws()
    {
        var act = () => MineralDosingCalculator.SplitDoses(
            caDeficit: 100m, mgDeficit: null, caDose: 100m, mgDose: null,
            maxIncreasePerDoseMgL: 0m);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Calculate_ExposesSplitPlan()
    {
        // Kịch bản của người dùng: 20‰, thiếu Ca 100 mg/L → phải chia nhỏ, không châm 1 lần.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 20m,
            CalciumCurrentMgL: 300m,
            MagnesiumCurrentMgL: 1200m));

        r.MaxIncreasePerDoseMgL.Should().Be(50m);
        r.DoseCount.Should().Be(2);
        r.CaCl2GramsPerDose.Should().BeApproximately(r.CaCl2DoseGrams!.Value / 2, 0.1m);
        r.DoseIntervalHours.Should().Be(24);
        r.DosingInstructions.Should().NotBeEmpty();
        // Mỗi lần châm không được vượt trần.
        r.DosingInstructions.Should().Contain(s => s.Contains("50"));
    }

    [Fact]
    public void Calculate_AlreadyAtTarget_SaysNothingToDose()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 300m,
            MagnesiumCurrentMgL: 900m));

        r.DoseCount.Should().Be(0);
        r.DosingInstructions.Should().ContainSingle()
            .Which.Should().Contain("Không cần châm");
    }

    [Fact]
    public void Calculate_MissingMagnesium_CanStillSplitCalcium()
    {
        // Thiếu Mg ⇒ liều MgCl2 null, nhưng vẫn chia được liều Ca.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m));

        r.DoseCount.Should().Be(2); // Ca thiếu 100 ⇒ 2 lần
        r.CaCl2GramsPerDose.Should().NotBeNull();
        r.MgCl2GramsPerDose.Should().BeNull();
    }

    [Fact]
    public void DosingInstructions_CautionAgainstMixingBothSalts()
    {
        var steps = MineralDosingCalculator.DosingInstructions(
            doseCount: 2, maxIncreasePerDoseMgL: 50m,
            doseCa: true, doseMg: true, salinityPpt: 10m);

        steps.Should().Contain(s => s.Contains("không trộn CaCl"));
        steps.Should().Contain(s => s.Contains("nguội")); // CaCl2 tan toả nhiệt
        steps.Should().Contain(s => s.Contains("buổi sáng"));
        steps.Should().Contain(s => s.Contains("pH"));
    }

    [Fact]
    public void DosingInstructions_AboveResearchSalinity_WarnsNotToChaseNumber()
    {
        var steps = MineralDosingCalculator.DosingInstructions(
            doseCount: 2, maxIncreasePerDoseMgL: 50m,
            doseCa: true, doseMg: true, salinityPpt: 20m);

        steps.Should().Contain(s => s.Contains("NGOÀI vùng đã nghiên cứu"));
    }

    [Fact]
    public void DosingInstructions_WithinResearchSalinity_NoExtrapolationNote()
    {
        var steps = MineralDosingCalculator.DosingInstructions(
            doseCount: 2, maxIncreasePerDoseMgL: 50m,
            doseCa: true, doseMg: true, salinityPpt: 10m);

        steps.Should().NotContain(s => s.Contains("NGOÀI vùng đã nghiên cứu"));
    }

    [Fact]
    public void Calculate_ManyDoses_WarnsProgramTooLong()
    {
        // Thiếu 500 mg/L với trần 50 ⇒ 10 lần > 7 ⇒ phải cảnh báo kéo dài.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 0m,
            CalciumTargetMgL: 500m,
            MagnesiumCurrentMgL: 0m,
            MagnesiumTargetMgL: 500m,
            TargetMode: "manual"));

        r.DoseCount.Should().Be(10);
        r.Warnings.Should().Contain(w => w.Contains("nhiều hơn 7 lần"));
    }

    [Fact]
    public void Calculate_CustomCapZero_FallsBackToDefault()
    {
        // Trần ≤ 0 là vô nghĩa — dùng mặc định, KHÔNG chia cho 0.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 10m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 800m,
            MaxIncreasePerDoseMgL: 0m));

        r.MaxIncreasePerDoseMgL.Should().Be(MineralDosingCalculator.DefaultMaxIncreasePerDoseMgL);
        r.DoseCount.Should().BeGreaterThan(0);
    }

    // ── Trạng thái độ mặn: ba tình trạng, KHÔNG gộp làm một ──────────────────
    // Bug thật đã gặp trên UI: nhập 20‰ nhưng không nhập mức mong muốn ⇒ thẻ
    // "ĐỘ MẶN" báo "Chưa đủ dữ liệu độ mặn", làm tưởng ô nhập bị lỗi.

    [Fact]
    public void CompareSalinity_CurrentWithoutTarget_ReportsCurrentNotMissing()
    {
        var (status, label) = MineralDosingCalculator.CompareSalinity(current: 20m, target: null);

        status.Should().Be(MineralDosingCalculator.SalinityNoTarget);
        label.Should().Contain("20");                    // phải nói ra số đã nhập
        label.Should().NotContain("Chưa đủ dữ liệu");    // câu gây hiểu nhầm cũ
    }

    [Fact]
    public void CompareSalinity_NothingEntered_SaysNotEntered()
    {
        var (status, label) = MineralDosingCalculator.CompareSalinity(current: null, target: null);

        status.Should().Be(MineralDosingCalculator.SalinityUnknown);
        label.Should().Be("Chưa nhập độ mặn");
    }

    [Fact]
    public void CompareSalinity_TargetWithoutCurrent_SaysWhichHalfMissing()
    {
        var (status, label) = MineralDosingCalculator.CompareSalinity(current: null, target: 10m);

        status.Should().Be(MineralDosingCalculator.SalinityUnknown);
        label.Should().Contain("hiện tại"); // chỉ rõ thiếu vế nào
    }

    [Theory]
    [InlineData(5, 10, "SALINITY_LOW")]
    [InlineData(15, 10, "SALINITY_HIGH")]
    [InlineData(10, 10, "SALINITY_OK")]
    public void CompareSalinity_BothEntered_ComparesAsBefore(
        double current, double target, string expected)
    {
        var (status, _) = MineralDosingCalculator.CompareSalinity(
            current: (decimal)current, target: (decimal)target);

        status.Should().Be(expected);
    }

    [Fact]
    public void Calculate_SalinityEnteredWithoutTarget_DoesNotClaimMissingData()
    {
        // Đúng ca trong ảnh: 1000 L, 20‰, KHÔNG nhập mức mong muốn.
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 20m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 1100m));

        r.SalinityStatus.Should().Be(MineralDosingCalculator.SalinityNoTarget);
        r.SalinityStatusLabel.Should().Contain("20‰");
    }

    // ── Số học gram: kiểm bằng tay cho đúng ca trong ảnh ────────────────────
    // 1000 L, Ca 200→400, Mg 1100→1200. Mg thiếu 100 (bằng NỬA Ca) nhưng cần
    // MgCl2 NHIỀU HƠN CaCl2, vì MgCl2·6H2O chỉ có 11,78% Mg còn CaCl2·2H2O có 26,17% Ca.

    [Fact]
    public void Calculate_GramsMatchHandComputation_ForScreenshotCase()
    {
        var r = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m,
            SalinityCurrentPpt: 20m,
            CalciumCurrentMgL: 200m,
            MagnesiumCurrentMgL: 1100m));

        // 200 g Ca ÷ 0,2617 = 764,2 g
        r.CaCl2DoseGrams.Should().BeApproximately(764.2m, 0.5m);
        // 100 g Mg ÷ 0,1178 = 848,9 g
        r.MgCl2DoseGrams.Should().BeApproximately(848.9m, 0.5m);

        // San phẩm Mg nặng hơn Ca dù thiếu ít hơn — đúng, không phải lỗi.
        r.MgCl2DoseGrams.Should().BeGreaterThan(r.CaCl2DoseGrams!.Value);
    }

    [Fact]
    public void Calculate_LargerDeficitCallsForMoreProduct_SameMineral()
    {
        // Nội suy: thiếu Ca gấp đôi ⇒ số gram CaCl2 gấp đôi. Chốt công thức là tuyến tính
        // theo chênh lệch, nên nếu ai đó sửa thành phi tuyến thì test này gãy.
        var half = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m, SalinityCurrentPpt: 20m, CalciumCurrentMgL: 300m));
        var full = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m, SalinityCurrentPpt: 20m, CalciumCurrentMgL: 200m));

        full.CalciumDeficitMgL.Should().Be(half.CalciumDeficitMgL!.Value * 2);
        full.CaCl2DoseGrams.Should().BeApproximately(half.CaCl2DoseGrams!.Value * 2, 0.5m);
    }

    [Fact]
    public void Calculate_VolumeScalesDoseLinearly()
    {
        // 2000 L ⇒ gấp đôi gram của 1000 L.
        var one = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 1000m, SalinityCurrentPpt: 20m, CalciumCurrentMgL: 200m));
        var two = MineralDosingCalculator.Calculate(new MineralDoseRequest(
            WaterVolumeL: 2000m, SalinityCurrentPpt: 20m, CalciumCurrentMgL: 200m));

        two.CaCl2DoseGrams.Should().BeApproximately(one.CaCl2DoseGrams!.Value * 2, 0.5m);
    }
}
