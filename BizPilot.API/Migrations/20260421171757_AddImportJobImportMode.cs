using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BizPilot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddImportJobImportMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportMode",
                table: "ImportJobs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportMode",
                table: "ImportJobs");
        }
    }
}
