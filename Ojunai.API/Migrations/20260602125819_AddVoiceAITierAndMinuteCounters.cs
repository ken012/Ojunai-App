using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVoiceAITierAndMinuteCounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VoiceAICycleMinutesUsed",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VoiceAITier",
                table: "Businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoiceAITrialMinutesUsed",
                table: "Businesses",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoiceAICycleMinutesUsed",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAITier",
                table: "Businesses");

            migrationBuilder.DropColumn(
                name: "VoiceAITrialMinutesUsed",
                table: "Businesses");
        }
    }
}
