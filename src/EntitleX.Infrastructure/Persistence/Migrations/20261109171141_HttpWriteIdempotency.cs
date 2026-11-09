using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HttpWriteIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "http_write_idempotency_records",
                schema: "public",
                columns: table => new
                {
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    request_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_http_write_idempotency_records", x => x.idempotency_key);
                });

            migrationBuilder.CreateIndex(
                name: "ix_http_write_idempotency_operation_actor",
                schema: "public",
                table: "http_write_idempotency_records",
                columns: new[] { "operation", "actor_user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "http_write_idempotency_records",
                schema: "public");
        }
    }
}
