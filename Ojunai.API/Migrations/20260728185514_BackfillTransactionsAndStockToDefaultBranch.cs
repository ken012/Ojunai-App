using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class BackfillTransactionsAndStockToDefaultBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ties every EXISTING transaction to each business's default/Main location so it shows under Main
            // instead of only under "All locations". The multi-location rollout added these LocationId columns
            // but never backfilled them (only Ledger + Contacts were backfilled later), so historical rows are
            // all null and hidden by the strict branch filter. Companion to 20260728173819_ScopeLedgerAndContacts.
            //
            // Every statement is idempotent: the UPDATEs only touch LocationId IS NULL rows, the INSERT only adds
            // a Main stock row where none exists — safe to re-run, and skips businesses with no DefaultLocationId
            // (single-location / not-yet-primed → their reads never scope, so they're unaffected either way).

            foreach (var table in new[] { "Sales", "Expenses", "InventoryTransactions", "PurchaseOrders", "StockHolds", "Stocktakes" })
            {
                migrationBuilder.Sql($@"
                    UPDATE ""{table}"" x
                    SET ""LocationId"" = b.""DefaultLocationId""
                    FROM ""Businesses"" b
                    WHERE x.""BusinessId"" = b.""Id""
                      AND x.""LocationId"" IS NULL
                      AND b.""DefaultLocationId"" IS NOT NULL;");
            }

            // Ensure every product has a stock row at its business's default location = its current stock, so a
            // pre-split product shows its full quantity under Main (and 0 under a branch that has no row yet).
            // SUM(per-location stock) == Product.CurrentStock holds by construction. Mirrors step 3 of
            // scripts/backfill-multi-location.sql.
            migrationBuilder.Sql(@"
                INSERT INTO ""ProductLocationStocks""
                    (""Id"", ""BusinessId"", ""ProductId"", ""LocationId"", ""CurrentStock"", ""LowStockThreshold"")
                SELECT gen_random_uuid(), p.""BusinessId"", p.""Id"", b.""DefaultLocationId"", p.""CurrentStock"", NULL
                FROM ""Products"" p
                JOIN ""Businesses"" b ON b.""Id"" = p.""BusinessId""
                WHERE b.""DefaultLocationId"" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM ""ProductLocationStocks"" pls
                      WHERE pls.""ProductId"" = p.""Id"" AND pls.""LocationId"" = b.""DefaultLocationId"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
