using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approval_reminder_records",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    approval_case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_order = table.Column<int>(type: "integer", nullable: false),
                    window_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    reminder_sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    escalated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terminal_state_applied = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_reminder_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expiration_notification_records",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entitlement_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    customer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    window_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    expiry_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expiration_notification_records", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_approval_reminder_records_case_stage",
                schema: "public",
                table: "approval_reminder_records",
                columns: new[] { "approval_case_id", "stage_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_approval_reminder_records_window_key",
                schema: "public",
                table: "approval_reminder_records",
                column: "window_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expiration_notification_records_provider_entitlement",
                schema: "public",
                table: "expiration_notification_records",
                columns: new[] { "provider", "entitlement_id" });

            migrationBuilder.CreateIndex(
                name: "ux_expiration_notification_records_window_key",
                schema: "public",
                table: "expiration_notification_records",
                column: "window_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_reminder_records",
                schema: "public");

            migrationBuilder.DropTable(
                name: "expiration_notification_records",
                schema: "public");
        }
    }
}
