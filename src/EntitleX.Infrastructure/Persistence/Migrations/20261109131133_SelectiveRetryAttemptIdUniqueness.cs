using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SelectiveRetryAttemptIdUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_saga_item_outcomes_logical_operation_id",
                schema: "public",
                table: "saga_item_outcomes");

            migrationBuilder.DropIndex(
                name: "IX_fulfillment_item_results_logical_operation_id",
                schema: "public",
                table: "fulfillment_item_results");

            migrationBuilder.AddColumn<Guid>(
                name: "attempt_id",
                schema: "public",
                table: "fulfillment_item_results",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_saga_item_outcomes_logical_operation_id",
                schema: "public",
                table: "saga_item_outcomes",
                column: "logical_operation_id");

            migrationBuilder.CreateIndex(
                name: "ux_saga_item_outcomes_saga_logical_operation_id",
                schema: "public",
                table: "saga_item_outcomes",
                columns: new[] { "SagaId", "logical_operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_item_results_logical_operation_id",
                schema: "public",
                table: "fulfillment_item_results",
                column: "logical_operation_id");

            migrationBuilder.CreateIndex(
                name: "ux_fulfillment_item_results_logical_attempt",
                schema: "public",
                table: "fulfillment_item_results",
                columns: new[] { "logical_operation_id", "attempt_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_saga_item_outcomes_logical_operation_id",
                schema: "public",
                table: "saga_item_outcomes");

            migrationBuilder.DropIndex(
                name: "ux_saga_item_outcomes_saga_logical_operation_id",
                schema: "public",
                table: "saga_item_outcomes");

            migrationBuilder.DropIndex(
                name: "ix_fulfillment_item_results_logical_operation_id",
                schema: "public",
                table: "fulfillment_item_results");

            migrationBuilder.DropIndex(
                name: "ux_fulfillment_item_results_logical_attempt",
                schema: "public",
                table: "fulfillment_item_results");

            migrationBuilder.DropColumn(
                name: "attempt_id",
                schema: "public",
                table: "fulfillment_item_results");

            migrationBuilder.CreateIndex(
                name: "ux_saga_item_outcomes_logical_operation_id",
                schema: "public",
                table: "saga_item_outcomes",
                column: "logical_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fulfillment_item_results_logical_operation_id",
                schema: "public",
                table: "fulfillment_item_results",
                column: "logical_operation_id",
                unique: true);
        }
    }
}
