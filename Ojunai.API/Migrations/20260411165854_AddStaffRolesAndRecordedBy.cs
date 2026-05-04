using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffRolesAndRecordedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordedByName",
                table: "Sales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByUserId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordedByName",
                table: "Expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByUserId",
                table: "Expenses",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordedByName",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "RecordedByName",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "Expenses");
        }
    }
}
