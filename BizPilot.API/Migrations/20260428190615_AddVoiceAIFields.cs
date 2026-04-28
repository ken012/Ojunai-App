using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BizPilot.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceAIFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VoiceAIEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoiceAIEnabledAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VoiceAIInternalOverride",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoiceAIPlanStatus",
                table: "Businesses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "inactive");

            migrationBuilder.AddColumn<DateTime>(
                name: "VoiceAISubscriptionEndsAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoiceAISubscriptionId",
                table: "Businesses",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoiceAITrialEndsAt",
                table: "Businesses",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoiceAIEnabled",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAIEnabledAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAIInternalOverride",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAIPlanStatus",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAISubscriptionEndsAt",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAISubscriptionId",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAITrialEndsAt",
                table: "Businesses");
        }
    }
}
