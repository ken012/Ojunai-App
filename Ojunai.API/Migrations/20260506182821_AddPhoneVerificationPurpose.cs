using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneVerificationPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhoneVerificationCodes_PhoneNumber_ExpiresAtUtc",
                table: "PhoneVerificationCodes");

            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "PhoneVerificationCodes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationCodes_PhoneNumber_Purpose_ExpiresAtUtc",
                table: "PhoneVerificationCodes",
                columns: new[] { "PhoneNumber", "Purpose", "ExpiresAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhoneVerificationCodes_PhoneNumber_Purpose_ExpiresAtUtc",
                table: "PhoneVerificationCodes");

            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "PhoneVerificationCodes");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneVerificationCodes_PhoneNumber_ExpiresAtUtc",
                table: "PhoneVerificationCodes",
                columns: new[] { "PhoneNumber", "ExpiresAtUtc" });
        }
    }
}
