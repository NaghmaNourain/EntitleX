using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntitleX.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SagaRowVersionConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "row_version",
                schema: "public",
                table: "license_extension_sagas",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "row_version",
                schema: "public",
                table: "license_extension_sagas");
        }
    }
}
