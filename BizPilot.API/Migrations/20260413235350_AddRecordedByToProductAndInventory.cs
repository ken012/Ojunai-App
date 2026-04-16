using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BizPilot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddRecordedByToProductAndInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordedByName",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByUserId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordedByName",
                table: "InventoryTransactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecordedByUserId",
                table: "InventoryTransactions",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordedByName",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "RecordedByName",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "RecordedByUserId",
                table: "InventoryTransactions");
        }
    }
}
