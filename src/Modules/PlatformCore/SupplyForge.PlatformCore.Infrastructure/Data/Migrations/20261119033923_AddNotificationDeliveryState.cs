using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupplyForge.PlatformCore.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeliveryAttempts",
                schema: "platform",
                table: "Notification",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Pre-existing rows predate delivery tracking (ADR-008): backfill
            // them as visible Pending with NextAttemptAt NULL, so the retry
            // sweep never picks them up (they were never attempted) and the
            // operator closes them via manual mark-sent (TD-2).
            migrationBuilder.AddColumn<string>(
                name: "DeliveryStatus",
                schema: "platform",
                table: "Notification",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<string>(
                name: "LastDeliveryError",
                schema: "platform",
                table: "Notification",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "platform",
                table: "Notification",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentAt",
                schema: "platform",
                table: "Notification",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notification_DeliveryStatus_NextAttemptAt",
                schema: "platform",
                table: "Notification",
                columns: new[] { "DeliveryStatus", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notification_DeliveryStatus_NextAttemptAt",
                schema: "platform",
                table: "Notification");

            migrationBuilder.DropColumn(
                name: "DeliveryAttempts",
                schema: "platform",
                table: "Notification");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                schema: "platform",
                table: "Notification");

            migrationBuilder.DropColumn(
                name: "LastDeliveryError",
                schema: "platform",
                table: "Notification");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "platform",
                table: "Notification");

            migrationBuilder.DropColumn(
                name: "SentAt",
                schema: "platform",
                table: "Notification");
        }
    }
}
