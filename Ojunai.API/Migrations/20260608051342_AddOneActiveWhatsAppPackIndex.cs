using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddOneActiveWhatsAppPackIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive cleanup BEFORE the unique index: this is the bug the index prevents, so
            // pre-existing data may already have >1 active whatsapp_pack for a business — which would
            // make the index build FAIL. Keep the newest active pack per business (by AddedAtUtc,
            // tie-broken by Id) and cancel the rest. No-op when there are no duplicates.
            migrationBuilder.Sql(@"
                UPDATE ""BusinessAddOns"" b
                SET ""Status"" = 'cancelled',
                    ""CancelledAtUtc"" = now(),
                    ""UpdatedAtUtc"" = now()
                WHERE b.""Status"" = 'active'
                  AND b.""AddOnCode"" LIKE 'whatsapp_pack.%'
                  AND b.""Id"" <> (
                      SELECT b2.""Id""
                      FROM ""BusinessAddOns"" b2
                      WHERE b2.""BusinessId"" = b.""BusinessId""
                        AND b2.""Status"" = 'active'
                        AND b2.""AddOnCode"" LIKE 'whatsapp_pack.%'
                      ORDER BY b2.""AddedAtUtc"" DESC, b2.""Id"" DESC
                      LIMIT 1
                  );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessAddOns_OneActiveWhatsAppPack",
                table: "BusinessAddOns",
                column: "BusinessId",
                unique: true,
                filter: "\"Status\" = 'active' AND \"AddOnCode\" LIKE 'whatsapp_pack.%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BusinessAddOns_OneActiveWhatsAppPack",
                table: "BusinessAddOns");
        }
    }
}
