namespace CrabSenseBE.Application.DTOs.Reports;
public class HarvestReportDto
{
    public int TotalVouchers { get; set; }

    public int TotalQuantity { get; set; }

    public decimal TotalWeightKg { get; set; }

    public List<HarvestByDateDto> ByDate { get; set; } = [];

    public List<HarvestByGradeDto> ByGrade { get; set; } = [];

    public HarvestSoftshellDto Softshell { get; set; } = new();
}
public class HarvestByDateDto
{
    public DateTime Date { get; set; }

    public int Quantity { get; set; }

    public decimal WeightKg { get; set; }
}

public class HarvestByGradeDto
{
    public string Grade { get; set; } = string.Empty;

    public decimal WeightKg { get; set; }
}

public class HarvestSoftshellDto
{
    public decimal WeightKg { get; set; }
}