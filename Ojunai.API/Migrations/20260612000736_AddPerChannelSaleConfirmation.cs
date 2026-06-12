using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPerChannelSaleConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmLargeSaleThresholdMessenger",
                table: "Businesses",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmLargeSaleThresholdTelegram",
                table: "Businesses",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ConfirmLargeSalesMessenger",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ConfirmLargeSalesTelegram",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConfirmLargeSaleThresholdMessenger",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ConfirmLargeSaleThresholdTelegram",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ConfirmLargeSalesMessenger",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "ConfirmLargeSalesTelegram",
                table: "Businesses");
        }
    }
}
