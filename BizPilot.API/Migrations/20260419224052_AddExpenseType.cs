using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BizPilot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpenseType",
                table: "Expenses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "operating");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpenseType",
                table: "Expenses");
        }
    }
}
