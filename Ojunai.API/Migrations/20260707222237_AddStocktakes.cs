using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddStocktakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stocktakes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedByName = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stocktakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stocktakes_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StocktakeItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StocktakeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "unit"),
                    SystemQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    UnitCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StocktakeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StocktakeItems_Stocktakes_StocktakeId",
                        column: x => x.StocktakeId,
                        principalTable: "Stocktakes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StocktakeItems_StocktakeId",
                table: "StocktakeItems",
                column: "StocktakeId");

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_BusinessId_CreatedAtUtc",
                table: "Stocktakes",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Stocktakes_BusinessId_Status",
                table: "Stocktakes",
                columns: new[] { "BusinessId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StocktakeItems");

            migrationBuilder.DropTable(
                name: "Stocktakes");
        }
    }
}
