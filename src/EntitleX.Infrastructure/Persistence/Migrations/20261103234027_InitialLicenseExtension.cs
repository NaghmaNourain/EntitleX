using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialLicenseExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "license_extension_requests",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    extension_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    approval_version = table.Column<int>(type: "integer", nullable: false),
                    approved_scope_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    requested_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_extension_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_license_extension_requests_license_extension_requests_paren~",
                        column: x => x.parent_id,
                        principalSchema: "public",
                        principalTable: "license_extension_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "license_extension_request_items",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entitlement_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    logical_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_expiry_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    requested_expiry_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_extension_request_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_license_extension_request_items_license_extension_requests_~",
                        column: x => x.request_id,
                        principalSchema: "public",
                        principalTable: "license_extension_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_request_items_entitlement_id",
                schema: "public",
                table: "license_extension_request_items",
                column: "entitlement_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_request_items_logical_operation_id",
                schema: "public",
                table: "license_extension_request_items",
                column: "logical_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_request_items_request_id",
                schema: "public",
                table: "license_extension_request_items",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_requests_created_at_utc",
                schema: "public",
                table: "license_extension_requests",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_requests_parent_id",
                schema: "public",
                table: "license_extension_requests",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_requests_status",
                schema: "public",
                table: "license_extension_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_license_extension_requests_extension_number",
                schema: "public",
                table: "license_extension_requests",
                column: "extension_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "license_extension_request_items",
                schema: "public");

            migrationBuilder.DropTable(
                name: "license_extension_requests",
                schema: "public");
        }
    }
}
