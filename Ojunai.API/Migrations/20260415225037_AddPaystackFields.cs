using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaystackFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaystackCustomerCode",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaystackPlanCode",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaystackSubscriptionCode",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionEndsAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaystackCustomerCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PaystackPlanCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "PaystackSubscriptionCode",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "SubscriptionEndsAt",
                table: "Businesses");
        }
    }
}
