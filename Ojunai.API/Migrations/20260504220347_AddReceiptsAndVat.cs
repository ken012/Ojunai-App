using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptsAndVat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiptGeneratedAtUtc",
                table: "Sales",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptNumber",
                table: "Sales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "Sales",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NextReceiptNumber",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptPrefix",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VatEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "VatRate",
                table: "Businesses",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReceiptGeneratedAtUtc",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReceiptNumber",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "Address",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "NextReceiptNumber",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ReceiptPrefix",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VatEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VatRate",
                table: "Businesses");
        }
    }
}
