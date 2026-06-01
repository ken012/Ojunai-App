using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSignupChannelTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SignupChannelTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedByIdentity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupChannelTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SignupChannelTokens_ExpiresAtUtc",
                table: "SignupChannelTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SignupChannelTokens_Token",
                table: "SignupChannelTokens",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SignupChannelTokens");
        }
    }
}
