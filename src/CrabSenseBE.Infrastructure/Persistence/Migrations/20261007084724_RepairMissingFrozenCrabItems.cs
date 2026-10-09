using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CrabSenseBE.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RepairMissingFrozenCrabItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
{
    // Tạo lại bảng sản phẩm cua cấp đông đang bị thiếu trong schema be.
    migrationBuilder.CreateTable(
        name: "FrozenCrabItems",
        schema: "be",
        columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            FrozenLotId = table.Column<Guid>(type: "uuid", nullable: false),
            HarvestLineId = table.Column<Guid>(type: "uuid", nullable: false),
            CrabId = table.Column<Guid>(type: "uuid", nullable: false),
            BarcodeValue = table.Column<string>(
                type: "character varying(40)",
                maxLength: 40,
                nullable: false),
            LotCode = table.Column<string>(
                type: "character varying(64)",
                maxLength: 64,
                nullable: false),
            CrabCode = table.Column<string>(
                type: "character varying(64)",
                maxLength: 64,
                nullable: false),
            HarvestVoucherCode = table.Column<string>(
                type: "character varying(64)",
                maxLength: 64,
                nullable: true),
            HarvestDate = table.Column<DateTime>(
                type: "timestamp with time zone",
                nullable: false),
            FrozenDate = table.Column<DateTime>(
                type: "timestamp with time zone",
                nullable: false),
            ExpiryDate = table.Column<DateTime>(
                type: "timestamp with time zone",
                nullable: false),
            WeightGram = table.Column<decimal>(
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false),
            Grade = table.Column<string>(
                type: "character varying(1)",
                maxLength: 1,
                nullable: false),
            CreatedAt = table.Column<DateTime>(
                type: "timestamp with time zone",
                nullable: false),
            UpdatedAt = table.Column<DateTime>(
                type: "timestamp with time zone",
                nullable: true)
        },
        constraints: table =>
        {
            table.PrimaryKey("PK_FrozenCrabItems", x => x.Id);

            table.ForeignKey(
                name: "FK_FrozenCrabItems_Crabs_CrabId",
                column: x => x.CrabId,
                principalSchema: "be",
                principalTable: "Crabs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            table.ForeignKey(
                name: "FK_FrozenCrabItems_FrozenLots_FrozenLotId",
                column: x => x.FrozenLotId,
                principalSchema: "be",
                principalTable: "FrozenLots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            table.ForeignKey(
                name: "FK_FrozenCrabItems_HarvestLines_HarvestLineId",
                column: x => x.HarvestLineId,
                principalSchema: "be",
                principalTable: "HarvestLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        });

    // Mỗi mã vạch phải duy nhất.
    migrationBuilder.CreateIndex(
        name: "IX_FrozenCrabItems_BarcodeValue",
        schema: "be",
        table: "FrozenCrabItems",
        column: "BarcodeValue",
        unique: true);

    migrationBuilder.CreateIndex(
        name: "IX_FrozenCrabItems_CrabId",
        schema: "be",
        table: "FrozenCrabItems",
        column: "CrabId");

    migrationBuilder.CreateIndex(
        name: "IX_FrozenCrabItems_FrozenLotId",
        schema: "be",
        table: "FrozenCrabItems",
        column: "FrozenLotId");

    // Mỗi dòng thu hoạch chỉ được gắn với một sản phẩm cua cấp đông.
    migrationBuilder.CreateIndex(
        name: "IX_FrozenCrabItems_HarvestLineId",
        schema: "be",
        table: "FrozenCrabItems",
        column: "HarvestLineId",
        unique: true);
}
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
{
    // Chỉ hoàn tác bảng được tạo bởi migration sửa chữa này.
    migrationBuilder.DropTable(
        name: "FrozenCrabItems",
        schema: "be");
}
    }
}
