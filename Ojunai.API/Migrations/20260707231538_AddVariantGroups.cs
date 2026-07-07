using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VariantGroupId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VariantOptions",
                table: "Products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VariantsEnabled",
                table: "Businesses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "VariantGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Axes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VariantGroups_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_VariantGroupId",
                table: "Products",
                column: "VariantGroupId",
                filter: "\"VariantGroupId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VariantGroups_BusinessId_CreatedAtUtc",
                table: "VariantGroups",
                columns: new[] { "BusinessId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VariantGroups");

            migrationBuilder.DropIndex(
                name: "IX_Products_VariantGroupId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VariantGroupId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VariantOptions",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "VariantsEnabled",
                table: "Businesses");
        }
    }
}
