using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInboundMessageClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboundMessageClaims",
                columns: table => new
                {
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    ProviderMessageId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboundMessageClaims", x => new { x.Channel, x.ProviderMessageId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboundMessageClaims_ClaimedAtUtc",
                table: "InboundMessageClaims",
                column: "ClaimedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboundMessageClaims");
        }
    }
}
