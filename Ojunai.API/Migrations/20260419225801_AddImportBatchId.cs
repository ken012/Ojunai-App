using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "LedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Contacts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_ImportBatchId",
                table: "Products",
                column: "ImportBatchId",
                filter: "\"ImportBatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ImportBatchId",
                table: "LedgerEntries",
                column: "ImportBatchId",
                filter: "\"ImportBatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ImportBatchId",
                table: "Expenses",
                column: "ImportBatchId",
                filter: "\"ImportBatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_ImportBatchId",
                table: "Contacts",
                column: "ImportBatchId",
                filter: "\"ImportBatchId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_ImportBatchId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_ImportBatchId",
                table: "LedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_ImportBatchId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_ImportBatchId",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Contacts");
        }
    }
}
