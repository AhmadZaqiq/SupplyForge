using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.MasterData.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class A1WarehousesLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Warehouse",
                schema: "master_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouse", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Warehouse_Branch_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "master_data",
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorageLocation",
                schema: "master_data",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsBlocked = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageLocation", x => x.Id);
                    table.CheckConstraint("CK_StorageLocation_Type_Valid", "[Type] IN ('Receiving','Quarantine','Storage','Returns','Staging')");
                    table.ForeignKey(
                        name: "FK_StorageLocation_Warehouse_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "master_data",
                        principalTable: "Warehouse",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocation_Type",
                schema: "master_data",
                table: "StorageLocation",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_StorageLocation_WarehouseId_Code",
                schema: "master_data",
                table: "StorageLocation",
                columns: new[] { "WarehouseId", "Code" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_BranchId",
                schema: "master_data",
                table: "Warehouse",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouse_Code",
                schema: "master_data",
                table: "Warehouse",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageLocation",
                schema: "master_data");

            migrationBuilder.DropTable(
                name: "Warehouse",
                schema: "master_data");
        }
    }
}
