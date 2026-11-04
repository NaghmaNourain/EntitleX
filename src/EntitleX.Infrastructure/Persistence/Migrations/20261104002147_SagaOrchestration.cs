using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SagaOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "license_extension_sagas",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    approval_band = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requires_deal_desk = table.Column<bool>(type: "boolean", nullable: false),
                    renewal_manager_approved = table.Column<bool>(type: "boolean", nullable: false),
                    deal_desk_approved = table.Column<bool>(type: "boolean", nullable: false),
                    timeout_kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    timeout_deadline_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_extension_sagas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saga_item_outcomes",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entitlement_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    logical_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SagaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saga_item_outcomes", x => x.id);
                    table.ForeignKey(
                        name: "FK_saga_item_outcomes_license_extension_sagas_SagaId",
                        column: x => x.SagaId,
                        principalSchema: "public",
                        principalTable: "license_extension_sagas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saga_transitions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    to_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SagaId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_saga_transitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_saga_transitions_license_extension_sagas_SagaId",
                        column: x => x.SagaId,
                        principalSchema: "public",
                        principalTable: "license_extension_sagas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_sagas_state",
                schema: "public",
                table: "license_extension_sagas",
                column: "state");

            migrationBuilder.CreateIndex(
                name: "ix_license_extension_sagas_timeout_deadline_utc",
                schema: "public",
                table: "license_extension_sagas",
                column: "timeout_deadline_utc");

            migrationBuilder.CreateIndex(
                name: "ux_license_extension_sagas_request_id",
                schema: "public",
                table: "license_extension_sagas",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saga_item_outcomes_saga_id",
                schema: "public",
                table: "saga_item_outcomes",
                column: "SagaId");

            migrationBuilder.CreateIndex(
                name: "ux_saga_item_outcomes_logical_operation_id",
                schema: "public",
                table: "saga_item_outcomes",
                column: "logical_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saga_transitions_occurred_at_utc",
                schema: "public",
                table: "saga_transitions",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_saga_transitions_saga_id",
                schema: "public",
                table: "saga_transitions",
                column: "SagaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saga_item_outcomes",
                schema: "public");

            migrationBuilder.DropTable(
                name: "saga_transitions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "license_extension_sagas",
                schema: "public");
        }
    }
}
