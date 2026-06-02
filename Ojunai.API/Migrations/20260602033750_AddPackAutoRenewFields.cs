using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPackAutoRenewFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAutoRenew",
                table: "BusinessAddOns",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSubscriptionId",
                table: "BusinessAddOns",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAddOns_ProviderSubscriptionId",
                table: "BusinessAddOns",
                column: "ProviderSubscriptionId",
                filter: "\"ProviderSubscriptionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessAddOns_ProviderSubscriptionId",
                table: "BusinessAddOns");

            migrationBuilder.DropColumn(
                name: "IsAutoRenew",
                table: "BusinessAddOns");

            migrationBuilder.DropColumn(
                name: "ProviderSubscriptionId",
                table: "BusinessAddOns");
        }
    }
}
