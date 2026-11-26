using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class A1InventoryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "ProductCost",
                schema: "inventory",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AvgCost = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    QtyOnHand = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCost", x => new { x.ProductId, x.WarehouseId });
                });

            migrationBuilder.CreateTable(
                name: "StockBalance",
                schema: "inventory",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnHand = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockBalance", x => new { x.ProductId, x.WarehouseId, x.LocationId });
                    table.CheckConstraint("CK_StockBalance_OnHand_NonNegative", "[OnHand] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "StockMovement",
                schema: "inventory",
                columns: table => new
                {
                    MovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MovementType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReversalOfMovementId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovement", x => x.MovementId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_MovementType_CreatedAt",
                schema: "inventory",
                table: "StockMovement",
                columns: new[] { "MovementType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_ProductId_WarehouseId_LocationId",
                schema: "inventory",
                table: "StockMovement",
                columns: new[] { "ProductId", "WarehouseId", "LocationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductCost",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockBalance",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockMovement",
                schema: "inventory");
        }
    }
}
