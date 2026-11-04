using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceObservabilityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_locked",
                schema: "public",
                table: "outbox_messages",
                columns: new[] { "status", "locked_until_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_sagas_state_updated",
                schema: "public",
                table: "license_extension_sagas",
                columns: new[] { "state", "updated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_request_occurred_id",
                schema: "public",
                table: "audit_events",
                columns: new[] { "request_id", "occurred_at_utc", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_status_locked",
                schema: "public",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "ix_license_extension_sagas_state_updated",
                schema: "public",
                table: "license_extension_sagas");

            migrationBuilder.DropIndex(
                name: "ix_audit_events_request_occurred_id",
                schema: "public",
                table: "audit_events");
        }
    }
}
