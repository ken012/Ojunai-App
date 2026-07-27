using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiLocationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Stocktakes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "StockHolds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "PurchaseOrders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "ProductBatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "LedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "InventoryTransactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "DailySummaries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "ContactIdentities",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultLocationId",
                table: "Businesses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "branch"),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NextReceiptNumber = table.Column<int>(type: "integer", nullable: false),
                    ReceiptPrefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductLocationStocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStock = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LowStockThreshold = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLocationStocks", x => x.Id);
                    table.CheckConstraint("CK_ProductLocationStock_CurrentStock_NonNegative", "\"CurrentStock\" >= 0");
                    table.ForeignKey(
                        name: "FK_ProductLocationStocks_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductLocationStocks_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserLocations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sales_BusinessId_LocationId_CreatedAtUtc",
                table: "Sales",
                columns: new[] { "BusinessId", "LocationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryTransactions_BusinessId_LocationId_CreatedAtUtc",
                table: "InventoryTransactions",
                columns: new[] { "BusinessId", "LocationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_BusinessId_LocationId_CreatedAtUtc",
                table: "Expenses",
                columns: new[] { "BusinessId", "LocationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_OneDefaultPerBusiness",
                table: "Locations",
                column: "BusinessId",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLocationStocks_BusinessId_LocationId",
                table: "ProductLocationStocks",
                columns: new[] { "BusinessId", "LocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductLocationStocks_LocationId",
                table: "ProductLocationStocks",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLocationStocks_ProductId_LocationId",
                table: "ProductLocationStocks",
                columns: new[] { "ProductId", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLocations_LocationId",
                table: "UserLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLocations_UserId_LocationId",
                table: "UserLocations",
                columns: new[] { "UserId", "LocationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductLocationStocks");

            migrationBuilder.DropTable(
                name: "UserLocations");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Sales_BusinessId_LocationId_CreatedAtUtc",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_InventoryTransactions_BusinessId_LocationId_CreatedAtUtc",
                table: "InventoryTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_BusinessId_LocationId_CreatedAtUtc",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Stocktakes");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "StockHolds");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "ProductBatches");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "InventoryTransactions");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "DailySummaries");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "ContactIdentities");

            migrationBuilder.DropColumn(
                name: "DefaultLocationId",
                table: "Businesses");
        }
    }
}
