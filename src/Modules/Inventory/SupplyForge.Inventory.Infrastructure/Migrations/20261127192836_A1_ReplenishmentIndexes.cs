using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class A1_ReplenishmentIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReorderPoint_ProductId_WarehouseId",
                schema: "inventory",
                table: "ReorderPoint");

            migrationBuilder.DropIndex(
                name: "IX_IncomingProjection_ProductId_WarehouseId",
                schema: "inventory",
                table: "IncomingProjection");

            migrationBuilder.CreateIndex(
                name: "IX_ReorderPoint_ProductId",
                schema: "inventory",
                table: "ReorderPoint",
                column: "ProductId",
                unique: true,
                filter: "[WarehouseId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReorderPoint_ProductId_WarehouseId",
                schema: "inventory",
                table: "ReorderPoint",
                columns: new[] { "ProductId", "WarehouseId" },
                unique: true,
                filter: "[WarehouseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IncomingProjection_ExpectedDeliveryId_ProductId_WarehouseId",
                schema: "inventory",
                table: "IncomingProjection",
                columns: new[] { "ExpectedDeliveryId", "ProductId", "WarehouseId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReorderPoint_ProductId",
                schema: "inventory",
                table: "ReorderPoint");

            migrationBuilder.DropIndex(
                name: "IX_ReorderPoint_ProductId_WarehouseId",
                schema: "inventory",
                table: "ReorderPoint");

            migrationBuilder.DropIndex(
                name: "IX_IncomingProjection_ExpectedDeliveryId_ProductId_WarehouseId",
                schema: "inventory",
                table: "IncomingProjection");

            migrationBuilder.CreateIndex(
                name: "IX_ReorderPoint_ProductId_WarehouseId",
                schema: "inventory",
                table: "ReorderPoint",
                columns: new[] { "ProductId", "WarehouseId" });

            migrationBuilder.CreateIndex(
                name: "IX_IncomingProjection_ProductId_WarehouseId",
                schema: "inventory",
                table: "IncomingProjection",
                columns: new[] { "ProductId", "WarehouseId" });
        }
    }
}
