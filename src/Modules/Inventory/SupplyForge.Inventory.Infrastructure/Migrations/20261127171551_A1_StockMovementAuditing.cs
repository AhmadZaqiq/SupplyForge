using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class A1_StockMovementAuditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BranchId",
                schema: "inventory",
                table: "StockMovement",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "inventory",
                table: "StockMovement",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                schema: "inventory",
                table: "StockMovement",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_BranchId",
                schema: "inventory",
                table: "StockMovement",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovement_CorrelationId",
                schema: "inventory",
                table: "StockMovement",
                column: "CorrelationId");

            // INV-010/INV-012: DB-level immutability — INSTEAD OF triggers block
            // UPDATE/DELETE even when EF Core's change tracker is bypassed.
            migrationBuilder.Sql(@"
                CREATE TRIGGER inventory.Trg_StockMovement_NoUpdate
                ON inventory.StockMovement
                INSTEAD OF UPDATE
                AS
                BEGIN
                    RAISERROR('StockMovement is immutable: UPDATE is not allowed (INV-010).', 16, 1);
                    ROLLBACK;
                END");

            migrationBuilder.Sql(@"
                CREATE TRIGGER inventory.Trg_StockMovement_NoDelete
                ON inventory.StockMovement
                INSTEAD OF DELETE
                AS
                BEGIN
                    RAISERROR('StockMovement is immutable: DELETE is not allowed (INV-012).', 16, 1);
                    ROLLBACK;
                END");

            // DENY at database level as defense in depth (TRUNCATE bypasses INSTEAD OF,
            // but requires ALTER permission which is a higher bar).
            migrationBuilder.Sql(@"
                DENY UPDATE ON inventory.StockMovement TO public;
                DENY DELETE ON inventory.StockMovement TO public;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                GRANT UPDATE ON inventory.StockMovement TO public;
                GRANT DELETE ON inventory.StockMovement TO public;");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'Trg_StockMovement_NoDelete' AND schema_id = SCHEMA_ID('inventory'))
                    DROP TRIGGER inventory.Trg_StockMovement_NoDelete");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.triggers WHERE name = 'Trg_StockMovement_NoUpdate' AND schema_id = SCHEMA_ID('inventory'))
                    DROP TRIGGER inventory.Trg_StockMovement_NoUpdate");

            migrationBuilder.DropIndex(
                name: "IX_StockMovement_BranchId",
                schema: "inventory",
                table: "StockMovement");

            migrationBuilder.DropIndex(
                name: "IX_StockMovement_CorrelationId",
                schema: "inventory",
                table: "StockMovement");

            migrationBuilder.DropColumn(
                name: "BranchId",
                schema: "inventory",
                table: "StockMovement");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "inventory",
                table: "StockMovement");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                schema: "inventory",
                table: "StockMovement");
        }
    }
}