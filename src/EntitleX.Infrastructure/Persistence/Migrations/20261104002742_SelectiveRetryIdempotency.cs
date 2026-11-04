using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelectiveRetryIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notes",
                schema: "public",
                table: "license_extension_requests",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "api_idempotency_records",
                schema: "public",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retry_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_idempotency_records", x => x.idempotency_key);
                });

            migrationBuilder.CreateTable(
                name: "retry_history",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retry_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    extension_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    item_count = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retry_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_retry_history_license_extension_requests_source_request_id",
                        column: x => x.source_request_id,
                        principalSchema: "public",
                        principalTable: "license_extension_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "retry_item_claims",
                schema: "public",
                columns: table => new
                {
                    source_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retry_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    claimed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retry_item_claims", x => new { x.source_request_id, x.logical_operation_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_idempotency_records_source_request_id",
                schema: "public",
                table: "api_idempotency_records",
                column: "source_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_retry_history_source_request_id",
                schema: "public",
                table: "retry_history",
                column: "source_request_id");

            migrationBuilder.CreateIndex(
                name: "ux_retry_history_retry_request_id",
                schema: "public",
                table: "retry_history",
                column: "retry_request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retry_item_claims_idempotency_key",
                schema: "public",
                table: "retry_item_claims",
                column: "idempotency_key");

            migrationBuilder.CreateIndex(
                name: "ix_retry_item_claims_retry_request_id",
                schema: "public",
                table: "retry_item_claims",
                column: "retry_request_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_idempotency_records",
                schema: "public");

            migrationBuilder.DropTable(
                name: "retry_history",
                schema: "public");

            migrationBuilder.DropTable(
                name: "retry_item_claims",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "notes",
                schema: "public",
                table: "license_extension_requests");
        }
    }
}
