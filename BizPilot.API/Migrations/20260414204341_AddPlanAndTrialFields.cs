using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BizPilot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanAndTrialFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Businesses",
                type: "text",
                nullable: false,
                defaultValue: "pro");

            // Set existing businesses to "pro" so nothing breaks
            migrationBuilder.Sql("UPDATE \"Businesses\" SET \"Plan\" = 'pro' WHERE \"Plan\" = '' OR \"Plan\" IS NULL;");

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Businesses");
        }
    }
}
