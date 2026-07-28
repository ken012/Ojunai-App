using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyNudgeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AlertDailyNudges",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "LastDailyNudgeOn",
                table: "Businesses",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AlertDailyNudges",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "LastDailyNudgeOn",
                table: "Businesses");
        }
    }
}
