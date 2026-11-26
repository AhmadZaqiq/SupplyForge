using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class A1TransferOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Adjustment",
                schema: "inventory",
                columns: table => new
                {
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Adjustment", x => x.AdjustmentId);
                });

            migrationBuilder.CreateTable(
                name: "OpeningImport",
                schema: "inventory",
                columns: table => new
                {
                    OpeningImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningImport", x => x.OpeningImportId);
                });

            migrationBuilder.CreateTable(
                name: "Transfer",
                schema: "inventory",
                columns: table => new
                {
                    TransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestWarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DestLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ShippedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReceivedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfer", x => x.TransferId);
                    table.CheckConstraint("CK_Transfer_Qty_Positive", "[Qty] > 0");
                    table.CheckConstraint("CK_Transfer_ReceivedQty_NonNegative", "[ReceivedQty] >= 0");
                    table.CheckConstraint("CK_Transfer_ShippedQty_NonNegative", "[ShippedQty] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "AdjustmentLine",
                schema: "inventory",
                columns: table => new
                {
                    AdjustmentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdjustmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    MovementType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjustmentLine", x => x.AdjustmentLineId);
                    table.ForeignKey(
                        name: "FK_AdjustmentLine_Adjustment_AdjustmentId",
                        column: x => x.AdjustmentId,
                        principalSchema: "inventory",
                        principalTable: "Adjustment",
                        principalColumn: "AdjustmentId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OpeningImportLine",
                schema: "inventory",
                columns: table => new
                {
                    OpeningImportLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OpeningImportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpeningImportLine", x => x.OpeningImportLineId);
                    table.CheckConstraint("CK_OpeningImportLine_Qty_Positive", "[Qty] > 0");
                    table.ForeignKey(
                        name: "FK_OpeningImportLine_OpeningImport_OpeningImportId",
                        column: x => x.OpeningImportId,
                        principalSchema: "inventory",
                        principalTable: "OpeningImport",
                        principalColumn: "OpeningImportId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustment_SourceDocumentType_SourceDocumentId",
                schema: "inventory",
                table: "Adjustment",
                columns: new[] { "SourceDocumentType", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Adjustment_Status",
                schema: "inventory",
                table: "Adjustment",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AdjustmentLine_AdjustmentId",
                schema: "inventory",
                table: "AdjustmentLine",
                column: "AdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningImportLine_OpeningImportId",
                schema: "inventory",
                table: "OpeningImportLine",
                column: "OpeningImportId");

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_DestWarehouseId_Status",
                schema: "inventory",
                table: "Transfer",
                columns: new[] { "DestWarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfer_SourceWarehouseId_Status",
                schema: "inventory",
                table: "Transfer",
                columns: new[] { "SourceWarehouseId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdjustmentLine",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "OpeningImportLine",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Transfer",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Adjustment",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "OpeningImport",
                schema: "inventory");
        }
    }
}
