using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ojunai.API.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLogLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "ActivityLogEntries",
                type: "uuid",
                nullable: true);

            // Tie existing audit rows to each business's default/Main branch so they show under Main (not only
            // under "All locations"), matching the transaction backfill. Idempotent; skips businesses without a
            // default (single-location / un-primed → their reads never scope anyway).
            migrationBuilder.Sql(@"
                UPDATE ""ActivityLogEntries"" a
                SET ""LocationId"" = b.""DefaultLocationId""
                FROM ""Businesses"" b
                WHERE a.""BusinessId"" = b.""Id""
                  AND a.""LocationId"" IS NULL
                  AND b.""DefaultLocationId"" IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "ActivityLogEntries");
        }
    }
}
