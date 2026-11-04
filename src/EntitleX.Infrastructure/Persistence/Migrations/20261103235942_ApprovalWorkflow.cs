using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approval_cases",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_extension_days = table.Column<int>(type: "integer", nullable: false),
                    band = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    current_stage_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_cases", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "approval_decisions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_order = table.Column<int>(type: "integer", nullable: false),
                    stage_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    actor_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    actor_display_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approval_case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_decisions", x => x.id);
                    table.ForeignKey(
                        name: "FK_approval_decisions_approval_cases_approval_case_id",
                        column: x => x.approval_case_id,
                        principalSchema: "public",
                        principalTable: "approval_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "approval_stages",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage_order = table.Column<int>(type: "integer", nullable: false),
                    required_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    decision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    approval_case_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approval_stages", x => x.id);
                    table.ForeignKey(
                        name: "FK_approval_stages_approval_cases_approval_case_id",
                        column: x => x.approval_case_id,
                        principalSchema: "public",
                        principalTable: "approval_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_approval_cases_request_id",
                schema: "public",
                table: "approval_cases",
                column: "request_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_approval_decisions_approval_case_id",
                schema: "public",
                table: "approval_decisions",
                column: "approval_case_id");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_decided_at_utc",
                schema: "public",
                table: "approval_decisions",
                column: "decided_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_approval_decisions_request_id",
                schema: "public",
                table: "approval_decisions",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ux_approval_stages_case_order",
                schema: "public",
                table: "approval_stages",
                columns: new[] { "approval_case_id", "stage_order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approval_decisions",
                schema: "public");

            migrationBuilder.DropTable(
                name: "approval_stages",
                schema: "public");

            migrationBuilder.DropTable(
                name: "approval_cases",
                schema: "public");
        }
    }
}
