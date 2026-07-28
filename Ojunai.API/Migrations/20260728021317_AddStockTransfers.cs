using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddStockTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedByName = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_BusinessId_CreatedAtUtc",
                table: "StockTransfers",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_BusinessId_ProductId",
                table: "StockTransfers",
                columns: new[] { "BusinessId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockTransfers");
        }
    }
}
