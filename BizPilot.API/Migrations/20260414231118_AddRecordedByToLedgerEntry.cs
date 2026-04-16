using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BizPilot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordedByToLedgerEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordedByName",
                table: "LedgerEntries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByUserId",
                table: "LedgerEntries",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordedByName",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "LedgerEntries");
        }
    }
}
