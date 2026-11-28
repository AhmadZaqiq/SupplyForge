using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class A1_ReplenishmentAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IncomingProjection",
                schema: "inventory",
                columns: table => new
                {
                    IncomingProjectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedDeliveryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpectedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingProjection", x => x.IncomingProjectionId);
                });

            migrationBuilder.CreateTable(
                name: "ReorderPoint",
                schema: "inventory",
                columns: table => new
                {
                    ReorderPointId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReorderPointQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IsAlerted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReorderPoint", x => x.ReorderPointId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingProjection_ExpectedDate",
                schema: "inventory",
                table: "IncomingProjection",
                column: "ExpectedDate");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingProjection_ProductId_WarehouseId",
                schema: "inventory",
                table: "IncomingProjection",
                columns: new[] { "ProductId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReorderPoint_IsAlerted",
                schema: "inventory",
                table: "ReorderPoint",
                column: "IsAlerted");

            migrationBuilder.CreateIndex(
                name: "IX_ReorderPoint_ProductId_WarehouseId",
                schema: "inventory",
                table: "ReorderPoint",
                columns: new[] { "ProductId", "WarehouseId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IncomingProjection",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "ReorderPoint",
                schema: "inventory");
        }
    }
}
