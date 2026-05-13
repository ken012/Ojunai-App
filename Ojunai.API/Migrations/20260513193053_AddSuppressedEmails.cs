using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSuppressedEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SuppressedEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BounceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    BounceSubType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    RawPayload = table.Column<string>(type: "jsonb", nullable: true),
                    SuppressedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuppressedEmails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SuppressedEmails_Email",
                table: "SuppressedEmails",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SuppressedEmails");
        }
    }
}
