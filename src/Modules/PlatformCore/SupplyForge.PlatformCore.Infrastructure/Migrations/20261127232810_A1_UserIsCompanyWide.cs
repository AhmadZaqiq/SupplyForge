using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.PlatformCore.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class A1_UserIsCompanyWide : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCompanyWide",
                schema: "platform",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCompanyWide",
                schema: "platform",
                table: "User");
        }
    }
}
