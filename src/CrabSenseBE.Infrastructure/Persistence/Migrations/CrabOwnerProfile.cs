using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations;

/// <summary>Cua: mã/QR/loại/giới tính/đặc điểm + lịch sử tách bảng.</summary>
public partial class CrabOwnerProfile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Code",
            schema: "be",
            table: "Crabs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "QrCode",
            schema: "be",
            table: "Crabs",
            type: "character varying(48)",
            maxLength: 48,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CrabType",
            schema: "be",
            table: "Crabs",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Gender",
            schema: "be",
            table: "Crabs",
            type: "text",
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.AddColumn<decimal>(
            name: "InitialWeightGram",
            schema: "be",
            table: "Crabs",
            type: "numeric(10,2)",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "CarapaceWidthMm",
            schema: "be",
            table: "Crabs",
            type: "numeric(8,2)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "InitialCondition",
            schema: "be",
            table: "Crabs",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Notes",
            schema: "be",
            table: "Crabs",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Condition",
            schema: "be",
            table: "Crabs",
            type: "text",
            nullable: false,
            defaultValue: "Normal");

        migrationBuilder.AddColumn<string>(
            name: "AiPrediction",
            schema: "be",
            table: "Crabs",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "AiConfidence",
            schema: "be",
            table: "Crabs",
            type: "numeric(5,2)",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CrabId",
            schema: "be",
            table: "QrCodes",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "CrabStatusHistories",
            schema: "be",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                OldCondition = table.Column<string>(type: "text", nullable: true),
                NewCondition = table.Column<string>(type: "text", nullable: false),
                OldStatus = table.Column<string>(type: "text", nullable: true),
                NewStatus = table.Column<string>(type: "text", nullable: false),
                ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Source = table.Column<string>(type: "text", nullable: false),
                Reason = table.Column<string>(type: "text", nullable: true),
                ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CrabStatusHistories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "CrabWeightHistories",
            schema: "be",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                WeightGram = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                MeasuredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Source = table.Column<string>(type: "text", nullable: false),
                Notes = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CrabWeightHistories", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "CrabAiAnalyses",
            schema: "be",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                BoxId = table.Column<Guid>(type: "uuid", nullable: true),
                Prediction = table.Column<string>(type: "text", nullable: false),
                Confidence = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                ActivityLevel = table.Column<string>(type: "text", nullable: true),
                AnomalyNote = table.Column<string>(type: "text", nullable: true),
                MediaUrl = table.Column<string>(type: "text", nullable: true),
                ModelVersion = table.Column<string>(type: "text", nullable: true),
                AnalyzedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CrabAiAnalyses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "CrabHarvestHistories",
            schema: "be",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CrabId = table.Column<Guid>(type: "uuid", nullable: false),
                HarvestLineId = table.Column<Guid>(type: "uuid", nullable: true),
                HarvestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                WeightGram = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                Grade = table.Column<string>(type: "text", nullable: true),
                Notes = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CrabHarvestHistories", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CrabStatusHistories_CrabId",
            schema: "be",
            table: "CrabStatusHistories",
            column: "CrabId");

        migrationBuilder.CreateIndex(
            name: "IX_CrabWeightHistories_CrabId",
            schema: "be",
            table: "CrabWeightHistories",
            column: "CrabId");

        migrationBuilder.CreateIndex(
            name: "IX_CrabAiAnalyses_CrabId",
            schema: "be",
            table: "CrabAiAnalyses",
            column: "CrabId");

        migrationBuilder.CreateIndex(
            name: "IX_CrabHarvestHistories_CrabId",
            schema: "be",
            table: "CrabHarvestHistories",
            column: "CrabId");

        migrationBuilder.CreateIndex(
            name: "IX_QrCodes_CrabId",
            schema: "be",
            table: "QrCodes",
            column: "CrabId");

        migrationBuilder.CreateIndex(
            name: "IX_Crabs_Code",
            schema: "be",
            table: "Crabs",
            column: "Code",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CrabStatusHistories", schema: "be");
        migrationBuilder.DropTable(name: "CrabWeightHistories", schema: "be");
        migrationBuilder.DropTable(name: "CrabAiAnalyses", schema: "be");
        migrationBuilder.DropTable(name: "CrabHarvestHistories", schema: "be");

        migrationBuilder.DropIndex(name: "IX_Crabs_Code", schema: "be", table: "Crabs");
        migrationBuilder.DropIndex(name: "IX_QrCodes_CrabId", schema: "be", table: "QrCodes");

        migrationBuilder.DropColumn(name: "Code", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "QrCode", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "CrabType", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "Gender", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "InitialWeightGram", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "CarapaceWidthMm", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "InitialCondition", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "Notes", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "Condition", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "AiPrediction", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "AiConfidence", schema: "be", table: "Crabs");
        migrationBuilder.DropColumn(name: "CrabId", schema: "be", table: "QrCodes");
    }
}
