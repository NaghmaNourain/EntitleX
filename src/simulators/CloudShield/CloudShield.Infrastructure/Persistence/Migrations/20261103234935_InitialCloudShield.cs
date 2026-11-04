using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudShield.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCloudShield : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "public");

            migrationBuilder.CreateTable(
                name: "applied_license_operations",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    logical_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entitlement_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    previous_expiry_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    new_expiry_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applied_license_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entitlements",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entitlement_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    customer_account_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    product_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    product_group = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entitlements", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_applied_license_operations_entitlement_key",
                schema: "public",
                table: "applied_license_operations",
                column: "entitlement_key");

            migrationBuilder.CreateIndex(
                name: "ux_applied_license_operations_logical_operation_id",
                schema: "public",
                table: "applied_license_operations",
                column: "logical_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entitlements_customer_account_id",
                schema: "public",
                table: "entitlements",
                column: "customer_account_id");

            migrationBuilder.CreateIndex(
                name: "ux_entitlements_entitlement_key",
                schema: "public",
                table: "entitlements",
                column: "entitlement_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applied_license_operations",
                schema: "public");

            migrationBuilder.DropTable(
                name: "entitlements",
                schema: "public");
        }
    }
}
