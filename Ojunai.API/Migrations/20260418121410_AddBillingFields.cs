using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingCurrency",
                table: "Businesses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "Businesses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillingProvider",
                table: "Businesses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FlutterwaveCustomerId",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlutterwaveSubscriptionId",
                table: "Businesses",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingCurrency",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "BillingProvider",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "FlutterwaveCustomerId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "FlutterwaveSubscriptionId",
                table: "Businesses");
        }
    }
}
