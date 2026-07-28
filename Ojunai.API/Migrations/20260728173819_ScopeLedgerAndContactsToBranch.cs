using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class ScopeLedgerAndContactsToBranch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Contacts",
                type: "uuid",
                nullable: true);

            // Backfill: tie every EXISTING (unassigned) debt to its business's default/Main location — "the
            // initial location before branches were added". After this, only entries created under "All
            // locations" going forward stay null. Idempotent (only touches LocationId IS NULL rows).
            migrationBuilder.Sql(@"
                UPDATE ""LedgerEntries"" le
                SET ""LocationId"" = b.""DefaultLocationId""
                FROM ""Businesses"" b
                WHERE le.""BusinessId"" = b.""Id""
                  AND le.""LocationId"" IS NULL
                  AND b.""DefaultLocationId"" IS NOT NULL;");

            // Backfill: tag every existing contact with its business's default/Main location as its origin branch.
            migrationBuilder.Sql(@"
                UPDATE ""Contacts"" c
                SET ""LocationId"" = b.""DefaultLocationId""
                FROM ""Businesses"" b
                WHERE c.""BusinessId"" = b.""Id""
                  AND c.""LocationId"" IS NULL
                  AND b.""DefaultLocationId"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Contacts");
        }
    }
}
